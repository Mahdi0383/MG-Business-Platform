using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using MG.BusinessPlatform.Infrastructure.Persistence;
using MG.BusinessPlatform.Infrastructure.Security;

namespace MG.BusinessPlatform.Domain.Tests.Security;

public sealed class StaffRequestContextTests
{
    private const string Password = "Strong-Passphrase-2026!";

    [Fact]
    public async Task ResolveAsync_UsesPersistedBusinessAndDefaultBranch()
    {
        await using var store = await TestStore.CreateAsync();
        var authentication = new StaffAuthenticationService(store.Db, new StaffPasswordHasher());
        await authentication.CreateInitialOwnerAsync(
            store.BusinessId, [store.BranchId], "owner", "Store Owner", Password);
        var login = await authentication.AuthenticateAsync(store.BusinessId, "owner", Password);
        Assert.NotNull(login);

        var resolver = new StaffRequestContextResolver(store.Db, authentication);
        var context = await resolver.ResolveAsync(login.Token, correlationId: "request-123");

        Assert.NotNull(context);
        Assert.Equal(store.BusinessId, context.BusinessId);
        Assert.Equal(store.BranchId, context.BranchId);
        Assert.Equal(login.Identity.StaffUserId, context.StaffUserId);
        Assert.Equal("request-123", context.CorrelationId);
        Assert.True(StaffAuthorizationPolicy.IsAllowed(context, StaffPermission.SettingsManage));
    }

    [Fact]
    public async Task ResolveAsync_RejectsUnassignedOrForeignBranch()
    {
        await using var store = await TestStore.CreateAsync();
        var authentication = new StaffAuthenticationService(store.Db, new StaffPasswordHasher());
        await authentication.CreateInitialOwnerAsync(
            store.BusinessId, [store.BranchId], "owner", "Store Owner", Password);
        var login = await authentication.AuthenticateAsync(store.BusinessId, "owner", Password);
        Assert.NotNull(login);

        var unassignedBranch = new BranchRecord
        {
            Id = Guid.NewGuid(),
            BusinessId = store.BusinessId,
            Code = "SECOND",
            Name = "Unassigned branch",
            IsActive = true
        };
        var foreignBusiness = new BusinessRecord
        {
            Id = Guid.NewGuid(),
            LegalName = "Foreign business",
            DisplayName = "Foreign business",
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
        store.Db.Branches.AddRange(unassignedBranch, foreignBranch);
        await store.Db.SaveChangesAsync();

        var resolver = new StaffRequestContextResolver(store.Db, authentication);
        Assert.Null(await resolver.ResolveAsync(login.Token, unassignedBranch.Id));
        Assert.Null(await resolver.ResolveAsync(login.Token, foreignBranch.Id));
    }

    [Fact]
    public async Task ResolveAsync_RejectsRevokedSessionsAndDisabledBranches()
    {
        await using var store = await TestStore.CreateAsync();
        var authentication = new StaffAuthenticationService(store.Db, new StaffPasswordHasher());
        await authentication.CreateInitialOwnerAsync(
            store.BusinessId, [store.BranchId], "owner", "Store Owner", Password);
        var login = await authentication.AuthenticateAsync(store.BusinessId, "owner", Password);
        Assert.NotNull(login);
        var resolver = new StaffRequestContextResolver(store.Db, authentication);

        Assert.NotNull(await resolver.ResolveAsync(login.Token));
        var branch = await store.Db.Branches.SingleAsync(x => x.Id == store.BranchId);
        branch.IsActive = false;
        await store.Db.SaveChangesAsync();
        Assert.Null(await resolver.ResolveAsync(login.Token));

        branch.IsActive = true;
        await store.Db.SaveChangesAsync();
        Assert.True(await authentication.RevokeSessionAsync(store.BusinessId, login.Token));
        Assert.Null(await resolver.ResolveAsync(login.Token));
    }

    [Fact]
    public async Task Authorization_IsDenyByDefaultAndRespectsRolePermissions()
    {
        await using var store = await TestStore.CreateAsync();
        var authentication = new StaffAuthenticationService(store.Db, new StaffPasswordHasher());
        var userId = await authentication.CreateInitialOwnerAsync(
            store.BusinessId, [store.BranchId], "owner", "Store Owner", Password);
        var login = await authentication.AuthenticateAsync(store.BusinessId, "owner", Password);
        Assert.NotNull(login);

        var user = await store.Db.StaffUsers.SingleAsync(x => x.Id == userId);
        user.Role = "Cashier";
        await store.Db.SaveChangesAsync();

        var resolver = new StaffRequestContextResolver(store.Db, authentication);
        var context = await resolver.ResolveAsync(login.Token);
        Assert.NotNull(context);
        Assert.True(StaffAuthorizationPolicy.IsAllowed(context, StaffPermission.SalesCreate));
        Assert.True(StaffAuthorizationPolicy.IsAllowed(context, StaffPermission.GameNetManage));
        Assert.False(StaffAuthorizationPolicy.IsAllowed(context, StaffPermission.AccountingManage));
        Assert.False(StaffAuthorizationPolicy.IsAllowed(context, StaffPermission.StaffManage));
        Assert.False(StaffAuthorizationPolicy.IsAllowed(null, StaffPermission.DashboardRead));
        Assert.False(StaffAuthorizationPolicy.IsRoleAllowed("UnknownRole", StaffPermission.DashboardRead));
        Assert.False(StaffAuthorizationPolicy.IsRoleAllowed(null, StaffPermission.DashboardRead));
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
            await db.Database.MigrateAsync();

            var businessId = Guid.NewGuid();
            var branchId = Guid.NewGuid();
            db.Businesses.Add(new BusinessRecord
            {
                Id = businessId,
                LegalName = "Test business",
                DisplayName = "Test business",
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
                StoreName = "Test business",
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
