using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using MG.BusinessPlatform.Infrastructure.Persistence;

namespace MG.BusinessPlatform.Domain.Tests.Persistence;

public sealed class BranchInventoryBalanceTests
{
    [Fact]
    public async Task InitializeAsync_MigratesLegacyProductStockIntoConfiguredDefaultBranchOnce()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        var migrator = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
        await migrator.MigrateAsync("20261117050000_AddStockMovementOwnership");

        db.StoreSettings.Add(new StoreSettings
        {
            Id = 1,
            StoreName = "Legacy Store",
            CurrencyCode = "IRR",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        var product = new StoreProduct
        {
            Id = Guid.NewGuid(),
            Sku = "LEGACY-BALANCE-01",
            Name = "Legacy product",
            UnitPrice = 100m,
            UnitCost = 60m,
            CurrencyCode = "IRR",
            IsAvailable = true,
            StockQuantity = 17,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.StoreProducts.Add(product);
        db.StockMovements.Add(new StockMovementRecord
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            OccurredAt = DateTimeOffset.UtcNow,
            MovementType = "OpeningBalance",
            Quantity = 17,
            SignedQuantity = 17,
            UnitCost = 60m,
            CurrencyCode = "IRR",
            Reference = "LEGACY-IMPORT",
            Note = "Legacy unassigned history"
        });
        await db.SaveChangesAsync();

        var requestedDefaultBranchId = Guid.NewGuid();
        await DatabaseInitializer.InitializeAsync(db, defaultBranchId: requestedDefaultBranchId);

        var balance = await db.BranchInventoryBalances.AsNoTracking().SingleAsync();
        Assert.Equal(product.BusinessId, balance.BusinessId);
        Assert.Equal(requestedDefaultBranchId, balance.BranchId);
        Assert.Equal(product.Id, balance.ProductId);
        Assert.Equal(17, balance.Quantity);

        await DatabaseInitializer.InitializeAsync(db, defaultBranchId: requestedDefaultBranchId);
        Assert.Single(await db.BranchInventoryBalances.AsNoTracking().ToListAsync());
        Assert.Equal(17, (await db.BranchInventoryBalances.AsNoTracking().SingleAsync()).Quantity);
    }

    [Fact]
    public async Task StockMovementTrigger_UpdatesBranchBalancesAtomicallyAndRejectsNegativeBalance()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await DatabaseInitializer.InitializeAsync(db);

        var settings = await db.StoreSettings.SingleAsync(x => x.Id == 1);
        var businessId = settings.BusinessId!.Value;
        var defaultBranchId = settings.DefaultBranchId!.Value;
        var product = new StoreProduct
        {
            Id = Guid.NewGuid(),
            BusinessId = businessId,
            Sku = "BRANCH-BALANCE-01",
            Name = "Branch balance product",
            UnitPrice = 100m,
            UnitCost = 50m,
            CurrencyCode = "IRR",
            IsAvailable = true,
            StockQuantity = 10,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.StoreProducts.Add(product);
        db.StockMovements.Add(NewMovement(product.Id, "OPEN-1", 10));
        await db.SaveChangesAsync();

        var defaultBalance = await db.BranchInventoryBalances.SingleAsync();
        Assert.Equal(10, defaultBalance.Quantity);

        product.StockQuantity = 7;
        product.UpdatedAt = DateTimeOffset.UtcNow;
        db.StockMovements.Add(NewMovement(product.Id, "SALE-1", -3));
        await db.SaveChangesAsync();
        Assert.Equal(7, (await db.BranchInventoryBalances.SingleAsync()).Quantity);

        var secondBranch = new BranchRecord
        {
            Id = Guid.NewGuid(),
            BusinessId = businessId,
            Code = "SECOND",
            Name = "Second branch",
            IsActive = true
        };
        db.Branches.Add(secondBranch);
        await db.SaveChangesAsync();

        product.StockQuantity = 11;
        product.UpdatedAt = DateTimeOffset.UtcNow;
        db.StockMovements.Add(NewMovement(product.Id, "PURCHASE-SECOND", 4, secondBranch.Id));
        await db.SaveChangesAsync();

        var balances = await db.BranchInventoryBalances.AsNoTracking()
            .OrderBy(x => x.BranchId)
            .ToArrayAsync();
        Assert.Equal(2, balances.Length);
        Assert.Equal(7, balances.Single(x => x.BranchId == defaultBranchId).Quantity);
        Assert.Equal(4, balances.Single(x => x.BranchId == secondBranch.Id).Quantity);

        var invalidMovement = NewMovement(product.Id, "OVERDRAW", -5, secondBranch.Id);
        db.StockMovements.Add(invalidMovement);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.Entry(invalidMovement).State = EntityState.Detached;
        Assert.Equal(4, (await db.BranchInventoryBalances.AsNoTracking()
            .SingleAsync(x => x.BranchId == secondBranch.Id)).Quantity);
    }

    private static StockMovementRecord NewMovement(
        Guid productId, string reference, int signedQuantity, Guid? branchId = null) => new()
    {
        Id = Guid.NewGuid(),
        BranchId = branchId,
        ProductId = productId,
        OccurredAt = DateTimeOffset.UtcNow,
        MovementType = signedQuantity >= 0 ? "PositiveAdjustment" : "SaleIssue",
        Quantity = Math.Abs(signedQuantity),
        SignedQuantity = signedQuantity,
        UnitCost = 50m,
        CurrencyCode = "IRR",
        Reference = reference,
        Note = string.Empty
    };
}
