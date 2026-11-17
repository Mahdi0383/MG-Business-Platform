using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using MG.BusinessPlatform.Infrastructure.Persistence;
using MG.BusinessPlatform.Infrastructure.Security;

namespace MG.BusinessPlatform.Domain.Tests.Security;

public sealed class StaffAuditAttributionTests
{
    private const string Password = "Strong-Passphrase-2026!";

    [Fact]
    public async Task SaveChanges_AttributesAuditRowsToValidatedStaffRequestAndBranch()
    {
        await using var store = await TestStore.CreateAsync();
        var authentication = new StaffAuthenticationService(store.Db, new StaffPasswordHasher());
        await authentication.CreateInitialOwnerAsync(
            store.BusinessId, [store.BranchId], "owner", "Store Owner", Password);
        var login = await authentication.AuthenticateAsync(store.BusinessId, "owner", Password);
        Assert.NotNull(login);

        var resolver = new StaffRequestContextResolver(store.Db, authentication);
        var context = await resolver.ResolveAsync(login.Token, correlationId: "trace-abc-123");
        Assert.NotNull(context);

        var product = new StoreProduct
        {
            Id = Guid.NewGuid(),
            Sku = "AUDIT-STAFF-01",
            Name = "Staff-attributed product",
            UnitPrice = 100m,
            CurrencyCode = "IRR",
            IsAvailable = true,
            StockQuantity = 1,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        using (StaffRequestContextScope.Push(context))
        {
            store.Db.StoreProducts.Add(product);
            await store.Db.SaveChangesAsync();
        }

        var audit = await store.Db.AuditEvents.AsNoTracking()
            .SingleAsync(x => x.EntityType == nameof(StoreProduct) && x.EntityId == product.Id.ToString());
        Assert.Equal(store.BusinessId, audit.BusinessId);
        Assert.Equal(store.BranchId, audit.BranchId);
        Assert.Equal(login.Identity.StaffUserId, audit.StaffUserId);
        Assert.Equal("Staff", audit.ActorType);
        Assert.Equal("OWNER", audit.Actor);
        Assert.Equal("trace-abc-123", audit.CorrelationId);
    }

    [Fact]
    public async Task SaveChanges_ExplicitlyClassifiesUnscopedWritesAsLegacyProcess()
    {
        await using var store = await TestStore.CreateAsync();
        var product = new StoreProduct
        {
            Id = Guid.NewGuid(),
            Sku = "AUDIT-LEGACY-01",
            Name = "Legacy process product",
            UnitPrice = 100m,
            CurrencyCode = "IRR",
            IsAvailable = true,
            StockQuantity = 1,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        store.Db.StoreProducts.Add(product);
        await store.Db.SaveChangesAsync();

        var audit = await store.Db.AuditEvents.AsNoTracking()
            .SingleAsync(x => x.EntityType == nameof(StoreProduct) && x.EntityId == product.Id.ToString());
        Assert.Equal("LegacyProcess", audit.ActorType);
        Assert.Null(audit.StaffUserId);
        Assert.Equal(store.BusinessId, audit.BusinessId);
        Assert.Equal(store.BranchId, audit.BranchId);
        Assert.False(string.IsNullOrWhiteSpace(audit.CorrelationId));
        Assert.False(string.IsNullOrWhiteSpace(audit.Actor));
    }

    private sealed class TestStore(SqliteConnection connection, BusinessDbContext db, Guid businessId, Guid branchId)
        : IAsyncDisposable
    {
        public BusinessDbContext Db { get; } = db;
        public Guid BusinessId { get; } = businessId;
        public Guid BranchId { get; } = branchId;

        public static async Task<TestStore> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
            var db = new BusinessDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var businessId = Guid.NewGuid();
            var branchId = Guid.NewGuid();
            db.Businesses.Add(new BusinessRecord
            {
                Id = businessId,
                LegalName = "Audit test business",
                DisplayName = "Audit test business",
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
                StoreName = "Audit test business",
                CurrencyCode = "IRR",
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            return new TestStore(connection, db, businessId, branchId);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
