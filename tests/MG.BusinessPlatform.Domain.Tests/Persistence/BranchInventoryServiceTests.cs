using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using MG.BusinessPlatform.Infrastructure.Persistence;

namespace MG.BusinessPlatform.Domain.Tests.Persistence;

public sealed class BranchInventoryServiceTests
{
    [Fact]
    public async Task IssueAndReceive_KeepBranchBalanceAndProductAggregateSynchronized()
    {
        await using var store = await TestStore.CreateAsync();
        var inventory = new BranchInventoryService(store.Db);
        var product = await store.Db.StoreProducts.SingleAsync(x => x.Id == store.ProductId);

        await inventory.CreateOpeningBalanceAsync(
            store.BusinessId, store.MainBranchId, product, 10, 20m, DateTimeOffset.UtcNow);
        await store.Db.SaveChangesAsync();

        var issuedCost = await inventory.IssueAsync(
            store.BusinessId, store.MainBranchId, product, 3, DateTimeOffset.UtcNow);
        await store.Db.SaveChangesAsync();

        Assert.Equal(20m, issuedCost);
        Assert.Equal(7, product.StockQuantity);
        Assert.Equal(20m, product.UnitCost);

        await inventory.ReceiveAsync(
            store.BusinessId, store.MainBranchId, product, 5, 30m, DateTimeOffset.UtcNow);
        await store.Db.SaveChangesAsync();

        var balance = await inventory.GetBalanceAsync(store.BusinessId, store.MainBranchId, product);
        Assert.Equal(12, balance.Quantity);
        Assert.Equal(25m, balance.AverageUnitCost);
        Assert.Equal(12, product.StockQuantity);
        Assert.Equal(25m, product.UnitCost);
    }

    [Fact]
    public async Task MultipleBranches_MaintainIndependentQuantitiesAndWeightedAggregateCost()
    {
        await using var store = await TestStore.CreateAsync();
        var secondBranch = new BranchRecord
        {
            Id = Guid.NewGuid(),
            BusinessId = store.BusinessId,
            Code = "SECOND",
            Name = "Second branch",
            IsActive = true
        };
        store.Db.Branches.Add(secondBranch);
        await store.Db.SaveChangesAsync();

        var inventory = new BranchInventoryService(store.Db);
        var product = await store.Db.StoreProducts.SingleAsync(x => x.Id == store.ProductId);
        await inventory.CreateOpeningBalanceAsync(
            store.BusinessId, store.MainBranchId, product, 8, 10m, DateTimeOffset.UtcNow);
        await inventory.CreateOpeningBalanceAsync(
            store.BusinessId, secondBranch.Id, product, 3, 20m, DateTimeOffset.UtcNow);
        await store.Db.SaveChangesAsync();

        Assert.Equal(11, product.StockQuantity);
        Assert.Equal(13m, product.UnitCost);

        var issuedCost = await inventory.IssueAsync(
            store.BusinessId, store.MainBranchId, product, 6, DateTimeOffset.UtcNow);
        await store.Db.SaveChangesAsync();

        Assert.Equal(10m, issuedCost);
        var main = await inventory.GetBalanceAsync(store.BusinessId, store.MainBranchId, product);
        var second = await inventory.GetBalanceAsync(store.BusinessId, secondBranch.Id, product);
        Assert.Equal(2, main.Quantity);
        Assert.Equal(3, second.Quantity);
        Assert.Equal(5, product.StockQuantity);
        Assert.Equal(16m, product.UnitCost);
    }

    [Fact]
    public async Task IssueAsync_RejectsInsufficientBranchStockWithoutChangingOtherBranches()
    {
        await using var store = await TestStore.CreateAsync();
        var secondBranch = new BranchRecord
        {
            Id = Guid.NewGuid(),
            BusinessId = store.BusinessId,
            Code = "SECOND",
            Name = "Second branch",
            IsActive = true
        };
        store.Db.Branches.Add(secondBranch);
        await store.Db.SaveChangesAsync();

        var inventory = new BranchInventoryService(store.Db);
        var product = await store.Db.StoreProducts.SingleAsync(x => x.Id == store.ProductId);
        await inventory.CreateOpeningBalanceAsync(
            store.BusinessId, secondBranch.Id, product, 4, 15m, DateTimeOffset.UtcNow);
        await store.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.IssueAsync(
            store.BusinessId, store.MainBranchId, product, 1, DateTimeOffset.UtcNow));

        var otherBranch = await inventory.GetBalanceAsync(store.BusinessId, secondBranch.Id, product);
        Assert.Equal(4, otherBranch.Quantity);
        Assert.Equal(4, product.StockQuantity);
    }

    [Fact]
    public async Task ReceiveAsync_RejectsForeignBranchAndProductOwnership()
    {
        await using var store = await TestStore.CreateAsync();
        var foreignBusiness = new BusinessRecord
        {
            Id = Guid.NewGuid(),
            LegalName = "Foreign business",
            DisplayName = "Foreign business",
            DefaultCurrencyCode = "IRR",
            TimeZoneId = "UTC",
            IsActive = true
        };
        var foreignBranch = new BranchRecord
        {
            Id = Guid.NewGuid(),
            BusinessId = foreignBusiness.Id,
            Code = "FOREIGN",
            Name = "Foreign branch",
            IsActive = true
        };
        store.Db.Businesses.Add(foreignBusiness);
        store.Db.Branches.Add(foreignBranch);
        await store.Db.SaveChangesAsync();

        var inventory = new BranchInventoryService(store.Db);
        var product = await store.Db.StoreProducts.SingleAsync(x => x.Id == store.ProductId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.ReceiveAsync(
            store.BusinessId, foreignBranch.Id, product, 1, 10m, DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.ReceiveAsync(
            foreignBusiness.Id, store.MainBranchId, product, 1, 10m, DateTimeOffset.UtcNow));
        Assert.Empty(await store.Db.BranchInventoryBalances.ToListAsync());
    }

    private sealed class TestStore(SqliteConnection connection, BusinessDbContext db,
        Guid businessId, Guid mainBranchId, Guid productId) : IAsyncDisposable
    {
        public BusinessDbContext Db { get; } = db;
        public Guid BusinessId { get; } = businessId;
        public Guid MainBranchId { get; } = mainBranchId;
        public Guid ProductId { get; } = productId;

        public static async Task<TestStore> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
            var db = new BusinessDbContext(options);
            await db.Database.MigrateAsync();

            var businessId = Guid.NewGuid();
            var branchId = Guid.NewGuid();
            var productId = Guid.NewGuid();
            db.Businesses.Add(new BusinessRecord
            {
                Id = businessId,
                LegalName = "Inventory service test",
                DisplayName = "Inventory service test",
                DefaultCurrencyCode = "IRR",
                TimeZoneId = "UTC",
                IsActive = true
            });
            db.Branches.Add(new BranchRecord
            {
                Id = branchId,
                BusinessId = businessId,
                Code = "MAIN",
                Name = "Main branch",
                IsActive = true
            });
            db.StoreSettings.Add(new StoreSettings
            {
                Id = 1,
                BusinessId = businessId,
                DefaultBranchId = branchId,
                StoreName = "Inventory service test",
                CurrencyCode = "IRR",
                UpdatedAt = DateTimeOffset.UtcNow
            });
            db.StoreProducts.Add(new StoreProduct
            {
                Id = productId,
                BusinessId = businessId,
                Sku = "BRANCH-INVENTORY-01",
                Name = "Branch inventory product",
                UnitPrice = 100m,
                UnitCost = 10m,
                CurrencyCode = "IRR",
                IsAvailable = true,
                StockQuantity = 0,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            return new TestStore(connection, db, businessId, branchId, productId);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
