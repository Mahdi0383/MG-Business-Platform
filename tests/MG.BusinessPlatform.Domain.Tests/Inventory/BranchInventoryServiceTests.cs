using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using MG.BusinessPlatform.Infrastructure.Persistence;
using MG.BusinessPlatform.Infrastructure.Security;
using MG.BusinessPlatform.Infrastructure.Services;

namespace MG.BusinessPlatform.Domain.Tests.Inventory;

public sealed class BranchInventoryServiceTests
{
    [Fact]
    public async Task ApplyMovement_AtomicallyCreatesBalanceAndMovement_AndRejectsNegativeStock()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await DatabaseInitializer.InitializeAsync(db);

        var product = NewProduct("BRANCH-INVENTORY-ATOMIC");
        db.StoreProducts.Add(product);
        await db.SaveChangesAsync();

        var service = new BranchInventoryService(db);
        var received = await service.ApplyMovementAsync(
            product.Id, 12, "Opening", "OPEN-001", "Initial stock",
            StaffPermission.InventoryAdjust);
        Assert.Equal(12, received);

        var issued = await service.ApplyMovementAsync(
            product.Id, -5, "Sale", "SALE-001", "Sale issue",
            StaffPermission.SalesCreate);
        Assert.Equal(7, issued);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyMovementAsync(
            product.Id, -8, "Sale", "SALE-002", "Insufficient issue",
            StaffPermission.SalesCreate));

        var settings = await db.StoreSettings.AsNoTracking().SingleAsync(x => x.Id == 1);
        var balance = await db.BranchInventoryBalances.SingleAsync();
        Assert.Equal(settings.BusinessId, balance.BusinessId);
        Assert.Equal(settings.DefaultBranchId, balance.BranchId);
        Assert.Equal(product.Id, balance.ProductId);
        Assert.Equal(7, balance.OnHandQuantity);
        Assert.Equal(new[] { 12, -5 }, await db.StockMovements
            .OrderBy(x => x.OccurredAt)
            .Select(x => x.SignedQuantity)
            .ToArrayAsync());
    }

    [Fact]
    public async Task FirstMovement_SeedsOnlyTheFirstBranchFromNewProductsOpeningStock()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await DatabaseInitializer.InitializeAsync(db);

        var product = NewProduct("BRANCH-INVENTORY-OPENING");
        product.StockQuantity = 8;
        db.StoreProducts.Add(product);
        await db.SaveChangesAsync();

        var resultingQuantity = await new BranchInventoryService(db).ApplyMovementAsync(
            product.Id, -3, "Sale", "SALE-OPENING-001", "Issue from opening stock",
            StaffPermission.SalesCreate);

        Assert.Equal(5, resultingQuantity);
        var balance = await db.BranchInventoryBalances.SingleAsync();
        var settings = await db.StoreSettings.AsNoTracking().SingleAsync(x => x.Id == 1);
        Assert.Equal(settings.DefaultBranchId, balance.BranchId);
        Assert.Equal(5, balance.OnHandQuantity);
        Assert.Equal(-3, (await db.StockMovements.SingleAsync()).SignedQuantity);
    }

    [Fact]
    public async Task ApplyMovement_UsesCallerTransaction_AndRollsBackBalanceAndLedgerTogether()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await DatabaseInitializer.InitializeAsync(db);

        var product = NewProduct("BRANCH-INVENTORY-OUTER-TX");
        db.StoreProducts.Add(product);
        await db.SaveChangesAsync();
        var service = new BranchInventoryService(db);
        await service.ApplyMovementAsync(
            product.Id, 10, "Opening", "OPEN-002", "Initial stock",
            StaffPermission.InventoryAdjust);

        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await service.ApplyMovementAsync(
                product.Id, -4, "Sale", "SALE-003", "Sale issue",
                StaffPermission.SalesCreate);
            await transaction.RollbackAsync();
        }

        Assert.Equal(10, await service.GetOnHandAsync(product.Id));
        Assert.Single(await db.StockMovements.ToListAsync());
    }

    [Fact]
    public async Task GetOnHand_RejectsProductsOutsideConfiguredBusiness()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await DatabaseInitializer.InitializeAsync(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            new BranchInventoryService(db).GetOnHandAsync(Guid.NewGuid()));
    }

    private static StoreProduct NewProduct(string sku) => new()
    {
        Id = Guid.NewGuid(),
        Sku = sku,
        Name = "Branch inventory test product",
        UnitPrice = 100m,
        UnitCost = 40m,
        CurrencyCode = "IRR",
        IsAvailable = true,
        StockQuantity = 0,
        UpdatedAt = DateTimeOffset.UtcNow
    };
}
