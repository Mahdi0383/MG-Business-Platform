using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MG.BusinessPlatform.Infrastructure.Persistence;
using MG.BusinessPlatform.Infrastructure.Services;

namespace MG.BusinessPlatform.Domain.Tests.Persistence;

public sealed class LegacySqliteProductImportServiceTests
{
    [Fact]
    public async Task ImportAsync_MapsCommonLegacyColumnsWithoutModifyingSource()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), $"mg-legacy-{Guid.NewGuid():N}.db");
        try
        {
            await CreateLegacyDatabaseAsync(sourcePath, """
                CREATE TABLE Products (
                    ProductCode TEXT,
                    ProductName TEXT NOT NULL,
                    SalePrice NUMERIC NOT NULL,
                    CostPrice NUMERIC,
                    Stock INTEGER,
                    CurrencyCode TEXT,
                    IsActive INTEGER,
                    Description TEXT
                );
                INSERT INTO Products VALUES
                    ('OLD-001', 'Coffee, large', 150.5, 70.25, 8, 'IRR', 1, 'Imported "as is"');
                """);

            await using var targetConnection = new SqliteConnection("Data Source=:memory:");
            await targetConnection.OpenAsync();
            var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(targetConnection).Options;
            await using var targetDb = new BusinessDbContext(options);
            await targetDb.Database.EnsureCreatedAsync();

            var result = await new LegacySqliteProductImportService(targetDb).ImportAsync(sourcePath);

            Assert.Equal("Products", result.SourceTable);
            Assert.Equal(1, result.ImportResult.ImportedCount);
            Assert.Empty(result.ImportResult.Issues);
            var product = await targetDb.StoreProducts.SingleAsync();
            Assert.Equal("OLD-001", product.Sku);
            Assert.Equal("Coffee, large", product.Name);
            Assert.Equal("Imported \"as is\"", product.Description);
            Assert.Equal(150.5m, product.UnitPrice);
            Assert.Equal(70.25m, product.UnitCost);
            Assert.Equal(8, product.StockQuantity);
            Assert.Equal(1, await targetDb.StockMovements.CountAsync());

            await using var verifySource = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = sourcePath,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString());
            await verifySource.OpenAsync();
            await using var verifyCommand = verifySource.CreateCommand();
            verifyCommand.CommandText = "SELECT COUNT(*) FROM Products;";
            Assert.Equal(1L, (long)(await verifyCommand.ExecuteScalarAsync())!);
        }
        finally
        {
            if (File.Exists(sourcePath))
                File.Delete(sourcePath);
        }
    }

    [Fact]
    public async Task ImportAsync_GeneratesStableFallbackSkuWhenLegacyTableHasNoCodeColumn()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), $"mg-legacy-{Guid.NewGuid():N}.db");
        try
        {
            await CreateLegacyDatabaseAsync(sourcePath, """
                CREATE TABLE Items (Name TEXT NOT NULL, Price NUMERIC NOT NULL);
                INSERT INTO Items VALUES ('Tea', 5);
                """);

            await using var targetConnection = new SqliteConnection("Data Source=:memory:");
            await targetConnection.OpenAsync();
            var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(targetConnection).Options;
            await using var targetDb = new BusinessDbContext(options);
            await targetDb.Database.EnsureCreatedAsync();

            var result = await new LegacySqliteProductImportService(targetDb).ImportAsync(sourcePath);

            Assert.Equal("Items", result.SourceTable);
            Assert.Equal(1, result.ImportResult.ImportedCount);
            Assert.Equal("LEGACY-ROW-000001", (await targetDb.StoreProducts.SingleAsync()).Sku);
        }
        finally
        {
            if (File.Exists(sourcePath))
                File.Delete(sourcePath);
        }
    }

    [Fact]
    public async Task ImportAsync_RejectsAmbiguousProductTables()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), $"mg-legacy-{Guid.NewGuid():N}.db");
        try
        {
            await CreateLegacyDatabaseAsync(sourcePath, """
                CREATE TABLE Products (Name TEXT, Price NUMERIC);
                CREATE TABLE ProductCatalog (Name TEXT, Price NUMERIC);
                INSERT INTO Products VALUES ('Coffee', 10);
                INSERT INTO ProductCatalog VALUES ('Tea', 5);
                """);

            await using var targetConnection = new SqliteConnection("Data Source=:memory:");
            await targetConnection.OpenAsync();
            var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(targetConnection).Options;
            await using var targetDb = new BusinessDbContext(options);
            await targetDb.Database.EnsureCreatedAsync();

            var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                new LegacySqliteProductImportService(targetDb).ImportAsync(sourcePath));
            Assert.Contains("More than one product table", error.Message, StringComparison.Ordinal);
            Assert.Empty(await targetDb.StoreProducts.ToListAsync());
        }
        finally
        {
            if (File.Exists(sourcePath))
                File.Delete(sourcePath);
        }
    }

    [Fact]
    public async Task ImportAsync_RefusesTheActiveTargetDatabase()
    {
        var targetPath = Path.Combine(Path.GetTempPath(), $"mg-target-{Guid.NewGuid():N}.db");
        try
        {
            var connectionString = new SqliteConnectionStringBuilder { DataSource = targetPath }.ToString();
            var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connectionString).Options;
            await using var targetDb = new BusinessDbContext(options);
            await targetDb.Database.EnsureCreatedAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new LegacySqliteProductImportService(targetDb).ImportAsync(targetPath));
        }
        finally
        {
            if (File.Exists(targetPath))
                File.Delete(targetPath);
        }
    }

    private static async Task CreateLegacyDatabaseAsync(string path, string sql)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
