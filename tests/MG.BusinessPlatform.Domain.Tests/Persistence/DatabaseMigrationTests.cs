using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MG.BusinessPlatform.Infrastructure.Persistence;

namespace MG.BusinessPlatform.Domain.Tests.Persistence;

public sealed class DatabaseMigrationTests
{
    [Fact]
    public async Task FreshDatabase_MigratesAndCanPersistStoreSettingsAndCatalog()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);

        await db.Database.MigrateAsync();

        db.StoreSettings.Add(new StoreSettings
        {
            Id = 1,
            StoreName = "Test Store",
            CurrencyCode = "IRR",
            OnlineOrderingEnabled = false,
            PublicPublicationEnabled = false,
            PublicHostname = string.Empty,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.StoreProducts.Add(new StoreProduct
        {
            Id = Guid.NewGuid(),
            Sku = "MIGRATION-1",
            Name = "Migration test",
            UnitPrice = 123.45m,
            CurrencyCode = "IRR",
            IsAvailable = true,
            StockQuantity = 8,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.Customers.Add(new CustomerRecord
        {
            Id = Guid.NewGuid(), DisplayName = "Test Customer", Phone = "0912", Email = string.Empty,
            Address = string.Empty, CreatedAt = DateTimeOffset.UtcNow, IsActive = true
        });
        db.Suppliers.Add(new SupplierRecord
        {
            Id = Guid.NewGuid(), DisplayName = "Test Supplier", Phone = "021", Email = string.Empty,
            Address = string.Empty, TaxIdentifier = string.Empty, PaymentTerms = string.Empty,
            CreatedAt = DateTimeOffset.UtcNow, IsActive = true
        });
        await db.SaveChangesAsync();

        Assert.Equal("Test Store", (await db.StoreSettings.SingleAsync()).StoreName);
        Assert.Equal(123.45m, (await db.StoreProducts.SingleAsync()).UnitPrice);
        Assert.Equal("Test Customer", (await db.Customers.SingleAsync()).DisplayName);
        Assert.Equal("Test Supplier", (await db.Suppliers.SingleAsync()).DisplayName);
        var appliedMigrations = await db.Database.GetAppliedMigrationsAsync();
        Assert.Contains("20261013000000_InitialStorefrontSchema", appliedMigrations);
        Assert.Contains("20261013010000_AddCustomerSupplierTables", appliedMigrations);
    }

    [Fact]
    public async Task DatabaseConstraint_RejectsNegativeDecimalPrice()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await db.Database.MigrateAsync();

        db.StoreProducts.Add(new StoreProduct
        {
            Id = Guid.NewGuid(),
            Sku = "NEGATIVE-PRICE",
            Name = "Invalid price",
            UnitPrice = -1.25m,
            CurrencyCode = "IRR",
            IsAvailable = true,
            StockQuantity = 1,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
