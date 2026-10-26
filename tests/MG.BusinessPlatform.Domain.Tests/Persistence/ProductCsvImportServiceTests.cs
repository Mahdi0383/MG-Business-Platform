using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MG.BusinessPlatform.Infrastructure.Persistence;
using MG.BusinessPlatform.Infrastructure.Services;

namespace MG.BusinessPlatform.Domain.Tests.Persistence;

public sealed class ProductCsvImportServiceTests
{
    [Fact]
    public async Task ImportAsync_ParsesQuotedFieldsAndRecordsOpeningStock()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await db.Database.EnsureCreatedAsync();

        const string csv = """
            SKU,Name,Description,UnitPrice,UnitCost,CurrencyCode,StockQuantity,IsAvailable
            COF-1,"Latte, large","Fresh ""milk"" latte",125.50,75.25,IRR,12,true
            """;

        var result = await new ProductCsvImportService(db).ImportAsync(csv);

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(0, result.SkippedExistingCount);
        Assert.Empty(result.Issues);
        var product = await db.StoreProducts.SingleAsync();
        Assert.Equal("COF-1", product.Sku);
        Assert.Equal("Latte, large", product.Name);
        Assert.Equal("Fresh \"milk\" latte", product.Description);
        Assert.Equal(125.50m, product.UnitPrice);
        Assert.Equal(75.25m, product.UnitCost);
        Assert.Equal(12, product.StockQuantity);
        var movement = await db.StockMovements.SingleAsync();
        Assert.Equal("OpeningBalance", movement.MovementType);
        Assert.Equal(12, movement.SignedQuantity);
        Assert.Equal(product.Id, movement.ProductId);
    }

    [Fact]
    public async Task ImportAsync_WithAnyInvalidRowImportsNothing()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await db.Database.EnsureCreatedAsync();

        const string csv = """
            SKU,Name,UnitPrice
            VALID-1,Valid product,10
            INVALID-1,Invalid product,-1
            """;

        var result = await new ProductCsvImportService(db).ImportAsync(csv);

        Assert.Equal(0, result.ImportedCount);
        Assert.Single(result.Issues);
        Assert.Contains("non-negative", result.Issues[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.StoreProducts.ToListAsync());
        Assert.Empty(await db.StockMovements.ToListAsync());
    }

    [Fact]
    public async Task ImportAsync_SkipsExistingSkuAndImportsNewProducts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.StoreProducts.Add(new StoreProduct
        {
            Id = Guid.NewGuid(), Sku = "EXISTING-1", Name = "Original", UnitPrice = 10m,
            UnitCost = 4m, CurrencyCode = "IRR", StockQuantity = 2, IsAvailable = true,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        const string csv = """
            SKU,Name,UnitPrice,UnitCost,StockQuantity
            existing-1,Do not overwrite,99,50,100
            NEW-1,New product,20,8,3
            """;

        var result = await new ProductCsvImportService(db).ImportAsync(csv);

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(1, result.SkippedExistingCount);
        Assert.Empty(result.Issues);
        Assert.Equal("Original", (await db.StoreProducts.SingleAsync(x => x.Sku == "EXISTING-1")).Name);
        Assert.Equal(2, await db.StoreProducts.CountAsync());
        Assert.Single(await db.StockMovements.Where(x => x.ProductId != Guid.Empty).ToListAsync());
    }

    [Fact]
    public async Task ImportAsync_AcceptsPersianHeadersAndDefaultsBlankOptionalValues()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await db.Database.EnsureCreatedAsync();

        const string csv = """
            کد کالا,نام محصول,قیمت فروش,بهای واحد,واحد پول,موجودی,فعال
            TEA-1,چای,5,,,,
            """;

        var result = await new ProductCsvImportService(db).ImportAsync(csv);

        Assert.Equal(1, result.ImportedCount);
        var product = await db.StoreProducts.SingleAsync();
        Assert.Equal("IRR", product.CurrencyCode);
        Assert.Equal(0m, product.UnitCost);
        Assert.Equal(0, product.StockQuantity);
        Assert.True(product.IsAvailable);
        Assert.Empty(await db.StockMovements.ToListAsync());
    }

    [Fact]
    public async Task ImportAsync_RejectsUnterminatedQuotedField()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await db.Database.EnsureCreatedAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ProductCsvImportService(db).ImportAsync("SKU,Name,UnitPrice\nA,\"broken,10"));
        Assert.Empty(await db.StoreProducts.ToListAsync());
    }
}
