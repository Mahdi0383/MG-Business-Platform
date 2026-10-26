using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using MG.BusinessPlatform.Infrastructure.Persistence;

namespace MG.BusinessPlatform.Domain.Tests.Persistence;

public sealed class ProductAddonServiceTests
{
    [Fact]
    public async Task Assignment_ValidatesActiveAddonsAndPreservesHistoricalDefinitions()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mg-addon-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "store.db");
        try
        {
            var options = new DbContextOptionsBuilder<BusinessDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False")
                .Options;
            await using var db = new BusinessDbContext(options);
            await DatabaseInitializer.InitializeAsync(db);
            var product = new StoreProduct
            {
                Id = Guid.NewGuid(),
                Sku = "ADDON-TEST",
                Name = "Coffee",
                UnitPrice = 100m,
                UnitCost = 40m,
                CurrencyCode = "IRR",
                IsAvailable = true,
                StockQuantity = 10,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.StoreProducts.Add(product);
            await db.SaveChangesAsync();

            var service = new ProductAddonService(db);
            var addon = await service.SaveDefinitionAsync(null, "Vanilla syrup", 15m, isActive: true);
            await service.SetProductAddonsAsync(product.Id, [addon.Id]);

            var assigned = await service.GetAssignedActiveAddonsAsync(product.Id);
            Assert.Single(assigned);
            Assert.Equal(addon.Id, assigned[0].Id);
            Assert.Equal(15m, assigned[0].Price);

            await service.SaveDefinitionAsync(addon.Id, "Vanilla syrup", 15m, isActive: false);
            Assert.Empty(await service.GetAssignedActiveAddonsAsync(product.Id));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.SetProductAddonsAsync(product.Id, [addon.Id]));

            Assert.Single(await db.StoreProductAddons.Where(x => x.ProductId == product.Id).ToListAsync());
            Assert.Equal("Vanilla syrup", (await db.ProductAddons.SingleAsync()).Name);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public async Task Assignment_RejectsDuplicateIdsAndMoreThanTwentyAddons()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mg-addon-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "store.db");
        try
        {
            var options = new DbContextOptionsBuilder<BusinessDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False")
                .Options;
            await using var db = new BusinessDbContext(options);
            await DatabaseInitializer.InitializeAsync(db);
            var product = new StoreProduct
            {
                Id = Guid.NewGuid(), Sku = "ADDON-LIMIT", Name = "Tea",
                UnitPrice = 10m, UnitCost = 3m, CurrencyCode = "IRR",
                IsAvailable = true, StockQuantity = 5, UpdatedAt = DateTimeOffset.UtcNow
            };
            db.StoreProducts.Add(product);
            await db.SaveChangesAsync();
            var service = new ProductAddonService(db);
            var addon = await service.SaveDefinitionAsync(null, "Lemon", 2m, true);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.SetProductAddonsAsync(product.Id, [addon.Id, addon.Id]));
            var tooMany = Enumerable.Range(0, 21).Select(_ => Guid.NewGuid()).ToArray();
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                service.SetProductAddonsAsync(product.Id, tooMany));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
