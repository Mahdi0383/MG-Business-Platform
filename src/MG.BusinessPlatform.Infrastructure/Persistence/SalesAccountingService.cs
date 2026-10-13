using Microsoft.EntityFrameworkCore;

namespace MG.BusinessPlatform.Infrastructure.Persistence;

public sealed class SalesAccountingService(BusinessDbContext db)
{
    public async Task<SalesInvoiceRecord> FulfillOrderAsync(
        Guid orderId,
        DateOnly issueDate,
        CancellationToken cancellationToken = default)
    {
        if (orderId == Guid.Empty) throw new ArgumentException("Order ID is required.", nameof(orderId));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var order = await db.CustomerOrders.Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken)
            ?? throw new KeyNotFoundException("Customer order was not found.");

        if (!string.Equals(order.Status, "Received", StringComparison.Ordinal))
            throw new InvalidOperationException("Only received, uninvoiced orders can be fulfilled.");
        if (await db.SalesInvoices.AnyAsync(x => x.OrderId == order.Id, cancellationToken))
            throw new InvalidOperationException("An invoice already exists for this order.");
        if (order.Lines.Count == 0)
            throw new InvalidOperationException("An order without lines cannot be invoiced.");

        var calculatedTotal = order.Lines.Aggregate(0m, (sum, line) => checked(sum + line.LineTotal));
        var costTotal = order.Lines.Aggregate(0m, (sum, line) => checked(sum + line.UnitCost * line.Quantity));
        if (calculatedTotal != order.TotalAmount)
            throw new InvalidOperationException("Order lines do not match the stored order total.");

        var requiredCodes = new List<string>();
        if (order.TotalAmount > 0m) { requiredCodes.Add("1100"); requiredCodes.Add("4000"); }
        if (costTotal > 0m) { requiredCodes.Add("5000"); requiredCodes.Add("1200"); }
        var accounts = await db.LedgerAccounts
            .Where(account => requiredCodes.Contains(account.Code))
            .ToDictionaryAsync(account => account.Code, cancellationToken);
        foreach (var code in requiredCodes.Distinct(StringComparer.Ordinal))
            EnsurePostingAccount(accounts, code);

        var invoiceId = Guid.NewGuid();
        var invoiceNumber = $"INV-{order.OrderNumber}";
        var invoice = new SalesInvoiceRecord
        {
            Id = invoiceId,
            InvoiceNumber = invoiceNumber,
            OrderId = order.Id,
            IssueDate = issueDate,
            CustomerName = order.CustomerName,
            CustomerPhone = order.CustomerPhone,
            CurrencyCode = order.CurrencyCode,
            TotalAmount = order.TotalAmount,
            CostTotal = costTotal,
            PaidAmount = 0m,
            Status = "Issued",
            CreatedAt = DateTimeOffset.UtcNow,
            Lines = order.Lines.Select(line => new SalesInvoiceLineRecord
            {
                Id = Guid.NewGuid(),
                InvoiceId = invoiceId,
                ProductId = line.ProductId,
                ProductName = line.ProductName,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                UnitCost = line.UnitCost,
                LineTotal = line.LineTotal,
                CostTotal = checked(line.UnitCost * line.Quantity)
            }).ToList()
        };
        db.SalesInvoices.Add(invoice);

        if (order.TotalAmount > 0m)
        {
            var salesEntry = CreateJournalEntry(
                $"ثبت فروش فاکتور {invoiceNumber}", order.CurrencyCode, "SalesInvoice", invoiceId, issueDate,
                (accounts["1100"], "Debit", order.TotalAmount),
                (accounts["4000"], "Credit", order.TotalAmount));
            db.JournalEntries.Add(salesEntry);
        }

        if (costTotal > 0m)
        {
            var costEntry = CreateJournalEntry(
                $"ثبت بهای تمام‌شده فاکتور {invoiceNumber}", order.CurrencyCode, "InventoryCOGS", invoiceId, issueDate,
                (accounts["5000"], "Debit", costTotal),
                (accounts["1200"], "Credit", costTotal));
            db.JournalEntries.Add(costEntry);
        }

        order.Status = "Fulfilled";
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return invoice;
    }

    public async Task<PaymentRecord> RecordPaymentAsync(
        Guid invoiceId,
        decimal amount,
        string method,
        string reference,
        string notes,
        DateTimeOffset occurredAt,
        DateOnly entryDate,
        CancellationToken cancellationToken = default)
    {
        if (invoiceId == Guid.Empty) throw new ArgumentException("Invoice ID is required.", nameof(invoiceId));
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(notes);
        if (amount <= 0m) throw new ArgumentOutOfRangeException(nameof(amount), "Payment amount must be positive.");
        if (reference.Length > 100) throw new ArgumentOutOfRangeException(nameof(reference));
        if (notes.Length > 500) throw new ArgumentOutOfRangeException(nameof(notes));

        var accountCode = method switch
        {
            "Cash" => "1000",
            "Card" => "1010",
            "Bank" => "1020",
            _ => throw new ArgumentException("Unsupported payment method.", nameof(method))
        };

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var invoice = await db.SalesInvoices.SingleOrDefaultAsync(x => x.Id == invoiceId, cancellationToken)
            ?? throw new KeyNotFoundException("Sales invoice was not found.");
        var outstanding = checked(invoice.TotalAmount - invoice.PaidAmount);
        if (invoice.Status == "Voided" || outstanding <= 0m)
            throw new InvalidOperationException("The invoice has no outstanding amount.");
        if (amount > outstanding)
            throw new InvalidOperationException($"Payment exceeds the outstanding balance of {outstanding} {invoice.CurrencyCode}.");

        var cashAccount = await db.LedgerAccounts.SingleOrDefaultAsync(x => x.Code == accountCode, cancellationToken);
        var receivableAccount = await db.LedgerAccounts.SingleOrDefaultAsync(x => x.Code == "1100", cancellationToken);
        if (cashAccount is null || receivableAccount is null)
            throw new InvalidOperationException("The cash/bank or accounts-receivable ledger account is missing.");
        EnsurePostingAccount(new Dictionary<string, LedgerAccountRecord>(StringComparer.Ordinal)
        {
            [cashAccount.Code] = cashAccount,
            [receivableAccount.Code] = receivableAccount
        }, accountCode);
        EnsurePostingAccount(new Dictionary<string, LedgerAccountRecord>(StringComparer.Ordinal)
        {
            [receivableAccount.Code] = receivableAccount
        }, "1100");

        var paymentId = Guid.NewGuid();
        var entry = CreateJournalEntry(
            $"ثبت دریافت فاکتور {invoice.InvoiceNumber}", invoice.CurrencyCode, "Payment", paymentId, entryDate,
            (cashAccount, "Debit", amount),
            (receivableAccount, "Credit", amount));
        var payment = new PaymentRecord
        {
            Id = paymentId,
            PaymentNumber = $"PAY-{Guid.NewGuid():N}".ToUpperInvariant(),
            InvoiceId = invoice.Id,
            JournalEntryId = entry.Id,
            OccurredAt = occurredAt,
            Method = method,
            Amount = amount,
            CurrencyCode = invoice.CurrencyCode,
            Reference = reference.Trim(),
            Notes = notes.Trim()
        };

        invoice.PaidAmount = checked(invoice.PaidAmount + amount);
        invoice.Status = invoice.PaidAmount == invoice.TotalAmount ? "Paid" : "PartiallyPaid";
        db.JournalEntries.Add(entry);
        db.Payments.Add(payment);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return payment;
    }

    private static void EnsurePostingAccount(IReadOnlyDictionary<string, LedgerAccountRecord> accounts, string code)
    {
        if (!accounts.TryGetValue(code, out var account) || !account.IsActive || !account.IsPostingAllowed)
            throw new InvalidOperationException($"Ledger account {code} is missing, inactive, or not postable.");
    }

    private static JournalEntryRecord CreateJournalEntry(
        string description,
        string currencyCode,
        string sourceType,
        Guid sourceId,
        DateOnly entryDate,
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
