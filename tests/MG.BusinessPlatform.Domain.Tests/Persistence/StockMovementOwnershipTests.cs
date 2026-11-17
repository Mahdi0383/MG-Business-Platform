using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using MG.BusinessPlatform.Infrastructure.Persistence;
using MG.BusinessPlatform.Infrastructure.Security;

namespace MG.BusinessPlatform.Domain.Tests.Persistence;

public sealed class StockMovementOwnershipTests
{
    private const string Password = "Strong-Passphrase-2026!";

    [Fact]
    public async Task NewStockMovements_AreStampedWithConfiguredBusinessAndDefaultBranch()
    {
        await using var store = await TestStore.CreateAsync();
        var movement = NewMovement(store.ProductId);

        store.Db.StockMovements.Add(movement);
        await store.Db.SaveChangesAsync();

        Assert.Equal(store.BusinessId, movement.BusinessId);
        Assert.Equal(store.DefaultBranchId, movement.BranchId);
    }

    [Fact]
    public async Task StaffContext_StampsSelectedAuthorizedBranchAndRejectsBranchSpoofing()
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

        var authentication = new StaffAuthenticationService(store.Db, new StaffPasswordHasher());
        await authentication.CreateInitialOwnerAsync(
            store.BusinessId, [store.DefaultBranchId, secondBranch.Id], "owner", "Store Owner", Password);
        var login = await authentication.AuthenticateAsync(store.BusinessId, "owner", Password);
        Assert.NotNull(login);
        var resolver = new StaffRequestContextResolver(store.Db, authentication);
        var context = await resolver.ResolveAsync(login.Token, secondBranch.Id);
        Assert.NotNull(context);

        using (StaffRequestContextScope.Push(context))
        {
            var movement = NewMovement(store.ProductId);
            store.Db.StockMovements.Add(movement);
            await store.Db.SaveChangesAsync();
            Assert.Equal(secondBranch.Id, movement.BranchId);

            var spoofed = NewMovement(store.ProductId);
            spoofed.BranchId = store.DefaultBranchId;
            store.Db.StockMovements.Add(spoofed);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.Db.SaveChangesAsync());
            store.Db.Entry(spoofed).State = EntityState.Detached;
        }
    }

    [Fact]
    public async Task ExplicitStockMovementBranch_MustBeOwnedByConfiguredBusiness()
    {
        await using var store = await TestStore.CreateAsync();
        var foreignBusiness = new BusinessRecord
        {
            Id = Guid.NewGuid(),
            LegalName = "Foreign",
            DisplayName = "Foreign",
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

        var movement = NewMovement(store.ProductId);
        movement.BranchId = foreignBranch.Id;
        store.Db.StockMovements.Add(movement);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.Db.SaveChangesAsync());
        Assert.Empty(await store.Db.StockMovements.ToListAsync());
    }

    private static StockMovementRecord NewMovement(Guid productId) => new()
    {
        Id = Guid.NewGuid(),
        ProductId = productId,
        OccurredAt = DateTimeOffset.UtcNow,
        MovementType = "PositiveAdjustment",
        Quantity = 2,
        SignedQuantity = 2,
        UnitCost = 10m,
        CurrencyCode = "IRR",
        Reference = "OWNERSHIP-TEST",
        Note = string.Empty
    };

    private sealed class TestStore(SqliteConnection connection, BusinessDbContext db,
        Guid businessId, Guid defaultBranchId, Guid productId) : IAsyncDisposable
    {
        public BusinessDbContext Db { get; } = db;
        public Guid BusinessId { get; } = businessId;
        public Guid DefaultBranchId { get; } = defaultBranchId;
        public Guid ProductId { get; } = productId;

        public static async Task<TestStore> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
            var db = new BusinessDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var businessId = Guid.NewGuid();
            var branchId = Guid.NewGuid();
            var productId = Guid.NewGuid();
            db.Businesses.Add(new BusinessRecord
            {
                Id = businessId,
                LegalName = "Inventory test business",
                DisplayName = "Inventory test business",
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
                StoreName = "Inventory test business",
                CurrencyCode = "IRR",
                UpdatedAt = DateTimeOffset.UtcNow
            });
            db.StoreProducts.Add(new StoreProduct
            {
                Id = productId,
                BusinessId = businessId,
                Sku = "STOCK-OWNERSHIP-01",
                Name = "Inventory product",
                UnitPrice = 20m,
                UnitCost = 10m,
                CurrencyCode = "IRR",
                IsAvailable = true,
                StockQuantity = 10,
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
