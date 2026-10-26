using Microsoft.EntityFrameworkCore;

namespace MG.BusinessPlatform.Infrastructure.Persistence;

public sealed record PurchaseInvoiceLineRequest(
    Guid ProductId,
    int Quantity,
    decimal UnitCost,
    decimal DiscountRate = 0m,
    string UnitCode = "EA");

/// <summary>
/// Records supplier receipts and payments as atomic inventory and double-entry ledger operations.
/// Supplier invoices increase inventory and accounts payable; supplier payments reduce payable
/// and credit the actual cash, card-clearing or bank account selected by the operator.
/// </summary>
public sealed class PurchasingAccountingService(BusinessDbContext db)
{
    public Task<PurchaseInvoiceRecord> CreatePurchaseInvoiceAsync(
        Guid supplierId,
        Guid branchId,
        IReadOnlyCollection<PurchaseInvoiceLineRequest> requestedLines,
        DateOnly invoiceDate,
        CancellationToken cancellationToken = default) =>
        CreatePurchaseInvoiceAsync(supplierId, branchId, requestedLines, invoiceDate,
            dueDate: null, cancellationToken: cancellationToken);

    public async Task<PurchaseInvoiceRecord> CreatePurchaseInvoiceAsync(
        Guid supplierId,
        Guid branchId,
        IReadOnlyCollection<PurchaseInvoiceLineRequest> requestedLines,
        DateOnly invoiceDate,
        DateOnly? dueDate,
        CancellationToken cancellationToken = default)
    {
        if (dueDate is DateOnly due && due < invoiceDate)
            throw new ArgumentException("Purchase invoice due date cannot precede its issue date.", nameof(dueDate));
        if (supplierId == Guid.Empty) throw new ArgumentException("Supplier ID is required.", nameof(supplierId));
        if (branchId == Guid.Empty) throw new ArgumentException("Branch ID is required.", nameof(branchId));
        ArgumentNullException.ThrowIfNull(requestedLines);
        if (requestedLines.Count is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(requestedLines), "A purchase invoice must contain between 1 and 500 lines.");

        foreach (var line in requestedLines)
        {
            if (line.ProductId == Guid.Empty) throw new ArgumentException("Every purchase line needs a product.", nameof(requestedLines));
            if (line.Quantity is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(requestedLines), "Quantity must be between 1 and 10000.");
            if (line.UnitCost < 0m) throw new ArgumentOutOfRangeException(nameof(requestedLines), "Unit cost cannot be negative.");
            if (line.DiscountRate is < 0m or > 1m) throw new ArgumentOutOfRangeException(nameof(requestedLines), "Discount rate must be between 0 and 1.");
            if (string.IsNullOrWhiteSpace(line.UnitCode) || line.UnitCode.Trim().Length > 20)
                throw new ArgumentException("Unit code is required and must not exceed 20 characters.", nameof(requestedLines));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var supplier = await db.Suppliers.SingleOrDefaultAsync(x => x.Id == supplierId, cancellationToken)
            ?? throw new KeyNotFoundException("Supplier was not found.");
        if (!supplier.IsActive)
            throw new InvalidOperationException("An inactive supplier cannot be used for a new purchase invoice.");

        var productIds = requestedLines.Select(x => x.ProductId).Distinct().ToArray();
        var products = await db.StoreProducts.Where(x => productIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        if (products.Count != productIds.Length)
            throw new KeyNotFoundException("One or more purchase products were not found.");

        var currency = products.Values.First().CurrencyCode.Trim().ToUpperInvariant();
        if (currency.Length != 3 || products.Values.Any(x => !string.Equals(x.CurrencyCode, currency, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("All products on a supplier invoice must use the same three-letter currency.");

        var lineDrafts = new List<(PurchaseInvoiceLineRequest Request, StoreProduct Product, decimal NetTotal, decimal EffectiveUnitCost)>();
        decimal total = 0m;
        foreach (var request in requestedLines)
        {
            var product = products[request.ProductId];
            if (product.StockQuantity < 0 || product.UnitCost < 0m)
                throw new InvalidOperationException($"Product '{product.Name}' has invalid stock or cost data.");
            var gross = checked(request.UnitCost * request.Quantity);
            var discount = checked(gross * request.DiscountRate);
            var net = checked(gross - discount);
            var effectiveUnitCost = net / request.Quantity;
            total = checked(total + net);
            lineDrafts.Add((request, product, net, effectiveUnitCost));
        }

        if (total <= 0m)
            throw new InvalidOperationException("A purchase invoice total must be greater than zero.");

        var accounts = await LoadPostingAccountsAsync(["1200", "2000"], cancellationToken);
        var invoiceId = Guid.NewGuid();
        var invoiceNumber = $"PUR-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..29].ToUpperInvariant();
        var now = DateTimeOffset.UtcNow;
        var invoice = new PurchaseInvoiceRecord
        {
            Id = invoiceId,
            InvoiceNumber = invoiceNumber,
            BranchId = branchId,
            SupplierId = supplierId,
            IssueDate = invoiceDate,
            DueDate = dueDate,
            CurrencyCode = currency,
            TotalAmount = total,
            PaidAmount = 0m,
            Status = "Received",
            CreatedAt = now
        };

        // Apply stock and weighted-average cost once per product. A supplier invoice
        // may contain multiple lines for the same SKU; updating from the original
        // quantity for each line would overwrite earlier receipts and misstate stock.
        foreach (var group in lineDrafts.GroupBy(draft => draft.Product.Id))
        {
            var product = group.First().Product;
            var receivedQuantity = group.Aggregate(0, (sum, draft) => checked(sum + draft.Request.Quantity));
            var incomingValue = group.Aggregate(0m, (sum, draft) => checked(sum + draft.NetTotal));
            var previousQuantity = product.StockQuantity;
            var newQuantity = checked(previousQuantity + receivedQuantity);
            var previousValue = checked(previousQuantity * product.UnitCost);
            product.StockQuantity = newQuantity;
            product.UnitCost = checked(previousValue + incomingValue) / newQuantity;
            product.UpdatedAt = now;
        }

        foreach (var draft in lineDrafts)
        {
            var product = draft.Product;
            invoice.Lines.Add(new PurchaseInvoiceLineRecord
            {
                Id = Guid.NewGuid(),
                InvoiceId = invoice.Id,
                ProductId = product.Id,
                ProductName = product.Name,
                UnitCode = draft.Request.UnitCode.Trim().ToUpperInvariant(),
                Quantity = draft.Request.Quantity,
                UnitCost = draft.Request.UnitCost,
                DiscountRate = draft.Request.DiscountRate,
                LineTotal = draft.NetTotal
            });

            db.StockMovements.Add(new StockMovementRecord
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                OccurredAt = now,
                MovementType = "PurchaseReceipt",
                Quantity = draft.Request.Quantity,
                SignedQuantity = draft.Request.Quantity,
                UnitCost = draft.EffectiveUnitCost,
                CurrencyCode = currency,
                Reference = invoiceNumber,
                Note = $"Supplier invoice {invoiceNumber}"
            });
        }

        db.PurchaseInvoices.Add(invoice);
        db.JournalEntries.Add(CreateJournalEntry(
            $"ثبت خرید از تأمین‌کننده {supplier.DisplayName} - {invoiceNumber}",
            currency, "PurchaseInvoice", invoice.Id, invoiceDate,
            (accounts["1200"], "Debit", total),
            (accounts["2000"], "Credit", total)));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return invoice;
    }

    public async Task<SupplierPaymentRecord> RecordSupplierPaymentAsync(
        Guid purchaseInvoiceId,
        decimal amount,
        string method,
        string reference = "",
        string notes = "",
        DateTimeOffset? occurredAt = null,
        CancellationToken cancellationToken = default)
    {
        if (purchaseInvoiceId == Guid.Empty) throw new ArgumentException("Purchase invoice ID is required.", nameof(purchaseInvoiceId));
        if (amount <= 0m) throw new ArgumentOutOfRangeException(nameof(amount), "Payment amount must be positive.");
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(notes);
        if (method.Length > 30 || reference.Length > 100 || notes.Length > 500)
            throw new ArgumentOutOfRangeException(nameof(method), "Payment method, reference or notes exceed the supported length.");

        var normalizedMethod = method.Trim();
        var cashAccountCode = normalizedMethod switch
        {
            "Cash" => "1000",
            "Card" => "1010",
            "BankTransfer" => "1020",
            _ => throw new ArgumentException("Payment method must be Cash, Card or BankTransfer.", nameof(method))
        };

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var invoice = await db.PurchaseInvoices.SingleOrDefaultAsync(x => x.Id == purchaseInvoiceId, cancellationToken)
            ?? throw new KeyNotFoundException("Purchase invoice was not found.");
        if (invoice.Status == "Voided")
            throw new InvalidOperationException("A voided purchase invoice cannot receive payments.");
        var outstanding = checked(invoice.TotalAmount - invoice.PaidAmount);
        if (outstanding <= 0m)
            throw new InvalidOperationException("This supplier invoice is already fully paid.");
        if (amount > outstanding)
            throw new InvalidOperationException($"Payment exceeds the outstanding balance of {outstanding:N2} {invoice.CurrencyCode}.");
        
        var accounts = await LoadPostingAccountsAsync(["2000", cashAccountCode], cancellationToken);
        var paymentId = Guid.NewGuid();
        var now = (occurredAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var entryDate = DateOnly.FromDateTime(now.UtcDateTime);
        var entry = CreateJournalEntry(
            $"پرداخت به تأمین‌کننده بابت {invoice.InvoiceNumber}",
            invoice.CurrencyCode, "SupplierPayment", paymentId, entryDate,
            (accounts["2000"], "Debit", amount),
            (accounts[cashAccountCode], "Credit", amount));

        var payment = new SupplierPaymentRecord
        {
            Id = paymentId,
            PaymentNumber = $"SPY-{Guid.NewGuid():N}".ToUpperInvariant(),
            PurchaseInvoiceId = invoice.Id,
            JournalEntryId = entry.Id,
            OccurredAt = now,
            Method = normalizedMethod,
            Amount = amount,
            CurrencyCode = invoice.CurrencyCode,
            Reference = reference.Trim(),
            Notes = notes.Trim()
        };

        invoice.PaidAmount = checked(invoice.PaidAmount + amount);
        invoice.Status = invoice.PaidAmount == invoice.TotalAmount ? "Paid" : "PartiallyPaid";
        db.SupplierPayments.Add(payment);
        db.JournalEntries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return payment;
    }

    private async Task<Dictionary<string, LedgerAccountRecord>> LoadPostingAccountsAsync(
        string[] codes, CancellationToken cancellationToken)
    {
        var accounts = await db.LedgerAccounts.Where(x => codes.Contains(x.Code))
            .ToDictionaryAsync(x => x.Code, cancellationToken);
        foreach (var code in codes)
            if (!accounts.TryGetValue(code, out var account) || !account.IsActive || !account.IsPostingAllowed)
                throw new InvalidOperationException($"Ledger account {code} is missing, inactive, or not postable.");
        return accounts;
    }

    private static JournalEntryRecord CreateJournalEntry(
        string description, string currencyCode, string sourceType, Guid sourceId, DateOnly entryDate,
        params (LedgerAccountRecord Account, string Side, decimal Amount)[] lines)
    {
        if (lines.Length < 2 || lines.Any(line => line.Amount <= 0m))
            throw new ArgumentException("A journal entry requires at least two positive lines.", nameof(lines));
        var debit = lines.Where(line => line.Side == "Debit").Aggregate(0m, (sum, line) => checked(sum + line.Amount));
        var credit = lines.Where(line => line.Side == "Credit").Aggregate(0m, (sum, line) => checked(sum + line.Amount));
        if (debit <= 0m || debit != credit)
            throw new InvalidOperationException("Journal entry is not balanced.");

        var entry = new JournalEntryRecord
        {
            Id = Guid.NewGuid(),
            EntryNumber = $"JRN-{Guid.NewGuid():N}".ToUpperInvariant(),
            EntryDate = entryDate,
            Description = description,
            CurrencyCode = currencyCode,
            SourceType = sourceType,
            SourceId = sourceId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entry.Lines.AddRange(lines.Select(line => new JournalLineRecord
        {
            Id = Guid.NewGuid(),
            JournalEntryId = entry.Id,
            AccountId = line.Account.Id,
            AccountCode = line.Account.Code,
            AccountName = line.Account.Name,
            Side = line.Side,
            Amount = line.Amount
        }));
        return entry;
    }
}
