using Microsoft.EntityFrameworkCore;

namespace MG.BusinessPlatform.Infrastructure.Persistence;

public sealed record PosSaleLineRequest(Guid ProductId, int Quantity, decimal? ExpectedUnitPrice = null);

public sealed class SalesAccountingService(BusinessDbContext db)
{
    public async Task<SalesInvoiceRecord> FulfillOrderAsync(
        Guid orderId,
        DateOnly issueDate,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var invoice = await FulfillOrderCoreAsync(orderId, issueDate, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return invoice;
    }

    /// <summary>
    /// Creates a walk-in POS order, deducts stock, issues its invoice, posts the sales/COGS
    /// journals, and records full payment in one database transaction.
    /// </summary>
    public async Task<SalesInvoiceRecord> CreatePosSaleAsync(
        Guid productId,
        int quantity,
        string customerName,
        string customerPhone,
        string paymentMethod,
        string paymentReference,
        string paymentNotes,
        DateOnly saleDate,
        CancellationToken cancellationToken = default)
    {
        if (productId == Guid.Empty) throw new ArgumentException("Product ID is required.", nameof(productId));
        if (quantity is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (string.IsNullOrWhiteSpace(customerName) || customerName.Trim().Length > 160)
            throw new ArgumentException("Customer name is required and must not exceed 160 characters.", nameof(customerName));
        ArgumentNullException.ThrowIfNull(customerPhone);
        ArgumentNullException.ThrowIfNull(paymentMethod);
        ArgumentNullException.ThrowIfNull(paymentReference);
        ArgumentNullException.ThrowIfNull(paymentNotes);
        if (customerPhone.Length > 40) throw new ArgumentOutOfRangeException(nameof(customerPhone));
        if (paymentReference.Length > 100) throw new ArgumentOutOfRangeException(nameof(paymentReference));
        if (paymentNotes.Length > 500) throw new ArgumentOutOfRangeException(nameof(paymentNotes));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var product = await db.StoreProducts.SingleOrDefaultAsync(x => x.Id == productId, cancellationToken)
            ?? throw new KeyNotFoundException("Product was not found.");
        if (!product.IsAvailable)
            throw new InvalidOperationException("The selected product is unavailable.");
        if (product.StockQuantity < quantity)
            throw new InvalidOperationException($"Insufficient stock. Available quantity: {product.StockQuantity}.");
        if (product.UnitPrice < 0m || product.UnitCost < 0m)
            throw new InvalidOperationException("The selected product has a negative price or cost.");

        var total = checked(product.UnitPrice * quantity);
        var orderId = Guid.NewGuid();
        var orderNumber = $"POS-{Guid.NewGuid():N}".ToUpperInvariant();
        var order = new CustomerOrder
        {
            Id = orderId,
            OrderNumber = orderNumber,
            IdempotencyKey = $"POS-{Guid.NewGuid():N}".ToUpperInvariant(),
            CustomerName = customerName.Trim(),
            CustomerPhone = customerPhone.Trim(),
            DeliveryAddress = string.Empty,
            Notes = "POS sale",
            Status = "Received",
            CurrencyCode = product.CurrencyCode,
            TotalAmount = total,
            CreatedAt = DateTimeOffset.UtcNow,
            Lines =
            [
                new CustomerOrderLine
                {
                    Id = Guid.NewGuid(),
                    OrderId = orderId,
                    ProductId = product.Id,
                    ProductName = product.Name,
                    UnitPrice = product.UnitPrice,
                    UnitCost = product.UnitCost,
                    Quantity = quantity,
                    LineTotal = total
                }
            ]
        };

        var updatedAt = DateTimeOffset.UtcNow;
        var affected = await db.StoreProducts
            .Where(x => x.Id == product.Id && x.IsAvailable && x.StockQuantity >= quantity
                && x.UnitPrice == product.UnitPrice && x.UnitCost == product.UnitCost
                && x.CurrencyCode == product.CurrencyCode)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.StockQuantity, x => x.StockQuantity - quantity)
                .SetProperty(x => x.UpdatedAt, updatedAt), cancellationToken);
        if (affected != 1)
            throw new InvalidOperationException("Stock or price changed during checkout. Refresh the product and retry.");

        // ExecuteUpdate bypasses EF's change tracker. Synchronize the tracked entity so
        // subsequent reads through this DbContext observe the committed checkout quantity.
        product.StockQuantity -= quantity;
        product.UpdatedAt = updatedAt;

        db.CustomerOrders.Add(order);
        db.StockMovements.Add(new StockMovementRecord
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            OccurredAt = DateTimeOffset.UtcNow,
            MovementType = "PosSaleIssue",
            Quantity = quantity,
            SignedQuantity = -quantity,
            UnitCost = product.UnitCost,
            CurrencyCode = product.CurrencyCode,
            Reference = orderNumber,
            Note = "POS sale stock issue"
        });

        await db.SaveChangesAsync(cancellationToken);
        var invoice = await FulfillOrderCoreAsync(orderId, saleDate, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        if (invoice.TotalAmount > 0m)
        {
            await RecordPaymentCoreAsync(invoice.Id, invoice.TotalAmount, paymentMethod, paymentReference,
                paymentNotes, DateTimeOffset.UtcNow, saleDate, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return invoice;
    }

    public async Task<SalesInvoiceRecord> CreatePosSaleAsync(
        IReadOnlyCollection<PosSaleLineRequest> requestedLines,
        string customerName, string customerPhone, string paymentMethod,
        string paymentReference, string paymentNotes, DateOnly saleDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestedLines);
        if (requestedLines.Count is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(requestedLines), "A sale must contain 1-100 line requests.");
        if (string.IsNullOrWhiteSpace(customerName) || customerName.Trim().Length > 160)
            throw new ArgumentException("Customer name is required and must not exceed 160 characters.", nameof(customerName));
        ArgumentNullException.ThrowIfNull(customerPhone);
        ArgumentNullException.ThrowIfNull(paymentMethod);
        ArgumentNullException.ThrowIfNull(paymentReference);
        ArgumentNullException.ThrowIfNull(paymentNotes);
        if (customerPhone.Length > 40 || paymentReference.Length > 100 || paymentNotes.Length > 500)
            throw new ArgumentException("Customer phone, payment reference or notes exceed the allowed length.");

        var quantities = new Dictionary<Guid, int>();
        foreach (var line in requestedLines)
        {
            if (line.ProductId == Guid.Empty || line.Quantity is < 1 or > 10000)
                throw new ArgumentException("Every line must have a valid product and quantity between 1 and 10000.", nameof(requestedLines));
            quantities[line.ProductId] = checked(quantities.GetValueOrDefault(line.ProductId) + line.Quantity);
            if (quantities[line.ProductId] > 10000)
                throw new ArgumentException("Combined quantity for a product cannot exceed 10000.", nameof(requestedLines));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var ids = quantities.Keys.ToArray();
        var products = await db.StoreProducts.Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        if (products.Count != ids.Length)
            throw new KeyNotFoundException("One or more POS products were not found.");
        foreach (var requested in requestedLines)
            if (requested.ExpectedUnitPrice is decimal expected && products[requested.ProductId].UnitPrice != expected)
                throw new InvalidOperationException($"Price for '{products[requested.ProductId].Name}' changed. Refresh the cart and confirm the new total.");

        var ordered = ids.Select(id => products[id]).OrderBy(x => x.Sku, StringComparer.Ordinal).ToArray();
        var currency = ordered[0].CurrencyCode;
        decimal total = 0m;
        foreach (var product in ordered)
        {
            var quantity = quantities[product.Id];
            if (!product.IsAvailable || product.StockQuantity < quantity)
                throw new InvalidOperationException($"Product '{product.Name}' is unavailable or has insufficient stock.");
            if (!string.Equals(product.CurrencyCode, currency, StringComparison.Ordinal))
                throw new InvalidOperationException("A POS sale cannot mix currencies.");
            if (product.UnitPrice < 0m || product.UnitCost < 0m)
                throw new InvalidOperationException($"Product '{product.Name}' has a negative price or cost.");
            total = checked(total + checked(product.UnitPrice * quantity));
        }

        var orderId = Guid.NewGuid();
        var orderNumber = $"POS-{Guid.NewGuid():N}".ToUpperInvariant();
        var now = DateTimeOffset.UtcNow;
        var order = new CustomerOrder
        {
            Id = orderId, OrderNumber = orderNumber, IdempotencyKey = $"POS-{Guid.NewGuid():N}".ToUpperInvariant(),
            CustomerName = customerName.Trim(), CustomerPhone = customerPhone.Trim(), DeliveryAddress = string.Empty,
            Notes = "POS sale", Status = "Received", CurrencyCode = currency, TotalAmount = total, CreatedAt = now,
            Lines = ordered.Select(product => new CustomerOrderLine
            {
                Id = Guid.NewGuid(), OrderId = orderId, ProductId = product.Id, ProductName = product.Name,
                UnitPrice = product.UnitPrice, UnitCost = product.UnitCost, Quantity = quantities[product.Id],
                LineTotal = checked(product.UnitPrice * quantities[product.Id])
            }).ToList()
        };

        foreach (var product in ordered)
        {
            var quantity = quantities[product.Id];
            var affected = await db.StoreProducts.Where(x => x.Id == product.Id && x.IsAvailable
                    && x.StockQuantity >= quantity && x.UnitPrice == product.UnitPrice
                    && x.UnitCost == product.UnitCost && x.CurrencyCode == product.CurrencyCode)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.StockQuantity, x => x.StockQuantity - quantity)
                    .SetProperty(x => x.UpdatedAt, now), cancellationToken);
            if (affected != 1)
                throw new InvalidOperationException($"Stock or price for '{product.Name}' changed during checkout.");
            product.StockQuantity -= quantity;
            product.UpdatedAt = now;
            db.StockMovements.Add(new StockMovementRecord
            {
                Id = Guid.NewGuid(), ProductId = product.Id, OccurredAt = now, MovementType = "PosSaleIssue",
                Quantity = quantity, SignedQuantity = -quantity, UnitCost = product.UnitCost,
                CurrencyCode = product.CurrencyCode, Reference = orderNumber, Note = "POS sale stock issue"
            });
        }

        db.CustomerOrders.Add(order);
        await db.SaveChangesAsync(cancellationToken);
        var invoice = await FulfillOrderCoreAsync(orderId, saleDate, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (invoice.TotalAmount > 0m)
        {
            await RecordPaymentCoreAsync(invoice.Id, invoice.TotalAmount, paymentMethod, paymentReference,
                paymentNotes, DateTimeOffset.UtcNow, saleDate, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return invoice;
    }

    /// <summary>
    /// Issues an invoice for a completed game session without treating player charges as
    /// inventory sales. The session ID is used as the stable order reference so retries
    /// return the existing invoice instead of creating duplicate revenue entries.
    /// Payment is collected separately through RecordPaymentAsync.
    /// </summary>
    public async Task<SalesInvoiceRecord> CreateGameSessionInvoiceAsync(
        Guid sessionId,
        DateOnly issueDate,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
            throw new ArgumentException("Game session ID is required.", nameof(sessionId));

        var existingInvoice = await db.SalesInvoices.AsNoTracking()
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.OrderId == sessionId, cancellationToken);
        if (existingInvoice is not null)
        {
            var existingOrder = await db.CustomerOrders.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == sessionId, cancellationToken);
            if (existingOrder?.OrderNumber.StartsWith("GAME-", StringComparison.Ordinal) == true)
                return existingInvoice;
            throw new InvalidOperationException("The session ID is already associated with a non-game invoice.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        existingInvoice = await db.SalesInvoices.AsNoTracking()
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.OrderId == sessionId, cancellationToken);
        if (existingInvoice is not null)
        {
            var existingOrder = await db.CustomerOrders.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == sessionId, cancellationToken);
            if (existingOrder?.OrderNumber.StartsWith("GAME-", StringComparison.Ordinal) == true)
            {
                await transaction.CommitAsync(cancellationToken);
                return existingInvoice;
            }
            throw new InvalidOperationException("The session ID is already associated with a non-game invoice.");
        }

        var session = await db.GameSessions.AsNoTracking()
            .Include(x => x.Players)
            .SingleOrDefaultAsync(x => x.Id == sessionId, cancellationToken)
            ?? throw new KeyNotFoundException("Game session was not found.");
        if (!string.Equals(session.Status, "Completed", StringComparison.Ordinal) || session.EndedAtUtc is null)
            throw new InvalidOperationException("Only a completed game session can be invoiced.");
        if (session.Players.Count == 0)
            throw new InvalidOperationException("A game session must have at least one player before it can be invoiced.");
        if (session.CurrencyCode.Length != 3
            || session.CurrencyCode.Any(c => c is < 'A' or > 'Z'))
            throw new InvalidOperationException("The game session has an invalid currency code.");

        decimal total = 0m;
        var lines = new List<CustomerOrderLine>(session.Players.Count);
        foreach (var player in session.Players.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id))
        {
            if (!string.Equals(player.CurrencyCode, session.CurrencyCode, StringComparison.Ordinal)
                || player.EntryAmount < 0m || player.TimeCharge < 0m || player.AdditionalAmount < 0m)
                throw new InvalidOperationException($"Player '{player.DisplayName}' has invalid charges or currency.");

            var playerTotal = checked(checked(player.EntryAmount + player.TimeCharge) + player.AdditionalAmount);
            total = checked(total + playerTotal);
            var label = $"بازی {session.GameName} - {player.DisplayName}";
            if (label.Length > 200)
                label = label[..200];

            lines.Add(new CustomerOrderLine
            {
                Id = Guid.NewGuid(),
                OrderId = sessionId,
                ProductId = Guid.Empty,
                ProductName = label,
                UnitPrice = playerTotal,
                UnitCost = 0m,
                Quantity = 1,
                LineTotal = playerTotal
            });
        }

        var orderNumber = $"GAME-{sessionId:N}".ToUpperInvariant();
        var order = new CustomerOrder
        {
            Id = sessionId,
            OrderNumber = orderNumber,
            IdempotencyKey = orderNumber,
            CustomerName = "جلسه بازی",
            CustomerPhone = string.Empty,
            DeliveryAddress = string.Empty,
            Notes = $"صورتحساب جلسه: {session.GameName}",
            CurrencyCode = session.CurrencyCode,
            TotalAmount = total,
            Status = "Fulfilled",
            CreatedAt = session.EndedAtUtc.Value,
            Lines = lines
        };
        db.CustomerOrders.Add(order);
        await db.SaveChangesAsync(cancellationToken);

        var invoice = new SalesInvoiceRecord
        {
            Id = Guid.NewGuid(),
            InvoiceNumber = $"G-{sessionId:N}".ToUpperInvariant(),
            OrderId = sessionId,
            IssueDate = issueDate,
            CustomerName = order.CustomerName,
            CustomerPhone = order.CustomerPhone,
            CurrencyCode = session.CurrencyCode,
            TotalAmount = total,
            CostTotal = 0m,
            PaidAmount = 0m,
            Status = total == 0m ? "Paid" : "Issued",
            CreatedAt = DateTimeOffset.UtcNow,
            Lines = lines.Select(line => new SalesInvoiceLineRecord
            {
                Id = Guid.NewGuid(),
                ProductId = line.ProductId,
                ProductName = line.ProductName,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                UnitCost = 0m,
                LineTotal = line.LineTotal,
                CostTotal = 0m
            }).ToList()
        };
        db.SalesInvoices.Add(invoice);

        if (total > 0m)
        {
            var receivableAccount = await db.LedgerAccounts.SingleOrDefaultAsync(x => x.Code == "1100", cancellationToken)
                ?? throw new InvalidOperationException("Accounts-receivable ledger account 1100 is missing.");
            var revenueAccount = await db.LedgerAccounts.SingleOrDefaultAsync(x => x.Code == "4000", cancellationToken)
                ?? throw new InvalidOperationException("Sales-revenue ledger account 4000 is missing.");
            EnsurePostingAccount(new Dictionary<string, LedgerAccountRecord>(StringComparer.Ordinal)
            {
                [receivableAccount.Code] = receivableAccount,
                [revenueAccount.Code] = revenueAccount
            }, "1100");
            EnsurePostingAccount(new Dictionary<string, LedgerAccountRecord>(StringComparer.Ordinal)
            {
                [revenueAccount.Code] = revenueAccount
            }, "4000");

            db.JournalEntries.Add(CreateJournalEntry(
                $"فاکتور جلسه بازی {session.GameName}",
                session.CurrencyCode,
                "GameSessionInvoice",
                sessionId,
                issueDate,
                (receivableAccount, "Debit", total),
                (revenueAccount, "Credit", total)));
        }

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
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var payment = await RecordPaymentCoreAsync(invoiceId, amount, method, reference, notes,
            occurredAt, entryDate, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return payment;
    }

    private async Task<SalesInvoiceRecord> FulfillOrderCoreAsync(
        Guid orderId,
        DateOnly issueDate,
        CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty) throw new ArgumentException("Order ID is required.", nameof(orderId));

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
        if (invoiceNumber.Length > 40)
            invoiceNumber = $"INV-{order.Id:N}".ToUpperInvariant();

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
        return invoice;
    }

    private async Task<PaymentRecord> RecordPaymentCoreAsync(
        Guid invoiceId,
        decimal amount,
        string method,
        string reference,
        string notes,
        DateTimeOffset occurredAt,
        DateOnly entryDate,
        CancellationToken cancellationToken)
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
