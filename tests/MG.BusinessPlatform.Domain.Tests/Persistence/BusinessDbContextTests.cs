using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MG.BusinessPlatform.Infrastructure.Persistence;

namespace MG.BusinessPlatform.Domain.Tests.Persistence;

public sealed class BusinessDbContextTests
{
    [Fact]
    public async Task Database_CreatesExpectedTablesAndEnforcesUniqueSku()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await db.Database.EnsureCreatedAsync();

        Assert.True(await db.Database.CanConnectAsync());
        Assert.True(await db.StoreProducts.AnyAsync() == false);

        db.StoreProducts.Add(new StoreProduct
        {
            Id = Guid.NewGuid(), Sku = "COFFEE-01", Name = "Coffee", UnitPrice = 120m,
            CurrencyCode = "IRR", IsAvailable = true, StockQuantity = 10, UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        db.StoreProducts.Add(new StoreProduct
        {
            Id = Guid.NewGuid(), Sku = "COFFEE-01", Name = "Duplicate", UnitPrice = 100m,
            CurrencyCode = "IRR", IsAvailable = true, StockQuantity = 10, UpdatedAt = DateTimeOffset.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Database_RejectsNegativeStockAtDatabaseLevel()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.StoreProducts.Add(new StoreProduct
        {
            Id = Guid.NewGuid(), Sku = "NEG-STOCK", Name = "Invalid", UnitPrice = 1m,
            CurrencyCode = "IRR", IsAvailable = true, StockQuantity = -1, UpdatedAt = DateTimeOffset.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Database_EnforcesOrderIdempotencyKeyUniqueness()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.CustomerOrders.Add(NewOrder("retry-key-00000001"));
        await db.SaveChangesAsync();
        db.CustomerOrders.Add(NewOrder("retry-key-00000001"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static CustomerOrder NewOrder(string key) => new()
    {
        Id = Guid.NewGuid(),
        OrderNumber = $"WEB-{Guid.NewGuid():N}",
        IdempotencyKey = key,
        CustomerName = "Customer",
        CustomerPhone = "000",
        CurrencyCode = "IRR",
        TotalAmount = 10m,
        Status = "Received",
        CreatedAt = DateTimeOffset.UtcNow
    };
}
