using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MG.BusinessPlatform.Domain.GameCafe;
using MG.BusinessPlatform.Infrastructure.Persistence;

namespace MG.BusinessPlatform.Domain.Tests.Persistence;

public sealed class SalesAccountingServiceTests
{
    [Fact]
    public async Task Fulfillment_PostsInvoiceAndBalancedSalesAndCostJournals_ThenPaymentsUpdateReceivable()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await DatabaseInitializer.InitializeAsync(db);
        Assert.Equal(10, await db.LedgerAccounts.CountAsync());
        var orderId = await SeedOrderAsync(db, total: 200m, unitCost: 40m, quantity: 2);
        var service = new SalesAccountingService(db);

        var invoice = await service.FulfillOrderAsync(orderId, new DateOnly(2026, 10, 13));

        Assert.Equal("Issued", invoice.Status);
        Assert.Equal(200m, invoice.TotalAmount);
        Assert.Equal(80m, invoice.CostTotal);
        Assert.Equal("Fulfilled", (await db.CustomerOrders.SingleAsync(x => x.Id == orderId)).Status);
        Assert.Equal(2, await db.JournalEntries.CountAsync());
        await AssertAllJournalEntriesBalancedAsync(db);

        var firstPayment = await service.RecordPaymentAsync(invoice.Id, 50m, "Cash", "", "",
            DateTimeOffset.UtcNow, new DateOnly(2026, 10, 13));
        Assert.Equal(50m, firstPayment.Amount);
        var savedInvoice = await db.SalesInvoices.SingleAsync(x => x.Id == invoice.Id);
        Assert.Equal(50m, savedInvoice.PaidAmount);
        Assert.Equal("PartiallyPaid", savedInvoice.Status);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordPaymentAsync(
            invoice.Id, 151m, "Card", "", "", DateTimeOffset.UtcNow, new DateOnly(2026, 10, 13)));
        Assert.Equal(1, await db.Payments.CountAsync());
        Assert.Equal(50m, (await db.SalesInvoices.SingleAsync(x => x.Id == invoice.Id)).PaidAmount);

        await service.RecordPaymentAsync(invoice.Id, 150m, "Bank", "BANK-REF", "Final payment",
            DateTimeOffset.UtcNow, new DateOnly(2026, 10, 13));
        savedInvoice = await db.SalesInvoices.SingleAsync(x => x.Id == invoice.Id);
        Assert.Equal(200m, savedInvoice.PaidAmount);
        Assert.Equal("Paid", savedInvoice.Status);
        Assert.Equal(2, await db.Payments.CountAsync());
        Assert.Equal(4, await db.JournalEntries.CountAsync());
        await AssertAllJournalEntriesBalancedAsync(db);
    }

    [Fact]
    public async Task SingleLinePosSale_RejectsNegativeCostBeforeMutation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await DatabaseInitializer.InitializeAsync(db);
        var product = new StoreProduct
        {
            Id = Guid.NewGuid(), Sku = "POS-NEGATIVE", Name = "Invalid product",
            UnitPrice = 10m, UnitCost = -1m, CurrencyCode = "IRR",
            IsAvailable = true, StockQuantity = 5, UpdatedAt = DateTimeOffset.UtcNow
        };
        db.StoreProducts.Add(product);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SalesAccountingService(db).CreatePosSaleAsync(
                product.Id, 1, "Walk-in customer", "", "Cash", "", "",
                new DateOnly(2026, 10, 13)));

        Assert.Equal(5, (await db.StoreProducts.SingleAsync()).StockQuantity);
        Assert.Empty(await db.CustomerOrders.ToListAsync());
        Assert.Empty(await db.StockMovements.ToListAsync());
        Assert.Empty(await db.SalesInvoices.ToListAsync());
        Assert.Empty(await db.JournalEntries.ToListAsync());
    }

    [Fact]
    public async Task CompletedGameSession_CreatesPerPlayerInvoiceOnce_ThenAllowsSeparatePayment()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await DatabaseInitializer.InitializeAsync(db);

        var now = DateTimeOffset.UtcNow;
        var sessionId = Guid.NewGuid();
        db.GameSessions.Add(new GameSessionRecord
        {
            Id = sessionId,
            BranchId = Guid.NewGuid(),
            DeviceId = Guid.NewGuid(),
            GameName = "FIFA",
            RatePerHour = 600000m,
            CurrencyCode = "IRR",
            StartedAtUtc = now.AddHours(-1),
            EndedAtUtc = now,
            LastTransitionAtUtc = now,
            Status = "Completed",
            Players =
            [
                new GameSessionPlayerRecord
                {
                    Id = Guid.NewGuid(), GameSessionId = sessionId, CreatedAtUtc = now,
                    DisplayName = "Ali", EntryAmount = 50000m, TimeCharge = 100000m,
                    AdditionalAmount = 10000m, CurrencyCode = "IRR"
                },
                new GameSessionPlayerRecord
                {
                    Id = Guid.NewGuid(), GameSessionId = sessionId, CreatedAtUtc = now.AddTicks(1),
                    DisplayName = "Sara", EntryAmount = 50000m, TimeCharge = 150000m,
                    AdditionalAmount = 0m, CurrencyCode = "IRR"
                }
            ]
        });
        await db.SaveChangesAsync();

        var recentSessions = await new GameSessionRepository(db).FindRecentAsync();
        Assert.Contains(recentSessions, session => session.Id == sessionId && session.Status == GameSessionStatus.Completed);

        var service = new SalesAccountingService(db);
        var issueDate = new DateOnly(2026, 10, 13);
        var invoice = await service.CreateGameSessionInvoiceAsync(sessionId, issueDate);

        Assert.Equal(360000m, invoice.TotalAmount);
        Assert.Equal(0m, invoice.PaidAmount);
        Assert.Equal("Issued", invoice.Status);
        Assert.Equal(2, invoice.Lines.Count);
        Assert.Equal(new[] { 160000m, 200000m }, invoice.Lines.OrderBy(x => x.ProductName).Select(x => x.LineTotal).OrderBy(x => x).ToArray());
        Assert.Equal(0m, invoice.CostTotal);
        Assert.Empty(await db.StockMovements.ToListAsync());
        Assert.Single(await db.CustomerOrders.ToListAsync());
        Assert.Single(await db.JournalEntries.ToListAsync());
        Assert.Empty(await db.Payments.ToListAsync());
        await AssertAllJournalEntriesBalancedAsync(db);

        var retry = await service.CreateGameSessionInvoiceAsync(sessionId, issueDate);
        Assert.Equal(invoice.Id, retry.Id);
        Assert.Single(await db.SalesInvoices.ToListAsync());
        Assert.Single(await db.CustomerOrders.ToListAsync());
        Assert.Single(await db.JournalEntries.ToListAsync());

        await service.RecordPaymentAsync(invoice.Id, invoice.TotalAmount, "Cash", "", "",
            DateTimeOffset.UtcNow, issueDate);
        var paidInvoice = await db.SalesInvoices.SingleAsync(x => x.Id == invoice.Id);
        Assert.Equal("Paid", paidInvoice.Status);
        Assert.Equal(invoice.TotalAmount, paidInvoice.PaidAmount);
        Assert.Single(await db.Payments.ToListAsync());
        await AssertAllJournalEntriesBalancedAsync(db);
    }

    [Fact]
    public async Task ActiveGameSession_CannotBeInvoicedBeforeCompletion()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await DatabaseInitializer.InitializeAsync(db);

        var now = DateTimeOffset.UtcNow;
        var sessionId = Guid.NewGuid();
        db.GameSessions.Add(new GameSessionRecord
        {
            Id = sessionId,
            BranchId = Guid.NewGuid(),
            DeviceId = Guid.NewGuid(),
            GameName = "FIFA",
            RatePerHour = 600000m,
            CurrencyCode = "IRR",
            StartedAtUtc = now,
            LastTransitionAtUtc = now,
            Status = "Active",
            Players =
            [
                new GameSessionPlayerRecord
                {
                    Id = Guid.NewGuid(), GameSessionId = sessionId, CreatedAtUtc = now,
                    DisplayName = "Ali", EntryAmount = 50000m, TimeCharge = 0m,
                    AdditionalAmount = 0m, CurrencyCode = "IRR"
                }
            ]
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SalesAccountingService(db).CreateGameSessionInvoiceAsync(
                sessionId, new DateOnly(2026, 10, 13)));

        Assert.Empty(await db.CustomerOrders.ToListAsync());
        Assert.Empty(await db.SalesInvoices.ToListAsync());
        Assert.Empty(await db.JournalEntries.ToListAsync());
    }

    [Fact]
    public async Task PosSale_DeductsStockIssuesInvoiceAndPostsFullPaymentAtomically()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await DatabaseInitializer.InitializeAsync(db);
        var product = new StoreProduct
        {
            Id = Guid.NewGuid(), Sku = "POS-ITEM-1", Name = "POS item", UnitPrice = 100m,
            UnitCost = 40m, CurrencyCode = "IRR", IsAvailable = true, StockQuantity = 5,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.StoreProducts.Add(product);
        await db.SaveChangesAsync();

        var invoice = await new SalesAccountingService(db).CreatePosSaleAsync(
            product.Id, 2, "Walk-in customer", "", "Cash", "", "",
            new DateOnly(2026, 10, 13));

        Assert.Equal(200m, invoice.TotalAmount);
        Assert.Equal(80m, invoice.CostTotal);
        Assert.Equal(200m, invoice.PaidAmount);
        Assert.Equal("Paid", invoice.Status);
        Assert.Equal(3, await db.JournalEntries.CountAsync());
        Assert.Equal(1, await db.Payments.CountAsync());
        Assert.Equal("Fulfilled", (await db.CustomerOrders.SingleAsync()).Status);
        Assert.Equal(3, (await db.StoreProducts.SingleAsync()).StockQuantity);
        var movement = await db.StockMovements.SingleAsync();
        Assert.Equal(-2, movement.SignedQuantity);
        Assert.Equal("PosSaleIssue", movement.MovementType);
        await AssertAllJournalEntriesBalancedAsync(db);
    }

    [Fact]
    public async Task MultiLinePosSale_DeductsEachStockAndPostsOneBalancedInvoiceAtomically()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await DatabaseInitializer.InitializeAsync(db);
        var first = new StoreProduct
        {
            Id = Guid.NewGuid(), Sku = "POS-MULTI-1", Name = "First item", UnitPrice = 100m,
            UnitCost = 40m, CurrencyCode = "IRR", IsAvailable = true, StockQuantity = 5,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var second = new StoreProduct
        {
            Id = Guid.NewGuid(), Sku = "POS-MULTI-2", Name = "Second item", UnitPrice = 50m,
            UnitCost = 20m, CurrencyCode = "IRR", IsAvailable = true, StockQuantity = 7,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.StoreProducts.AddRange(first, second);
        await db.SaveChangesAsync();

        var invoice = await new SalesAccountingService(db).CreatePosSaleAsync(
            new[] { new PosSaleLineRequest(first.Id, 2), new PosSaleLineRequest(second.Id, 3) },
            "Walk-in customer", "", "Cash", "", "", new DateOnly(2026, 10, 13));

        Assert.Equal(350m, invoice.TotalAmount);
        Assert.Equal(140m, invoice.CostTotal);
        Assert.Equal(350m, invoice.PaidAmount);
        Assert.Equal("Paid", invoice.Status);
        Assert.Equal(2, invoice.Lines.Count);
        Assert.Equal(3, await db.JournalEntries.CountAsync());
        Assert.Equal(2, await db.StockMovements.CountAsync());
        Assert.Equal(3, (await db.StoreProducts.SingleAsync(x => x.Id == first.Id)).StockQuantity);
        Assert.Equal(4, (await db.StoreProducts.SingleAsync(x => x.Id == second.Id)).StockQuantity);
        await AssertAllJournalEntriesBalancedAsync(db);
    }

    [Fact]
    public async Task MultiLinePosSale_RejectsStaleCartPriceBeforeChangingStock()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await DatabaseInitializer.InitializeAsync(db);
        var product = new StoreProduct
        {
            Id = Guid.NewGuid(), Sku = "POS-STALE-PRICE", Name = "Changed item", UnitPrice = 120m,
            UnitCost = 40m, CurrencyCode = "IRR", IsAvailable = true, StockQuantity = 5,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.StoreProducts.Add(product);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => new SalesAccountingService(db).CreatePosSaleAsync(
            new[] { new PosSaleLineRequest(product.Id, 1, 100m) },
            "Walk-in customer", "", "Cash", "", "", new DateOnly(2026, 10, 13)));

        Assert.Equal(5, (await db.StoreProducts.SingleAsync(x => x.Id == product.Id)).StockQuantity);
        Assert.Empty(await db.CustomerOrders.ToListAsync());
        Assert.Empty(await db.StockMovements.ToListAsync());
        Assert.Empty(await db.SalesInvoices.ToListAsync());
        Assert.Empty(await db.JournalEntries.ToListAsync());
    }

    [Fact]
    public async Task MultiLinePosSale_InsufficientStockRollsBackEveryLine()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await DatabaseInitializer.InitializeAsync(db);
        var first = new StoreProduct
        {
            Id = Guid.NewGuid(), Sku = "POS-ROLLBACK-1", Name = "Available item", UnitPrice = 100m,
            UnitCost = 40m, CurrencyCode = "IRR", IsAvailable = true, StockQuantity = 5,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var second = new StoreProduct
        {
            Id = Guid.NewGuid(), Sku = "POS-ROLLBACK-2", Name = "Low stock item", UnitPrice = 50m,
            UnitCost = 20m, CurrencyCode = "IRR", IsAvailable = true, StockQuantity = 1,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.StoreProducts.AddRange(first, second);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => new SalesAccountingService(db).CreatePosSaleAsync(
            new[] { new PosSaleLineRequest(first.Id, 2), new PosSaleLineRequest(second.Id, 2) },
            "Walk-in customer", "", "Cash", "", "", new DateOnly(2026, 10, 13)));

        Assert.Equal(5, (await db.StoreProducts.SingleAsync(x => x.Id == first.Id)).StockQuantity);
        Assert.Equal(1, (await db.StoreProducts.SingleAsync(x => x.Id == second.Id)).StockQuantity);
        Assert.Empty(await db.CustomerOrders.ToListAsync());
        Assert.Empty(await db.StockMovements.ToListAsync());
        Assert.Empty(await db.SalesInvoices.ToListAsync());
        Assert.Empty(await db.JournalEntries.ToListAsync());
    }

    [Fact]
    public async Task PosSale_InvalidPaymentMethodRollsBackOrderAndStock()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await DatabaseInitializer.InitializeAsync(db);
        var product = new StoreProduct
        {
            Id = Guid.NewGuid(), Sku = "POS-ITEM-2", Name = "POS item", UnitPrice = 100m,
            UnitCost = 40m, CurrencyCode = "IRR", IsAvailable = true, StockQuantity = 5,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.StoreProducts.Add(product);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => new SalesAccountingService(db).CreatePosSaleAsync(
            product.Id, 2, "Walk-in customer", "", "Unsupported", "", "",
            new DateOnly(2026, 10, 13)));

        await db.Entry(product).ReloadAsync();
        Assert.Equal(5, product.StockQuantity);
        Assert.Empty(await db.CustomerOrders.ToListAsync());
        Assert.Empty(await db.StockMovements.ToListAsync());
        Assert.Empty(await db.SalesInvoices.ToListAsync());
        Assert.Empty(await db.JournalEntries.ToListAsync());
    }

    [Fact]
    public async Task Fulfillment_RejectsDuplicateInvoiceWithoutDuplicateJournals()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await DatabaseInitializer.InitializeAsync(db);
        var orderId = await SeedOrderAsync(db, total: 100m, unitCost: 0m, quantity: 1);
        var service = new SalesAccountingService(db);

        await service.FulfillOrderAsync(orderId, new DateOnly(2026, 10, 13));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.FulfillOrderAsync(orderId, new DateOnly(2026, 10, 13)));

        Assert.Single(await db.SalesInvoices.ToListAsync());
        Assert.Single(await db.JournalEntries.ToListAsync());
    }

    [Fact]
    public async Task Fulfillment_RejectsInconsistentOrderTotalWithoutPartialWrites()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await DatabaseInitializer.InitializeAsync(db);
        var orderId = await SeedOrderAsync(db, total: 999m, unitCost: 0m, quantity: 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SalesAccountingService(db).FulfillOrderAsync(orderId, new DateOnly(2026, 10, 13)));

        Assert.Empty(await db.SalesInvoices.ToListAsync());
        Assert.Empty(await db.JournalEntries.ToListAsync());
        Assert.Equal("Received", (await db.CustomerOrders.SingleAsync(x => x.Id == orderId)).Status);
    }

    private static async Task<Guid> SeedOrderAsync(BusinessDbContext db, decimal total, decimal unitCost, int quantity)
    {
        var productId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var lineTotal = checked(100m * quantity);
        db.StoreProducts.Add(new StoreProduct
        {
            Id = productId, Sku = $"SKU-{Guid.NewGuid():N}"[..12], Name = "Test product",
            UnitPrice = 100m, UnitCost = unitCost, CurrencyCode = "IRR", IsAvailable = true,
            StockQuantity = 0, UpdatedAt = DateTimeOffset.UtcNow
        });
        var order = new CustomerOrder
        {
            Id = orderId, OrderNumber = $"WEB-{Guid.NewGuid():N}".ToUpperInvariant(),
            IdempotencyKey = Guid.NewGuid().ToString("N"), CustomerName = "Test Customer",
            CustomerPhone = "09120000000", DeliveryAddress = string.Empty, Notes = string.Empty,
            Status = "Received", CurrencyCode = "IRR", TotalAmount = total, CreatedAt = DateTimeOffset.UtcNow,
            Lines =
            [
                new CustomerOrderLine
                {
                    Id = Guid.NewGuid(), OrderId = orderId, ProductId = productId, ProductName = "Test product",
                    UnitPrice = 100m, UnitCost = unitCost, Quantity = quantity, LineTotal = lineTotal
                }
            ]
        };
        db.CustomerOrders.Add(order);
        await db.SaveChangesAsync();
        return orderId;
    }

    private static async Task AssertAllJournalEntriesBalancedAsync(BusinessDbContext db)
    {
        var entries = await db.JournalEntries.Include(x => x.Lines).ToListAsync();
        foreach (var entry in entries)
        {
            var debit = entry.Lines.Where(x => x.Side == "Debit").Sum(x => x.Amount);
            var credit = entry.Lines.Where(x => x.Side == "Credit").Sum(x => x.Amount);
            Assert.True(debit > 0m);
            Assert.Equal(debit, credit);
        }
    }
}
