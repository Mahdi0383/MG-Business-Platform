using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MG.BusinessPlatform.Infrastructure.Persistence;

namespace MG.BusinessPlatform.Infrastructure.Services;

public sealed record ProductCsvImportIssue(int RowNumber, string Message);

public sealed record ProductCsvImportResult(
    int ImportedCount,
    int SkippedExistingCount,
    IReadOnlyList<ProductCsvImportIssue> Issues);

/// <summary>
/// Imports a deliberately small, documented product CSV schema into the local catalog.
/// The import is all-or-nothing for malformed rows, skips existing SKUs, and records
/// opening-stock movements rather than silently changing the inventory balance.
/// </summary>
public sealed class ProductCsvImportService(BusinessDbContext db)
{
    private const int MaximumCsvCharacters = 10 * 1024 * 1024;
    private const int MaximumDataRows = 10000;

    public async Task<ProductCsvImportResult> ImportAsync(
        string csvText,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(csvText);
        if (csvText.Length == 0)
            throw new InvalidDataException("CSV file is empty.");
        if (csvText.Length > MaximumCsvCharacters)
            throw new InvalidDataException("CSV file exceeds the 10 MB import limit.");

        var records = ParseCsv(csvText);
        if (records.Count < 2)
            throw new InvalidDataException("CSV must contain a header row and at least one product row.");
        if (records.Count - 1 > MaximumDataRows)
            throw new InvalidDataException($"CSV exceeds the {MaximumDataRows:N0} product row limit.");

        var headers = records[0].Fields.Select(NormalizeHeader).ToArray();
        var skuColumn = FindColumn(headers, "sku", "کد کالا", "کدکالا", "کد محصول");
        var nameColumn = FindColumn(headers, "name", "productname", "نام", "نام محصول", "نامکالا");
        var priceColumn = FindColumn(headers, "unitprice", "price", "قیمت فروش", "قیمت", "نرخ");
        if (skuColumn < 0 || nameColumn < 0 || priceColumn < 0)
            throw new InvalidDataException(
                "Required CSV columns are SKU (کد کالا), Name (نام محصول), and UnitPrice (قیمت فروش).");

        var costColumn = FindColumn(headers, "unitcost", "cost", "بهای واحد", "بهای تمام شده", "بهای تمام‌شده");
        var currencyColumn = FindColumn(headers, "currencycode", "currency", "واحد پول", "ارز");
        var stockColumn = FindColumn(headers, "stockquantity", "stock", "موجودی", "تعداد موجودی");
        var availableColumn = FindColumn(headers, "isavailable", "available", "فعال", "وضعیت");
        var descriptionColumn = FindColumn(headers, "description", "توضیحات", "شرح");

        var issues = new List<ProductCsvImportIssue>();
        var products = new List<StoreProduct>();
        var seenSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records.Skip(1))
        {
            if (record.Fields.All(string.IsNullOrWhiteSpace))
                continue;
            if (record.Fields.Count > headers.Length)
            {
                issues.Add(new(record.RowNumber, "تعداد ستون‌های ردیف از تعداد ستون‌های سرستون بیشتر است."));
                continue;
            }

            string Read(int column, string fallback = "") =>
                column >= 0 && column < record.Fields.Count ? record.Fields[column].Trim() : fallback;

            var sku = Read(skuColumn);
            var name = Read(nameColumn);
            var description = Read(descriptionColumn);
            var currency = Read(currencyColumn, "IRR").ToUpperInvariant();
            var rawPrice = Read(priceColumn);
            var rawCost = Read(costColumn, "0");
            var rawStock = Read(stockColumn, "0");
            var rawAvailable = Read(availableColumn, "true");

            var rowIssues = new List<string>();
            if (string.IsNullOrWhiteSpace(sku) || sku.Length > 80)
                rowIssues.Add("کد کالا الزامی است و حداکثر ۸۰ نویسه دارد.");
            else if (!seenSkus.Add(sku))
                rowIssues.Add($"کد کالا «{sku}» در همین فایل تکراری است.");
            if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
                rowIssues.Add("نام کالا الزامی است و حداکثر ۲۰۰ نویسه دارد.");
            if (description.Length > 2000)
                rowIssues.Add("توضیحات بیش از ۲۰۰۰ نویسه است.");
            if (currency.Length != 3 || currency.Any(ch => ch is < 'A' or > 'Z'))
                rowIssues.Add("واحد پول باید کد سه‌حرفی لاتین مانند IRR یا USD باشد.");
            if (!decimal.TryParse(rawPrice, NumberStyles.Number, CultureInfo.InvariantCulture, out var price) || price < 0m)
                rowIssues.Add("قیمت فروش باید عدد معتبر و غیرمنفی باشد.");
            if (!decimal.TryParse(rawCost, NumberStyles.Number, CultureInfo.InvariantCulture, out var cost) || cost < 0m)
                rowIssues.Add("بهای واحد باید عدد معتبر و غیرمنفی باشد.");
            if (!int.TryParse(rawStock, NumberStyles.Integer, CultureInfo.InvariantCulture, out var stock) || stock < 0)
                rowIssues.Add("موجودی باید عدد صحیح و غیرمنفی باشد.");

            var isAvailable = true;
            if (!TryParseAvailability(rawAvailable, out isAvailable))
                rowIssues.Add("وضعیت فعال باید true/false، 1/0 یا فعال/غیرفعال باشد.");

            if (rowIssues.Count > 0)
            {
                issues.Add(new(record.RowNumber, string.Join(" ", rowIssues)));
                continue;
            }

            products.Add(new StoreProduct
            {
                Id = Guid.NewGuid(),
                Sku = sku,
                Name = name,
                Description = description,
                UnitPrice = price,
                UnitCost = cost,
                CurrencyCode = currency,
                IsAvailable = isAvailable,
                StockQuantity = stock,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        if (issues.Count > 0)
            return new ProductCsvImportResult(0, 0, issues);
        if (products.Count == 0)
            throw new InvalidDataException("CSV contains no product rows.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var existingSkus = (await db.StoreProducts.AsNoTracking()
                .Select(product => product.Sku)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toImport = products.Where(product => !existingSkus.Contains(product.Sku)).ToArray();
        var skipped = products.Count - toImport.Length;
        if (toImport.Length == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return new ProductCsvImportResult(0, skipped, []);
        }

        var now = DateTimeOffset.UtcNow;
        db.StoreProducts.AddRange(toImport);
        db.StockMovements.AddRange(toImport.Where(product => product.StockQuantity > 0).Select(product =>
            new StockMovementRecord
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                OccurredAt = now,
                MovementType = "OpeningBalance",
                Quantity = product.StockQuantity,
                SignedQuantity = product.StockQuantity,
                UnitCost = product.UnitCost,
                CurrencyCode = product.CurrencyCode,
                Reference = "CSV-IMPORT",
                Note = "Opening stock imported from product CSV"
            }));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ProductCsvImportResult(toImport.Length, skipped, []);
    }

    private static int FindColumn(IReadOnlyList<string> headers, params string[] aliases)
    {
        var aliasSet = aliases.Select(NormalizeHeader).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count; i++)
            if (aliasSet.Contains(headers[i]))
                return i;
        return -1;
    }

    private static string NormalizeHeader(string value) =>
        value.Trim().TrimStart('\uFEFF').Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

    private static bool TryParseAvailability(string value, out bool result)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "true":
            case "1":
            case "yes":
            case "فعال":
                result = true;
                return true;
            case "false":
            case "0":
            case "no":
            case "غیرفعال":
                result = false;
                return true;
            default:
                result = true;
                return false;
        }
    }

    private static List<CsvRecord> ParseCsv(string text)
    {
        text = text.TrimStart('\uFEFF');
        var records = new List<CsvRecord>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var physicalLine = 1;
        var recordStartLine = 1;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (ch == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    field.Append(ch);
                    if (ch == '\n')
                        physicalLine++;
                }
                continue;
            }

            if (ch == '"' && field.Length == 0)
            {
                inQuotes = true;
            }
            else if (ch == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (ch is '\r' or '\n')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                fields.Add(field.ToString());
                field.Clear();
                records.Add(new CsvRecord(recordStartLine, fields));
                fields = [];
                physicalLine++;
                recordStartLine = physicalLine;
            }
            else
            {
                field.Append(ch);
            }
        }

        if (inQuotes)
            throw new InvalidDataException("CSV contains an unterminated quoted field.");
        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            records.Add(new CsvRecord(recordStartLine, fields));
        }

        return records;
    }

    private sealed record CsvRecord(int RowNumber, List<string> Fields);
}
