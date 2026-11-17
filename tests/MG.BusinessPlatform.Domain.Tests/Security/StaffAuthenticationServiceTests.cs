using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using MG.BusinessPlatform.Infrastructure.Persistence;
using MG.BusinessPlatform.Infrastructure.Security;

namespace MG.BusinessPlatform.Domain.Tests.Security;

public sealed class StaffAuthenticationServiceTests
{
    private const string InitialPassword = "Strong-Passphrase-2026!";

    [Fact]
    public async Task InitialOwner_CanAuthenticateValidateAndRevokeOpaqueSession()
    {
        await using var store = await TestStore.CreateAsync();
        var hasher = new StaffPasswordHasher();
        var service = new StaffAuthenticationService(store.Db, hasher);

        var staffUserId = await service.CreateInitialOwnerAsync(
            store.BusinessId, [store.BranchId], "owner", "Store Owner", InitialPassword);
        var login = await service.AuthenticateAsync(
            store.BusinessId, " OWNER ", InitialPassword, "desktop test");

        Assert.NotNull(login);
        Assert.Equal(staffUserId, login.Identity.StaffUserId);
        Assert.Equal(store.BusinessId, login.Identity.BusinessId);
        Assert.Equal("OWNER", login.Identity.UserName);
        Assert.Equal("Owner", login.Identity.Role);
        Assert.Contains(store.BranchId, login.Identity.BranchIds);
        Assert.Equal(43, login.Token.Length);

        var persistedSession = await store.Db.StaffSessions.SingleAsync();
        Assert.NotEqual(login.Token, persistedSession.TokenHash);
        Assert.Equal(64, persistedSession.TokenHash.Length);

        var identity = await service.ValidateSessionAsync(store.BusinessId, login.Token);
        Assert.NotNull(identity);
        Assert.Equal(staffUserId, identity.StaffUserId);

        Assert.True(await service.RevokeSessionAsync(store.BusinessId, login.Token));
        Assert.Null(await service.ValidateSessionAsync(store.BusinessId, login.Token));
        Assert.False(await service.RevokeSessionAsync(store.BusinessId, login.Token));
    }

    [Fact]
    public async Task Authentication_UsesLockoutAfterFiveFailedAttempts()
    {
        await using var store = await TestStore.CreateAsync();
        var service = new StaffAuthenticationService(store.Db, new StaffPasswordHasher());
        await service.CreateInitialOwnerAsync(
            store.BusinessId, [store.BranchId], "owner", "Store Owner", InitialPassword);
        var existingLogin = await service.AuthenticateAsync(store.BusinessId, "owner", InitialPassword);
        Assert.NotNull(existingLogin);

        for (var attempt = 0; attempt < 5; attempt++)
            Assert.Null(await service.AuthenticateAsync(store.BusinessId, "owner", "Wrong-password-2026!"));

        var user = await store.Db.StaffUsers.SingleAsync();
        Assert.Equal(5, user.FailedLoginCount);
        Assert.True(user.LockedUntilUtc > DateTimeOffset.UtcNow);
        Assert.Null(await service.AuthenticateAsync(store.BusinessId, "owner", InitialPassword));
        Assert.Null(await service.ValidateSessionAsync(store.BusinessId, existingLogin.Token));
        Assert.NotNull((await store.Db.StaffSessions.AsNoTracking().SingleAsync()).RevokedAtUtc);
    }

    [Fact]
    public async Task DisabledStaffCannotAuthenticateOrUseExistingSession()
    {
        await using var store = await TestStore.CreateAsync();
        var service = new StaffAuthenticationService(store.Db, new StaffPasswordHasher());
        var userId = await service.CreateInitialOwnerAsync(
            store.BusinessId, [store.BranchId], "owner", "Store Owner", InitialPassword);
        var login = await service.AuthenticateAsync(store.BusinessId, "owner", InitialPassword);
        Assert.NotNull(login);

        var user = await store.Db.StaffUsers.SingleAsync(x => x.Id == userId);
        user.IsActive = false;
        await store.Db.SaveChangesAsync();

        Assert.Null(await service.AuthenticateAsync(store.BusinessId, "owner", InitialPassword));
        Assert.Null(await service.ValidateSessionAsync(store.BusinessId, login.Token));
    }

    [Fact]
    public async Task ExpiredAndBulkRevokedSessionsCannotBeValidated()
    {
        await using var store = await TestStore.CreateAsync();
        var service = new StaffAuthenticationService(store.Db, new StaffPasswordHasher());
        var userId = await service.CreateInitialOwnerAsync(
            store.BusinessId, [store.BranchId], "owner", "Store Owner", InitialPassword);
        var first = await service.AuthenticateAsync(store.BusinessId, "owner", InitialPassword);
        var second = await service.AuthenticateAsync(store.BusinessId, "owner", InitialPassword);
        Assert.NotNull(first);
        Assert.NotNull(second);

        var firstSession = await store.Db.StaffSessions.SingleAsync(
            x => x.TokenHash == HashToken(first.Token));
        firstSession.CreatedAtUtc = DateTimeOffset.UtcNow.AddHours(-2);
        firstSession.ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(-1);
        await store.Db.SaveChangesAsync();
        Assert.Null(await service.ValidateSessionAsync(store.BusinessId, first.Token));

        Assert.Equal(2, await service.RevokeAllSessionsAsync(store.BusinessId, userId));
        Assert.Null(await service.ValidateSessionAsync(store.BusinessId, second.Token));
    }

    private static string HashToken(string token)
    {
        var bytes = Convert.FromBase64String(token.Replace('-', '+').Replace('_', '/') + "=");
        try
        {
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
        }
    }

    [Fact]
    public async Task InitialOwner_CannotBeCreatedTwice()
    {
        await using var store = await TestStore.CreateAsync();
        var service = new StaffAuthenticationService(store.Db, new StaffPasswordHasher());
        await service.CreateInitialOwnerAsync(
            store.BusinessId, [store.BranchId], "owner", "Store Owner", InitialPassword);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateInitialOwnerAsync(
                store.BusinessId, [store.BranchId], "second-owner", "Second Owner", InitialPassword));
        Assert.Single(await store.Db.StaffUsers.ToListAsync());
    }

    [Fact]
    public async Task InitialOwner_RejectsBranchFromAnotherBusiness()
    {
        await using var store = await TestStore.CreateAsync();
        var otherBusiness = new BusinessRecord
        {
            Id = Guid.NewGuid(),
            LegalName = "Other",
            DisplayName = "Other",
            DefaultCurrencyCode = "IRR",
            TimeZoneId = "UTC",
            IsActive = true
        };
        var foreignBranch = new BranchRecord
        {
            Id = Guid.NewGuid(),
            BusinessId = otherBusiness.Id,
            Code = "OTHER",
            Name = "Foreign branch",
            IsActive = true
        };
        store.Db.Businesses.Add(otherBusiness);
        store.Db.Branches.Add(foreignBranch);
        await store.Db.SaveChangesAsync();

        var service = new StaffAuthenticationService(store.Db, new StaffPasswordHasher());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateInitialOwnerAsync(
                store.BusinessId, [foreignBranch.Id], "owner", "Store Owner", InitialPassword));
        Assert.Empty(await store.Db.StaffUsers.ToListAsync());
    }

    [Fact]
    public async Task SessionToken_IsBoundToBusinessAndRejectsMalformedInput()
    {
        await using var store = await TestStore.CreateAsync();
        var service = new StaffAuthenticationService(store.Db, new StaffPasswordHasher());
        await service.CreateInitialOwnerAsync(
            store.BusinessId, [store.BranchId], "owner", "Store Owner", InitialPassword);
        var login = await service.AuthenticateAsync(store.BusinessId, "owner", InitialPassword);
        Assert.NotNull(login);

        Assert.Null(await service.ValidateSessionAsync(Guid.NewGuid(), login.Token));
        Assert.Null(await service.ValidateSessionAsync(store.BusinessId, "not-a-session-token"));
        Assert.False(await service.RevokeSessionAsync(Guid.NewGuid(), login.Token));
    }

    private sealed class TestStore(SqliteConnection connection, BusinessDbContext db, Guid businessId, Guid branchId)
        : IAsyncDisposable
    {
        public SqliteConnection Connection { get; } = connection;
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

            var now = DateTimeOffset.UtcNow;
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
                UpdatedAt = now
            });
            await db.SaveChangesAsync();
            return new TestStore(connection, db, businessId, branchId);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
