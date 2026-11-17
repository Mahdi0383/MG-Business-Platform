using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using MG.BusinessPlatform.Infrastructure.Persistence;

namespace MG.BusinessPlatform.Domain.Tests.Persistence;

public sealed class BranchInventoryServiceTests
{
    [Fact]
    public async Task ConsumeAsync_UpdatesBranchBalanceAggregateAndOwnedMovementAtomically()
    {
        await using var store = await TestStore.CreateAsync();
        var service = new BranchInventoryService(store.Db);

        var remaining = await service.ApplyMovementAsync(
            store.BusinessId, store.BranchId, store.ProductId, -3, 50m, "IRR",
            "PosSaleIssue", "POS-TEST-1", "POS test sale");

        Assert.Equal(7, remaining);
        Assert.Equal(7, (await store.Db.BranchInventoryBalances.SingleAsync()).OnHandQuantity);
        Assert.Equal(7, (await store.Db.StoreProducts.SingleAsync()).StockQuantity);
        var movement = await store.Db.StockMovements.SingleAsync();
        Assert.Equal(store.BusinessId, movement.BusinessId);
        Assert.Equal(store.BranchId, movement.BranchId);
        Assert.Equal(store.ProductId, movement.ProductId);
        Assert.Equal(-3, movement.SignedQuantity);
        Assert.Equal(3, movement.Quantity);
    }

    [Fact]
    public async Task ReceiveAsync_IncrementsBranchAndBusinessAggregate()
    {
        await using var store = await TestStore.CreateAsync(initialQuantity: 4);
        var service = new BranchInventoryService(store.Db);

        var remaining = await service.ApplyMovementAsync(
            store.BusinessId, store.BranchId, store.ProductId, 6, 55m, "IRR",
            "PurchaseReceipt", "PUR-TEST-1", "supplier receipt");

        Assert.Equal(10, remaining);
        Assert.Equal(10, (await store.Db.StoreProducts.SingleAsync()).StockQuantity);
        Assert.Equal(6, (await store.Db.StockMovements.SingleAsync()).SignedQuantity);
    }

    [Fact]
    public async Task ConsumeAsync_CannotSpendStockHeldAtAnotherBranch()
    {
        await using var store = await TestStore.CreateAsync(initialQuantity: 8);
        var otherBranchId = Guid.NewGuid();
        store.Db.Branches.Add(new BranchRecord
        {
            Id = otherBranchId,
            BusinessId = store.BusinessId,
            Code = "SECOND",
            Name = "Second branch",
            IsActive = true
        });
        store.Db.BranchInventoryBalances.Add(new BranchInventoryBalanceRecord
        {
            BusinessId = store.BusinessId,
            BranchId = otherBranchId,
            ProductId = store.ProductId,
            OnHandQuantity = 5,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        var mainBalance = await store.Db.BranchInventoryBalances.SingleAsync();
        mainBalance.OnHandQuantity = 3;
        await store.Db.SaveChangesAsync();

        var service = new BranchInventoryService(store.Db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyMovementAsync(
            store.BusinessId, store.BranchId, store.ProductId, -4, 50m, "IRR",
            "PosSaleIssue", "POS-OVERSELL", "must not oversell"));

        Assert.Equal(3, (await store.Db.BranchInventoryBalances.SingleAsync(
            x => x.BranchId == store.BranchId)).OnHandQuantity);
        Assert.Equal(5, (await store.Db.BranchInventoryBalances.SingleAsync(
            x => x.BranchId == otherBranchId)).OnHandQuantity);
        Assert.Equal(8, (await store.Db.StoreProducts.SingleAsync()).StockQuantity);
        Assert.Empty(await store.Db.StockMovements.ToListAsync());
    }

    [Fact]
    public async Task ApplyMovementAsync_RejectsBusinessBranchMismatch()
    {
        await using var store = await TestStore.CreateAsync();
        var foreignBusinessId = Guid.NewGuid();
        store.Db.Businesses.Add(new BusinessRecord
        {
            Id = foreignBusinessId,
            LegalName = "Foreign business",
            DisplayName = "Foreign business",
            DefaultCurrencyCode = "IRR",
            TimeZoneId = "UTC",
            IsActive = true
        });
        await store.Db.SaveChangesAsync();

        var service = new BranchInventoryService(store.Db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyMovementAsync(
            foreignBusinessId, store.BranchId, store.ProductId, -1, 50m, "IRR",
            "PosSaleIssue", "POS-CROSS-BUSINESS", "must be denied"));

        Assert.Equal(10, (await store.Db.StoreProducts.SingleAsync()).StockQuantity);
        Assert.Empty(await store.Db.StockMovements.ToListAsync());
    }

    [Fact]
    public async Task ApplyMovementAsync_InitializesOnlyZeroStockProductsWithoutExistingBalance()
    {
        await using var store = await TestStore.CreateAsync(initialQuantity: 0, createBalance: false);
        var service = new BranchInventoryService(store.Db);

        var remaining = await service.ApplyMovementAsync(
            store.BusinessId, store.BranchId, store.ProductId, 2, 50m, "IRR",
            "PurchaseReceipt", "PUR-FIRST", "first receipt");

        Assert.Equal(2, remaining);
        Assert.Equal(2, (await store.Db.StoreProducts.SingleAsync()).StockQuantity);
        Assert.Equal(2, (await store.Db.BranchInventoryBalances.SingleAsync()).OnHandQuantity);
    }

    [Fact]
    public async Task GetOnHandAsync_SafelyMapsLegacyStockWhenOnlyOneBranchExists()
    {
        await using var store = await TestStore.CreateAsync(initialQuantity: 5, createBalance: false);
        var service = new BranchInventoryService(store.Db);

        var quantity = await service.GetOnHandAsync(store.BusinessId, store.BranchId, store.ProductId);

        Assert.Equal(5, quantity);
        Assert.Equal(5, (await store.Db.BranchInventoryBalances.SingleAsync()).OnHandQuantity);
    }

    [Fact]
    public async Task ApplyMovementAsync_RejectsPositiveLegacyStockWithoutExplicitMultiBranchMapping()
    {
        await using var store = await TestStore.CreateAsync(initialQuantity: 5, createBalance: false);
        store.Db.Branches.Add(new BranchRecord
        {
            Id = Guid.NewGuid(),
            BusinessId = store.BusinessId,
            Code = "SECOND",
            Name = "Second branch",
            IsActive = true
        });
        await store.Db.SaveChangesAsync();
        var service = new BranchInventoryService(store.Db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyMovementAsync(
            store.BusinessId, store.BranchId, store.ProductId, 1, 50m, "IRR",
            "PurchaseReceipt", "PUR-UNMAPPED", "must be mapped first"));

        Assert.Equal(5, (await store.Db.StoreProducts.SingleAsync()).StockQuantity);
        Assert.Empty(await store.Db.StockMovements.ToListAsync());
        Assert.Empty(await store.Db.BranchInventoryBalances.ToListAsync());
    }

    [Fact]
    public async Task ApplyMovementAsync_UsesExistingCallerTransaction()
    {
        await using var store = await TestStore.CreateAsync();
        var service = new BranchInventoryService(store.Db);

        await using (var transaction = await store.Db.Database.BeginTransactionAsync())
        {
            await service.ApplyMovementAsync(
                store.BusinessId, store.BranchId, store.ProductId, -2, 50m, "IRR",
                "PosSaleIssue", "POS-ROLLBACK", "transaction rollback");
            await transaction.RollbackAsync();
        }

        Assert.Equal(10, (await store.Db.StoreProducts.AsNoTracking().SingleAsync()).StockQuantity);
        Assert.Equal(10, (await store.Db.BranchInventoryBalances.AsNoTracking().SingleAsync()).OnHandQuantity);
        Assert.Empty(await store.Db.StockMovements.AsNoTracking().ToListAsync());
    }

    private sealed class TestStore(
        SqliteConnection connection,
        BusinessDbContext db,
        Guid businessId,
        Guid branchId,
        Guid productId) : IAsyncDisposable
    {
        public SqliteConnection Connection { get; } = connection;
        public BusinessDbContext Db { get; } = db;
        public Guid BusinessId { get; } = businessId;
        public Guid BranchId { get; } = branchId;
        public Guid ProductId { get; } = productId;

        public static async Task<TestStore> CreateAsync(
            int initialQuantity = 10,
            bool createBalance = true)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
            var db = new BusinessDbContext(options);
            await DatabaseInitializer.InitializeAsync(db);

            var settings = await db.StoreSettings.AsNoTracking().SingleAsync();
            var businessId = settings.BusinessId!.Value;
            var branchId = settings.DefaultBranchId!.Value;
            var productId = Guid.NewGuid();
            db.StoreProducts.Add(new StoreProduct
            {
                Id = productId,
                BusinessId = businessId,
                Sku = "INV-SERVICE-TEST",
                Name = "Inventory service test product",
                UnitPrice = 100m,
                UnitCost = 50m,
                CurrencyCode = "IRR",
                IsAvailable = true,
                StockQuantity = initialQuantity,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            if (createBalance)
            {
                db.BranchInventoryBalances.Add(new BranchInventoryBalanceRecord
                {
                    BusinessId = businessId,
                    BranchId = branchId,
                    ProductId = productId,
                    OnHandQuantity = initialQuantity,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                });
            }
            await db.SaveChangesAsync();
            return new TestStore(connection, db, businessId, branchId, productId);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
