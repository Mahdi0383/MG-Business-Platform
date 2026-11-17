using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using MG.BusinessPlatform.Infrastructure.Persistence;

namespace MG.BusinessPlatform.Domain.Tests.Persistence;

public sealed class BranchInventoryServiceTests
{
    [Fact]
    public async Task ApplyMovement_UpdatesBranchBalanceAggregateAndOwnedMovementAtomically()
    {
        await using var fixture = await InventoryFixture.CreateAsync(stockQuantity: 5);

        await using (var transaction = await fixture.Db.Database.BeginTransactionAsync())
        {
            await BranchInventoryService.ApplyMovementWithinTransactionAsync(
                fixture.Db, fixture.BusinessId, fixture.BranchId, fixture.ProductId,
                -3, "PosSaleIssue", "POS-001", "Test stock issue", 12m, "IRR");
            await fixture.Db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        Assert.Equal(2, await BranchInventoryService.GetOnHandQuantityAsync(
            fixture.Db, fixture.BusinessId, fixture.BranchId, fixture.ProductId));
        Assert.Equal(2, (await fixture.Db.StoreProducts.SingleAsync(x => x.Id == fixture.ProductId)).StockQuantity);
        var movement = await fixture.Db.StockMovements.SingleAsync();
        Assert.Equal(fixture.BusinessId, movement.BusinessId);
        Assert.Equal(fixture.BranchId, movement.BranchId);
        Assert.Equal(-3, movement.SignedQuantity);
        Assert.Equal(12m, movement.UnitCost);
    }

    [Fact]
    public async Task ApplyMovement_RejectsBranchUnderflowWithoutChangingAnyStockData()
    {
        await using var fixture = await InventoryFixture.CreateAsync(stockQuantity: 5);

        await using var transaction = await fixture.Db.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BranchInventoryService.ApplyMovementWithinTransactionAsync(
                fixture.Db, fixture.BusinessId, fixture.BranchId, fixture.ProductId,
                -6, "PosSaleIssue", "POS-UNDERFLOW", "Must roll back", 10m, "IRR"));
        await transaction.RollbackAsync();

        Assert.Equal(5, await BranchInventoryService.GetOnHandQuantityAsync(
            fixture.Db, fixture.BusinessId, fixture.BranchId, fixture.ProductId));
        Assert.Equal(5, (await fixture.Db.StoreProducts.SingleAsync(x => x.Id == fixture.ProductId)).StockQuantity);
        Assert.Empty(await fixture.Db.StockMovements.ToListAsync());
    }

    [Fact]
    public async Task ApplyMovement_RejectsCrossBusinessBranchBeforeWritingMovement()
    {
        await using var fixture = await InventoryFixture.CreateAsync(stockQuantity: 5);
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
        fixture.Db.Businesses.Add(foreignBusiness);
        fixture.Db.Branches.Add(foreignBranch);
        await fixture.Db.SaveChangesAsync();

        await using var transaction = await fixture.Db.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BranchInventoryService.ApplyMovementWithinTransactionAsync(
                fixture.Db, fixture.BusinessId, foreignBranch.Id, fixture.ProductId,
                1, "PurchaseReceipt", "PUR-FOREIGN", "Wrong branch", 10m, "IRR"));
        await transaction.RollbackAsync();

        Assert.Equal(5, await BranchInventoryService.GetOnHandQuantityAsync(
            fixture.Db, fixture.BusinessId, fixture.BranchId, fixture.ProductId));
        Assert.Empty(await fixture.Db.StockMovements.ToListAsync());
    }

    [Fact]
    public async Task ApplyMovement_RequiresCallerTransaction()
    {
        await using var fixture = await InventoryFixture.CreateAsync(stockQuantity: 5);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BranchInventoryService.ApplyMovementWithinTransactionAsync(
                fixture.Db, fixture.BusinessId, fixture.BranchId, fixture.ProductId,
                1, "PurchaseReceipt", "PUR-NO-TRANSACTION", "Must be atomic", 10m, "IRR"));

        Assert.Empty(await fixture.Db.StockMovements.ToListAsync());
    }

    [Fact]
    public async Task ApplyMovement_RejectsAggregateOverflowAndRollsBackBranchBalance()
    {
        await using var fixture = await InventoryFixture.CreateAsync(stockQuantity: int.MaxValue);

        await using var transaction = await fixture.Db.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BranchInventoryService.ApplyMovementWithinTransactionAsync(
                fixture.Db, fixture.BusinessId, fixture.BranchId, fixture.ProductId,
                1, "PurchaseReceipt", "PUR-OVERFLOW", "Must not overflow", 10m, "IRR"));
        await transaction.RollbackAsync();

        Assert.Equal(int.MaxValue, await BranchInventoryService.GetOnHandQuantityAsync(
            fixture.Db, fixture.BusinessId, fixture.BranchId, fixture.ProductId));
        Assert.Equal(int.MaxValue, (await fixture.Db.StoreProducts.SingleAsync(x => x.Id == fixture.ProductId)).StockQuantity);
        Assert.Empty(await fixture.Db.StockMovements.ToListAsync());
    }

    private sealed class InventoryFixture(
        SqliteConnection connection,
        BusinessDbContext db,
        Guid businessId,
        Guid branchId,
        Guid productId) : IAsyncDisposable
    {
        public BusinessDbContext Db { get; } = db;
        public Guid BusinessId { get; } = businessId;
        public Guid BranchId { get; } = branchId;
        public Guid ProductId { get; } = productId;

        public static async Task<InventoryFixture> CreateAsync(int stockQuantity)
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
                Sku = "BRANCH-INVENTORY-TEST",
                Name = "Inventory test product",
                UnitPrice = 100m,
                UnitCost = 10m,
                CurrencyCode = "IRR",
                IsAvailable = true,
                StockQuantity = stockQuantity,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            db.BranchInventoryBalances.Add(new BranchInventoryBalanceRecord
            {
                BusinessId = businessId,
                BranchId = branchId,
                ProductId = productId,
                OnHandQuantity = stockQuantity,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            return new InventoryFixture(connection, db, businessId, branchId, productId);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
