using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using MG.BusinessPlatform.Infrastructure.Persistence;

namespace MG.BusinessPlatform.Domain.Tests.Persistence;

public sealed class BusinessOwnedCatalogTests
{
    [Fact]
    public async Task InitializeAsync_BackfillsLegacyCatalogAndLedgerRows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await db.Database.MigrateAsync();

        var now = DateTimeOffset.UtcNow;
        db.StoreProducts.Add(new StoreProduct
        {
            Id = Guid.NewGuid(),
            Sku = "LEGACY-PRODUCT-1",
            Name = "Legacy product",
            Description = string.Empty,
            UnitPrice = 100m,
            UnitCost = 50m,
            CurrencyCode = "IRR",
            StockQuantity = 3,
            UpdatedAt = now
        });
        db.LedgerAccounts.Add(new LedgerAccountRecord
        {
            Id = Guid.NewGuid(),
            Code = "9900",
            Name = "Legacy account",
            AccountType = "Expense",
            IsPostingAllowed = true,
            IsActive = true
        });
        db.Customers.Add(new CustomerRecord
        {
            Id = Guid.NewGuid(),
            DisplayName = "Legacy customer",
            Phone = "09120000001",
            Email = string.Empty,
            Address = string.Empty,
            CreatedAt = now,
            IsActive = true
        });
        db.Suppliers.Add(new SupplierRecord
        {
            Id = Guid.NewGuid(),
            DisplayName = "Legacy supplier",
            Phone = "02100000001",
            Email = string.Empty,
            Address = string.Empty,
            TaxIdentifier = string.Empty,
            PaymentTerms = string.Empty,
            CreatedAt = now,
            IsActive = true
        });
        await db.SaveChangesAsync();

        await DatabaseInitializer.InitializeAsync(db, defaultBranchId: Guid.NewGuid());

        var businessId = (await db.StoreSettings.AsNoTracking().SingleAsync()).BusinessId;
        Assert.NotNull(businessId);
        Assert.All(await db.StoreProducts.AsNoTracking().ToListAsync(), product => Assert.Equal(businessId, product.BusinessId));
        Assert.All(await db.LedgerAccounts.AsNoTracking().ToListAsync(), account => Assert.Equal(businessId, account.BusinessId));
        Assert.All(await db.Customers.AsNoTracking().ToListAsync(), customer => Assert.Equal(businessId, customer.BusinessId));
        Assert.All(await db.Suppliers.AsNoTracking().ToListAsync(), supplier => Assert.Equal(businessId, supplier.BusinessId));
    }

    [Fact]
    public async Task SaveChanges_StampsNewBusinessOwnedRecordsFromProvisionedSettings()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await DatabaseInitializer.InitializeAsync(db, defaultBranchId: Guid.NewGuid());

        var now = DateTimeOffset.UtcNow;
        var product = new StoreProduct
        {
            Id = Guid.NewGuid(),
            Sku = "NEW-PRODUCT-1",
            Name = "New product",
            Description = string.Empty,
            UnitPrice = 100m,
            UnitCost = 50m,
            CurrencyCode = "IRR",
            StockQuantity = 0,
            UpdatedAt = now
        };
        var customer = new CustomerRecord
        {
            Id = Guid.NewGuid(),
            DisplayName = "New customer",
            Phone = "09120000002",
            Email = string.Empty,
            Address = string.Empty,
            CreatedAt = now
        };
        var supplier = new SupplierRecord
        {
            Id = Guid.NewGuid(),
            DisplayName = "New supplier",
            Phone = "02100000002",
            Email = string.Empty,
            Address = string.Empty,
            TaxIdentifier = string.Empty,
            PaymentTerms = string.Empty,
            CreatedAt = now
        };
        var account = new LedgerAccountRecord
        {
            Id = Guid.NewGuid(),
            Code = "9901",
            Name = "New account",
            AccountType = "Expense"
        };

        db.AddRange(product, customer, supplier, account);
        await db.SaveChangesAsync();

        var businessId = (await db.StoreSettings.AsNoTracking().SingleAsync()).BusinessId;
        Assert.NotNull(businessId);
        Assert.Equal(businessId, product.BusinessId);
        Assert.Equal(businessId, customer.BusinessId);
        Assert.Equal(businessId, supplier.BusinessId);
        Assert.Equal(businessId, account.BusinessId);
    }

    [Fact]
    public async Task SaveChanges_RejectsCallerSuppliedDifferentBusinessId()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await DatabaseInitializer.InitializeAsync(db, defaultBranchId: Guid.NewGuid());

        var foreignBusiness = new BusinessRecord
        {
            Id = Guid.NewGuid(),
            LegalName = "Other business",
            DisplayName = "Other business",
            DefaultCurrencyCode = "IRR",
            TimeZoneId = "UTC",
            IsActive = true
        };
        db.Businesses.Add(foreignBusiness);
        await db.SaveChangesAsync();

        db.StoreProducts.Add(new StoreProduct
        {
            Id = Guid.NewGuid(),
            BusinessId = foreignBusiness.Id,
            Sku = "FOREIGN-PRODUCT-1",
            Name = "Foreign product",
            Description = string.Empty,
            UnitPrice = 100m,
            UnitCost = 50m,
            CurrencyCode = "IRR",
            StockQuantity = 0,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.False(await db.StoreProducts.AnyAsync(product => product.Sku == "FOREIGN-PRODUCT-1"));
    }

    [Fact]
    public async Task SaveChanges_PreventsReassigningProvisionedStoreToAnotherBusiness()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await DatabaseInitializer.InitializeAsync(db, defaultBranchId: Guid.NewGuid());

        var originalBusinessId = (await db.StoreSettings.AsNoTracking().SingleAsync()).BusinessId;
        var foreignBusiness = new BusinessRecord
        {
            Id = Guid.NewGuid(),
            LegalName = "Other business",
            DisplayName = "Other business",
            DefaultCurrencyCode = "IRR",
            TimeZoneId = "UTC",
            IsActive = true
        };
        db.Businesses.Add(foreignBusiness);
        await db.SaveChangesAsync();

        var settings = await db.StoreSettings.SingleAsync();
        settings.BusinessId = foreignBusiness.Id;

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Equal(originalBusinessId,
            (await db.StoreSettings.AsNoTracking().SingleAsync()).BusinessId);
    }

    [Fact]
    public async Task SaveChanges_PreventsChangingExistingRecordBusinessId()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await DatabaseInitializer.InitializeAsync(db, defaultBranchId: Guid.NewGuid());

        var product = new StoreProduct
        {
            Id = Guid.NewGuid(),
            Sku = "IMMUTABLE-PRODUCT-1",
            Name = "Owned product",
            Description = string.Empty,
            UnitPrice = 100m,
            UnitCost = 50m,
            CurrencyCode = "IRR",
            StockQuantity = 0,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.StoreProducts.Add(product);
        await db.SaveChangesAsync();

        var foreignBusiness = new BusinessRecord
        {
            Id = Guid.NewGuid(),
            LegalName = "Other business",
            DisplayName = "Other business",
            DefaultCurrencyCode = "IRR",
            TimeZoneId = "UTC",
            IsActive = true
        };
        db.Businesses.Add(foreignBusiness);
        await db.SaveChangesAsync();

        product.BusinessId = foreignBusiness.Id;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.NotEqual(foreignBusiness.Id,
            (await db.StoreProducts.AsNoTracking().SingleAsync(x => x.Id == product.Id)).BusinessId);
    }
}
