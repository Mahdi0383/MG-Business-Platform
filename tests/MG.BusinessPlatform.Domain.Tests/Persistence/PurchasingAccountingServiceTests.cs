using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MG.BusinessPlatform.Infrastructure.Persistence;

namespace MG.BusinessPlatform.Domain.Tests.Persistence;

public sealed class PurchasingAccountingServiceTests
{
    [Fact]
    public async Task PurchaseInvoice_ReceivesStockPostsInventoryAndPayableAndSupportsPartialPayments()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await db.Database.MigrateAsync();
        var supplierId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        db.LedgerAccounts.AddRange(
            Account("1000", "Cash", "Asset"),
            Account("1010", "Card clearing", "Asset"),
            Account("1020", "Bank", "Asset"),
            Account("1200", "Inventory", "Asset"),
            Account("2000", "Accounts payable", "Liability"));
        db.Suppliers.Add(new SupplierRecord
        {
            Id = supplierId, DisplayName = "Supplier A", Phone = "", Email = "", Address = "",
            TaxIdentifier = "", PaymentTerms = "", CreatedAt = DateTimeOffset.UtcNow, IsActive = true
        });
        db.StoreProducts.Add(new StoreProduct
        {
            Id = productId, Sku = "PURCHASE-1", Name = "Widget", UnitPrice = 30m, UnitCost = 10m,
            CurrencyCode = "IRR", IsAvailable = true, StockQuantity = 5, UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new PurchasingAccountingService(db);
        var invoice = await service.CreatePurchaseInvoiceAsync(supplierId, branchId,
        [
            new PurchaseInvoiceLineRequest(productId, 3, 20m, 0.1m)
        ], new DateOnly(2026, 10, 16), dueDate: new DateOnly(2026, 11, 15));

        Assert.Equal(new DateOnly(2026, 11, 15), invoice.DueDate);
        Assert.Equal(54m, invoice.TotalAmount);
        Assert.Equal("Received", invoice.Status);
        Assert.Equal(0m, invoice.PaidAmount);
        var product = await db.StoreProducts.SingleAsync(x => x.Id == productId);
        Assert.Equal(8, product.StockQuantity);
        Assert.Equal(13m, product.UnitCost);
        Assert.Equal(1, await db.StockMovements.CountAsync(x => x.Reference == invoice.InvoiceNumber && x.SignedQuantity == 3));
        var purchaseJournal = await db.JournalEntries.Include(x => x.Lines)
            .SingleAsync(x => x.SourceType == "PurchaseInvoice" && x.SourceId == invoice.Id);
        Assert.Equal(54m, purchaseJournal.Lines.Where(x => x.Side == "Debit").Sum(x => x.Amount));
        Assert.Equal(54m, purchaseJournal.Lines.Where(x => x.Side == "Credit").Sum(x => x.Amount));

        var firstPayment = await service.RecordSupplierPaymentAsync(invoice.Id, 20m, "Cash", "CASH-1");
        Assert.Equal(20m, firstPayment.Amount);
        var afterPartial = await db.PurchaseInvoices.SingleAsync(x => x.Id == invoice.Id);
        Assert.Equal(20m, afterPartial.PaidAmount);
        Assert.Equal("PartiallyPaid", afterPartial.Status);

        var secondPayment = await service.RecordSupplierPaymentAsync(invoice.Id, 34m, "Card", "CARD-2");
        Assert.Equal("Card", secondPayment.Method);
        var afterFull = await db.PurchaseInvoices.SingleAsync(x => x.Id == invoice.Id);
        Assert.Equal(54m, afterFull.PaidAmount);
        Assert.Equal("Paid", afterFull.Status);
        Assert.Equal(2, await db.SupplierPayments.CountAsync());
        Assert.Equal(3, await db.JournalEntries.CountAsync());

        foreach (var entry in await db.JournalEntries.Include(x => x.Lines).ToListAsync())
        {
            Assert.Equal(entry.Lines.Where(x => x.Side == "Debit").Sum(x => x.Amount),
                entry.Lines.Where(x => x.Side == "Credit").Sum(x => x.Amount));
        }
    }

    [Fact]
    public async Task PurchaseInvoice_RepeatedProductLinesAccumulateStockAndWeightedAverageCost()
    {
        await using var fixture = await PurchasingFixture.CreateAsync();
        var service = new PurchasingAccountingService(fixture.Db);

        var invoice = await service.CreatePurchaseInvoiceAsync(
            fixture.SupplierId,
            fixture.BranchId,
            [
                new PurchaseInvoiceLineRequest(fixture.ProductId, 3, 20m, 0.1m),
                new PurchaseInvoiceLineRequest(fixture.ProductId, 2, 30m)
            ],
            new DateOnly(2026, 10, 16));

        Assert.Equal(114m, invoice.TotalAmount);
        Assert.Equal(2, invoice.Lines.Count);

        var product = await fixture.Db.StoreProducts.SingleAsync(x => x.Id == fixture.ProductId);
        Assert.Equal(10, product.StockQuantity);
        Assert.Equal(16.4m, product.UnitCost);

        var movements = await fixture.Db.StockMovements
            .Where(x => x.Reference == invoice.InvoiceNumber)
            .ToListAsync();
        Assert.Equal(2, movements.Count);
        Assert.Equal(5, movements.Sum(x => x.SignedQuantity));
        Assert.Equal(114m, movements.Sum(x => x.Quantity * x.UnitCost));

        var journal = await fixture.Db.JournalEntries.Include(x => x.Lines)
            .SingleAsync(x => x.SourceType == "PurchaseInvoice" && x.SourceId == invoice.Id);
        Assert.Equal(114m, journal.Lines.Where(x => x.Side == "Debit").Sum(x => x.Amount));
        Assert.Equal(114m, journal.Lines.Where(x => x.Side == "Credit").Sum(x => x.Amount));
    }

    [Fact]
    public async Task SupplierPayment_RejectsOverpaymentWithoutWritingLedgerEntries()
    {
        await using var fixture = await PurchasingFixture.CreateAsync();
        var service = new PurchasingAccountingService(fixture.Db);
        var invoice = await service.CreatePurchaseInvoiceAsync(fixture.SupplierId, fixture.BranchId,
        [
            new PurchaseInvoiceLineRequest(fixture.ProductId, 2, 15m)
        ], new DateOnly(2026, 10, 16));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecordSupplierPaymentAsync(invoice.Id, invoice.TotalAmount + 1m, "Cash"));

        Assert.Empty(await fixture.Db.SupplierPayments.ToListAsync());
        Assert.Single(await fixture.Db.JournalEntries.ToListAsync());
        var stored = await fixture.Db.PurchaseInvoices.SingleAsync(x => x.Id == invoice.Id);
        Assert.Equal(0m, stored.PaidAmount);
        Assert.Equal("Received", stored.Status);
    }

    [Fact]
    public async Task PurchaseInvoice_RejectsInactiveSupplierBeforeChangingInventory()
    {
        await using var fixture = await PurchasingFixture.CreateAsync(activeSupplier: false);
        var service = new PurchasingAccountingService(fixture.Db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreatePurchaseInvoiceAsync(
            fixture.SupplierId, fixture.BranchId,
            [new PurchaseInvoiceLineRequest(fixture.ProductId, 2, 15m)],
            new DateOnly(2026, 10, 16)));

        var product = await fixture.Db.StoreProducts.SingleAsync(x => x.Id == fixture.ProductId);
        Assert.Equal(5, product.StockQuantity);
        Assert.Empty(await fixture.Db.PurchaseInvoices.ToListAsync());
        Assert.Empty(await fixture.Db.StockMovements.ToListAsync());
        Assert.Empty(await fixture.Db.JournalEntries.ToListAsync());
    }

    private static LedgerAccountRecord Account(string code, string name, string type) => new()
    {
        Id = Guid.NewGuid(), Code = code, Name = name, AccountType = type,
        IsActive = true, IsPostingAllowed = true
    };

    private sealed class PurchasingFixture(SqliteConnection connection, BusinessDbContext db,
        Guid supplierId, Guid productId, Guid branchId) : IAsyncDisposable
    {
        public BusinessDbContext Db { get; } = db;
        public Guid SupplierId { get; } = supplierId;
        public Guid ProductId { get; } = productId;
        public Guid BranchId { get; } = branchId;

        public static async Task<PurchasingFixture> CreateAsync(bool activeSupplier = true)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
            var db = new BusinessDbContext(options);
            await db.Database.MigrateAsync();
            var supplierId = Guid.NewGuid();
            var productId = Guid.NewGuid();
            db.LedgerAccounts.AddRange(
                Account("1000", "Cash", "Asset"),
                Account("1010", "Card clearing", "Asset"),
                Account("1020", "Bank", "Asset"),
                Account("1200", "Inventory", "Asset"),
                Account("2000", "Accounts payable", "Liability"));
            db.Suppliers.Add(new SupplierRecord
            {
                Id = supplierId, DisplayName = "Supplier A", Phone = "", Email = "", Address = "",
                TaxIdentifier = "", PaymentTerms = "", CreatedAt = DateTimeOffset.UtcNow, IsActive = activeSupplier
            });
            db.StoreProducts.Add(new StoreProduct
            {
                Id = productId, Sku = "PURCHASE-2", Name = "Widget", UnitPrice = 30m, UnitCost = 10m,
                CurrencyCode = "IRR", IsAvailable = true, StockQuantity = 5, UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            return new PurchasingFixture(connection, db, supplierId, productId, Guid.NewGuid());
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
