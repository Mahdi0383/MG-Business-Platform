using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
