using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MG.BusinessPlatform.Infrastructure.Persistence;

namespace MG.BusinessPlatform.Infrastructure.Services;

public sealed record LegacySqliteProductImportResult(
    string SourceTable,
    ProductCsvImportResult ImportResult);

/// <summary>
/// Reads a legacy SQLite catalog without modifying the source database, maps common
/// product column names into the supported CSV import format, then delegates all
/// validation and target writes to ProductCsvImportService.
/// </summary>
public sealed class LegacySqliteProductImportService(BusinessDbContext targetDb)
{
    private static readonly string[] SkuAliases =
        ["sku", "barcode", "code", "productcode", "itemcode", "کدکالا", "بارکد", "کد"];
    private static readonly string[] NameAliases =
        ["name", "productname", "title", "itemname", "displayname", "نام", "نامکالا", "عنوان"];
    private static readonly string[] PriceAliases =
        ["unitprice", "price", "saleprice", "sellingprice", "sellprice", "retailprice", "قیمت", "قیمتفروش"];
    private static readonly string[] CostAliases =
        ["unitcost", "cost", "costprice", "purchaseprice", "buyprice", "بهایواحد", "بها", "قیمتخرید"];
    private static readonly string[] CurrencyAliases =
        ["currencycode", "currency", "ارز", "واحدپول"];
    private static readonly string[] StockAliases =
        ["stockquantity", "stock", "quantity", "qty", "inventory", "موجودی", "تعداد"];
    private static readonly string[] AvailabilityAliases =
        ["isavailable", "isactive", "active", "enabled", "فعال", "وضعیت"];
    private static readonly string[] DescriptionAliases =
        ["description", "details", "notes", "شرح", "توضیحات"];
    private static readonly string[] IdAliases =
        ["id", "productid", "itemid", "شناسه", "شناسهکالا"];

    public async Task<LegacySqliteProductImportResult> ImportAsync(
        string sourceDatabasePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDatabasePath);
        var sourcePath = Path.GetFullPath(sourceDatabasePath);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Legacy SQLite database was not found.", sourcePath);

        var targetConnectionString = targetDb.Database.GetConnectionString();
        if (!string.IsNullOrWhiteSpace(targetConnectionString))
        {
            var targetDataSource = new SqliteConnectionStringBuilder(targetConnectionString).DataSource;
            if (!string.IsNullOrWhiteSpace(targetDataSource) && !string.Equals(targetDataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
            {
                var targetPath = Path.GetFullPath(targetDataSource);
                if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The source database is the active MG Business Platform database. Choose a separate legacy database file.");
            }
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString();

        await using var source = new SqliteConnection(connectionString);
        await source.OpenAsync(cancellationToken);
        var candidates = await FindProductTablesAsync(source, cancellationToken);
        if (candidates.Count == 0)
            throw new InvalidDataException("No product table with recognizable name and price columns was found. Export the legacy catalog to CSV and use the CSV import option.");
        var bestScore = candidates.Max(candidate => candidate.Score);
        var best = candidates.Where(candidate => candidate.Score == bestScore).ToArray();
        if (best.Length > 1)
            throw new InvalidDataException($"More than one product table matched: {string.Join(", ", best.Select(candidate => candidate.Name))}. Use CSV import to choose and normalize the source table.");

        var selected = best[0];
        var csv = await BuildCsvAsync(source, selected, cancellationToken);
        var importResult = await new ProductCsvImportService(targetDb).ImportAsync(csv, cancellationToken);
        return new LegacySqliteProductImportResult(selected.Name, importResult);
    }

    private static async Task<List<ProductTableCandidate>> FindProductTablesAsync(
        SqliteConnection source,
        CancellationToken cancellationToken)
    {
        var names = new List<string>();
        await using (var command = source.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                names.Add(reader.GetString(0));
        }

        var candidates = new List<ProductTableCandidate>();
        foreach (var name in names)
        {
            var columns = new List<string>();
            await using (var command = source.CreateCommand())
            {
                command.CommandText = $"PRAGMA table_info({QuoteIdentifier(name)});";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    columns.Add(reader.GetString(1));
            }

            var nameColumn = FindColumn(columns, NameAliases);
            var priceColumn = FindColumn(columns, PriceAliases);
            if (nameColumn is null || priceColumn is null)
                continue;

            var skuColumn = FindColumn(columns, SkuAliases);
            var idColumn = FindColumn(columns, IdAliases);
            var score = 5;
            var normalizedTableName = Normalize(name);
            if (normalizedTableName.Contains("product", StringComparison.Ordinal)
                || normalizedTableName.Contains("کالا", StringComparison.Ordinal))
                score += 5;
            else if (normalizedTableName.Contains("item", StringComparison.Ordinal)
                || normalizedTableName.Contains("menu", StringComparison.Ordinal))
                score += 3;
            if (skuColumn is not null) score += 2;
            if (FindColumn(columns, StockAliases) is not null) score++;
            if (FindColumn(columns, CostAliases) is not null) score++;
            candidates.Add(new ProductTableCandidate(name, columns, score, skuColumn, idColumn, nameColumn, priceColumn));
        }

        return candidates;
    }

    private static async Task<string> BuildCsvAsync(
        SqliteConnection source,
        ProductTableCandidate candidate,
        CancellationToken cancellationToken)
    {
        var csv = new StringBuilder();
        csv.AppendLine("SKU,Name,Description,UnitPrice,UnitCost,CurrencyCode,StockQuantity,IsAvailable");
        var readCount = 0;

        await using var command = source.CreateCommand();
        command.CommandText = $"SELECT * FROM {QuoteIdentifier(candidate.Name)};";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var skuOrdinal = Ordinal(reader, candidate.SkuColumn);
        var idOrdinal = Ordinal(reader, candidate.IdColumn);
        var nameOrdinal = Ordinal(reader, candidate.NameColumn);
        var priceOrdinal = Ordinal(reader, candidate.PriceColumn);
        var costOrdinal = Ordinal(reader, FindColumn(candidate.Columns, CostAliases));
        var currencyOrdinal = Ordinal(reader, FindColumn(candidate.Columns, CurrencyAliases));
        var stockOrdinal = Ordinal(reader, FindColumn(candidate.Columns, StockAliases));
        var availableOrdinal = Ordinal(reader, FindColumn(candidate.Columns, AvailabilityAliases));
        var descriptionOrdinal = Ordinal(reader, FindColumn(candidate.Columns, DescriptionAliases));

        while (await reader.ReadAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            readCount++;
            if (readCount > 10000)
                throw new InvalidDataException("Legacy product table exceeds the 10,000 row import limit. Export a filtered subset to CSV first.");

            var sku = ReadValue(reader, skuOrdinal);
            if (string.IsNullOrWhiteSpace(sku))
            {
                var legacyId = ReadValue(reader, idOrdinal);
                sku = string.IsNullOrWhiteSpace(legacyId)
                    ? $"LEGACY-ROW-{readCount:D6}"
                    : CreateLegacySku(legacyId);
            }

            var values = new[]
            {
                sku,
                ReadValue(reader, nameOrdinal),
                ReadValue(reader, descriptionOrdinal),
                ReadValue(reader, priceOrdinal),
                ReadValue(reader, costOrdinal, "0"),
                ReadValue(reader, currencyOrdinal, "IRR"),
                ReadValue(reader, stockOrdinal, "0"),
                ReadValue(reader, availableOrdinal, "true")
            };
            csv.AppendLine(string.Join(",", values.Select(QuoteCsv)));
            if (csv.Length > 10 * 1024 * 1024)
                throw new InvalidDataException("Mapped legacy product data exceeds the 10 MB import limit. Export a smaller subset to CSV.");
        }

        if (readCount == 0)
            throw new InvalidDataException($"Product table '{candidate.Name}' contains no rows.");
        return csv.ToString();
    }

    private static int Ordinal(SqliteDataReader reader, string? column)
    {
        if (column is null)
            return -1;
        try { return reader.GetOrdinal(column); }
        catch (IndexOutOfRangeException) { return -1; }
    }

    private static string ReadValue(SqliteDataReader reader, int ordinal, string fallback = "")
    {
        if (ordinal < 0 || reader.IsDBNull(ordinal))
            return fallback;
        var value = reader.GetValue(ordinal);
        return value switch
        {
            bool boolean => boolean ? "true" : "false",
            double number => number.ToString("0.############################", CultureInfo.InvariantCulture),
            float number => number.ToString("0.############################", CultureInfo.InvariantCulture),
            decimal number => number.ToString(CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? fallback
        };
    }

    private static string? FindColumn(IEnumerable<string> columns, IReadOnlyCollection<string> aliases)
    {
        var aliasSet = aliases.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return columns.FirstOrDefault(column => aliasSet.Contains(Normalize(column)));
    }

    private static string Normalize(string value) =>
        value.Trim().Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

    private static string QuoteIdentifier(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string QuoteCsv(string? value) =>
        "\"" + (value ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string CreateLegacySku(string legacyId)
    {
        var normalized = new string(legacyId.Trim().Select(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' ? character : '-').ToArray()).Trim('-');
        if (normalized.Length > 70)
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(legacyId)))[..12];
            normalized = normalized[..55] + "-" + hash;
        }
        if (normalized.Length == 0)
            normalized = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(legacyId)))[..12];
        return "LEGACY-" + normalized;
    }

    private sealed record ProductTableCandidate(
        string Name,
        IReadOnlyList<string> Columns,
        int Score,
        string? SkuColumn,
        string? IdColumn,
        string NameColumn,
        string PriceColumn);
}
