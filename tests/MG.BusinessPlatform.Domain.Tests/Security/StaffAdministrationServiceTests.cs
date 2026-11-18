using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using MG.BusinessPlatform.Infrastructure.Persistence;
using MG.BusinessPlatform.Infrastructure.Security;

namespace MG.BusinessPlatform.Domain.Tests.Security;

public sealed class StaffAdministrationServiceTests
{
    private const string OwnerPassword = "Owner-Passphrase-2026!";
    private const string CashierPassword = "Cashier-Passphrase-2026!";

    [Fact]
    public async Task Owner_CanCreateListAndUpdateStaffWithScopedBranchGrants()
    {
        await using var store = await TestStore.CreateAsync();
        var hasher = new StaffPasswordHasher();
        var authentication = new StaffAuthenticationService(store.Db, hasher);
        var ownerId = await authentication.CreateInitialOwnerAsync(
            store.BusinessId, [store.MainBranchId, store.SecondBranchId],
            "owner", "Store Owner", OwnerPassword);
        var ownerLogin = await authentication.AuthenticateAsync(
            store.BusinessId, "owner", OwnerPassword);
        Assert.NotNull(ownerLogin);

        var resolver = new StaffRequestContextResolver(store.Db, authentication);
        var ownerContext = await resolver.ResolveAsync(ownerLogin.Token);
        Assert.NotNull(ownerContext);

        var administration = new StaffAdministrationService(store.Db, hasher);
        Guid cashierId;
        StaffLoginSession cashierSessionBeforeUpdate;
        using (StaffRequestContextScope.Push(ownerContext!))
        {
            cashierId = await administration.CreateStaffAsync(
                "cashier", "Front Cashier", CashierPassword, "Cashier",
                [store.MainBranchId, store.SecondBranchId]);
            cashierSessionBeforeUpdate = (await authentication.AuthenticateAsync(
                store.BusinessId, "cashier", CashierPassword))!;

            var staff = await administration.ListStaffAsync();
            Assert.Equal(2, staff.Count);
            var cashier = Assert.Single(staff, x => x.StaffUserId == cashierId);
            Assert.Equal("CASHIER", cashier.UserName);
            Assert.Equal("Cashier", cashier.Role);
            Assert.Equal(
                new[] { store.MainBranchId, store.SecondBranchId }.OrderBy(x => x),
                cashier.BranchIds);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                administration.UpdateStaffAccessAsync(
                    ownerId, "Cashier", true, [store.MainBranchId]));

            await administration.UpdateStaffAccessAsync(
                cashierId, "Accountant", true, [store.SecondBranchId]);
        }

        Assert.Null(await authentication.ValidateSessionAsync(
            store.BusinessId, cashierSessionBeforeUpdate.Token));
        var cashierLogin = await authentication.AuthenticateAsync(
            store.BusinessId, "cashier", CashierPassword);
        Assert.NotNull(cashierLogin);
        var cashierUser = await store.Db.StaffUsers.AsNoTracking()
            .SingleAsync(x => x.Id == cashierId);
        Assert.Equal("Accountant", cashierUser.Role);
        Assert.NotEqual(CashierPassword, cashierUser.PasswordHash);

        var cashierAccess = await store.Db.StaffBranchAccess.AsNoTracking()
            .Where(x => x.StaffUserId == cashierId)
            .Select(x => x.BranchId)
            .OrderBy(x => x)
            .ToArrayAsync();
        Assert.Equal(new[] { store.SecondBranchId }, cashierAccess);

        var auditEvents = await store.Db.AuditEvents.AsNoTracking().ToListAsync();
        Assert.Contains(auditEvents, audit => audit.StaffUserId == ownerId);
    }

    [Fact]
    public async Task Cashier_CannotCreateOrManageStaff()
    {
        await using var store = await TestStore.CreateAsync();
        var hasher = new StaffPasswordHasher();
        var authentication = new StaffAuthenticationService(store.Db, hasher);
        await authentication.CreateInitialOwnerAsync(
            store.BusinessId, [store.MainBranchId], "owner", "Store Owner", OwnerPassword);
        var ownerLogin = await authentication.AuthenticateAsync(
            store.BusinessId, "owner", OwnerPassword);
        Assert.NotNull(ownerLogin);
        var resolver = new StaffRequestContextResolver(store.Db, authentication);
        var ownerContext = await resolver.ResolveAsync(ownerLogin.Token);
        Assert.NotNull(ownerContext);

        var administration = new StaffAdministrationService(store.Db, hasher);
        using (StaffRequestContextScope.Push(ownerContext!))
        {
            await administration.CreateStaffAsync(
                "cashier", "Front Cashier", CashierPassword, "Cashier", [store.MainBranchId]);
        }

        var cashierLogin = await authentication.AuthenticateAsync(
            store.BusinessId, "cashier", CashierPassword);
        Assert.NotNull(cashierLogin);
        var cashierContext = await resolver.ResolveAsync(cashierLogin.Token);
        Assert.NotNull(cashierContext);

        using (StaffRequestContextScope.Push(cashierContext!))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                administration.CreateStaffAsync(
                    "second", "Second User", "Second-Passphrase-2026!", "ReadOnly",
                    [store.MainBranchId]));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                administration.UpdateStaffAccessAsync(
                    cashierLogin.Identity.StaffUserId, "Manager", true, [store.MainBranchId]));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                administration.ListStaffAsync());
        }
    }

    [Fact]
    public async Task StaffAdministration_RejectsForeignBranchesAndUnknownRoles()
    {
        await using var store = await TestStore.CreateAsync();
        var hasher = new StaffPasswordHasher();
        var authentication = new StaffAuthenticationService(store.Db, hasher);
        await authentication.CreateInitialOwnerAsync(
            store.BusinessId, [store.MainBranchId], "owner", "Store Owner", OwnerPassword);
        var login = await authentication.AuthenticateAsync(store.BusinessId, "owner", OwnerPassword);
        Assert.NotNull(login);
        var resolver = new StaffRequestContextResolver(store.Db, authentication);
        var context = await resolver.ResolveAsync(login.Token);
        Assert.NotNull(context);

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
        store.Db.Branches.Add(foreignBranch);
        var foreignStaffId = Guid.NewGuid();
        const string foreignPassword = "Foreign-Owner-Passphrase-2026!";
        var foreignNow = DateTimeOffset.UtcNow;
        store.Db.StaffUsers.Add(new StaffUserRecord
        {
            Id = foreignStaffId,
            BusinessId = foreignBusiness.Id,
            UserNameNormalized = "FOREIGN-OWNER",
            DisplayName = "Foreign Owner",
            PasswordHash = hasher.Hash(foreignPassword),
            Role = "Owner",
            IsActive = true,
            CreatedAtUtc = foreignNow,
            UpdatedAtUtc = foreignNow
        });
        store.Db.StaffBranchAccess.Add(new StaffBranchAccessRecord
        {
            BusinessId = foreignBusiness.Id,
            StaffUserId = foreignStaffId,
            BranchId = foreignBranch.Id,
            GrantedAtUtc = foreignNow
        });
        await store.Db.SaveChangesAsync();

        // Even a valid account in another persisted business cannot authenticate
        // through the local single-business staff service.
        Assert.Null(await authentication.AuthenticateAsync(
            foreignBusiness.Id, "foreign-owner", foreignPassword));

        var administration = new StaffAdministrationService(store.Db, hasher);
        using (StaffRequestContextScope.Push(context!))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                administration.CreateStaffAsync(
                    "cashier", "Front Cashier", CashierPassword, "Cashier", [foreignBranch.Id]));
            await Assert.ThrowsAsync<ArgumentException>(() =>
                administration.CreateStaffAsync(
                    "cashier", "Front Cashier", CashierPassword, "SuperUser", [store.MainBranchId]));
            await Assert.ThrowsAsync<ArgumentException>(() =>
                administration.CreateStaffAsync(
                    "cashier", "Front Cashier", CashierPassword, "Cashier", []));
        }

        Assert.Empty(await store.Db.StaffUsers.Where(x => x.UserNameNormalized == "CASHIER").ToListAsync());
    }

    [Fact]
    public async Task UpdateStaffAccess_CannotRemoveLastActiveAdministratorFromStaleRequestScope()
    {
        await using var store = await TestStore.CreateAsync();
        var hasher = new StaffPasswordHasher();
        var authentication = new StaffAuthenticationService(store.Db, hasher);
        await authentication.CreateInitialOwnerAsync(
            store.BusinessId, [store.MainBranchId], "owner", "Store Owner", OwnerPassword);
        var ownerLogin = await authentication.AuthenticateAsync(
            store.BusinessId, "owner", OwnerPassword);
        Assert.NotNull(ownerLogin);

        var resolver = new StaffRequestContextResolver(store.Db, authentication);
        var ownerContext = await resolver.ResolveAsync(ownerLogin.Token);
        Assert.NotNull(ownerContext);
        var administration = new StaffAdministrationService(store.Db, hasher);
        Guid secondOwnerId;
        using (StaffRequestContextScope.Push(ownerContext!))
        {
            secondOwnerId = await administration.CreateStaffAsync(
                "second-owner", "Second Owner", "Second-Owner-Passphrase-2026!",
                "Owner", [store.MainBranchId]);
        }

        var secondOwnerLogin = await authentication.AuthenticateAsync(
            store.BusinessId, "second-owner", "Second-Owner-Passphrase-2026!");
        Assert.NotNull(secondOwnerLogin);
        var secondOwnerContext = await resolver.ResolveAsync(secondOwnerLogin.Token);
        Assert.NotNull(secondOwnerContext);

        Guid thirdOwnerId;
        using (StaffRequestContextScope.Push(ownerContext!))
        {
            thirdOwnerId = await administration.CreateStaffAsync(
                "third-owner", "Third Owner", "Third-Owner-Passphrase-2026!",
                "Owner", [store.MainBranchId]);
        }

        using (StaffRequestContextScope.Push(ownerContext!))
        {
            await administration.UpdateStaffAccessAsync(
                secondOwnerId, "Cashier", true, [store.MainBranchId]);
        }

        // Simulate a request that resolved its Owner context just before its session/role was revoked.
        using (StaffRequestContextScope.Push(secondOwnerContext!))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                administration.UpdateStaffAccessAsync(
                    ownerLogin.Identity.StaffUserId, "Cashier", true, [store.MainBranchId]));
        }

        var remainingOwner = await store.Db.StaffUsers.AsNoTracking()
            .SingleAsync(x => x.Id == ownerLogin.Identity.StaffUserId);
        Assert.Equal("Owner", remainingOwner.Role);
        Assert.True(remainingOwner.IsActive);
        Assert.Equal("Cashier", (await store.Db.StaffUsers.AsNoTracking()
            .SingleAsync(x => x.Id == secondOwnerId)).Role);
        Assert.Equal("Owner", (await store.Db.StaffUsers.AsNoTracking()
            .SingleAsync(x => x.Id == thirdOwnerId)).Role);
    }

    [Fact]
    public async Task LiveRoleDowngrade_IsEnforcedEvenWhenSessionWasNotRevoked()
    {
        await using var store = await TestStore.CreateAsync();
        var hasher = new StaffPasswordHasher();
        var authentication = new StaffAuthenticationService(store.Db, hasher);
        var ownerId = await authentication.CreateInitialOwnerAsync(
            store.BusinessId, [store.MainBranchId], "owner", "Store Owner", OwnerPassword);
        var ownerLogin = await authentication.AuthenticateAsync(
            store.BusinessId, "owner", OwnerPassword);
        Assert.NotNull(ownerLogin);

        var resolver = new StaffRequestContextResolver(store.Db, authentication);
        var staleOwnerContext = await resolver.ResolveAsync(ownerLogin.Token);
        Assert.NotNull(staleOwnerContext);

        // Simulate a role change made by another trusted administrative path that did
        // not revoke sessions. Authorization must still use the current persisted role.
        var owner = await store.Db.StaffUsers.SingleAsync(x => x.Id == ownerId);
        owner.Role = "ReadOnly";
        await store.Db.SaveChangesAsync();

        var stillValidSession = await authentication.ValidateSessionAsync(
            store.BusinessId, ownerLogin.Token);
        Assert.NotNull(stillValidSession);
        Assert.Equal("ReadOnly", stillValidSession.Role);

        var administration = new StaffAdministrationService(store.Db, hasher);
        using (StaffRequestContextScope.Push(staleOwnerContext!))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                administration.CreateStaffAsync(
                    "blocked-user", "Blocked User", "Blocked-Passphrase-2026!",
                    "Cashier", [store.MainBranchId]));
        }

        Assert.Empty(await store.Db.StaffUsers.AsNoTracking()
            .Where(x => x.UserNameNormalized == "BLOCKED-USER").ToListAsync());
    }

    [Fact]
    public async Task RevokedStaffContext_CannotReadCreateOrChangePassword()
    {
        await using var store = await TestStore.CreateAsync();
        var hasher = new StaffPasswordHasher();
        var authentication = new StaffAuthenticationService(store.Db, hasher);
        await authentication.CreateInitialOwnerAsync(
            store.BusinessId, [store.MainBranchId], "owner", "Store Owner", OwnerPassword);
        var ownerLogin = await authentication.AuthenticateAsync(
            store.BusinessId, "owner", OwnerPassword);
        Assert.NotNull(ownerLogin);

        var resolver = new StaffRequestContextResolver(store.Db, authentication);
        var ownerContext = await resolver.ResolveAsync(ownerLogin.Token);
        Assert.NotNull(ownerContext);
        var administration = new StaffAdministrationService(store.Db, hasher);
        Guid managerId;
        using (StaffRequestContextScope.Push(ownerContext!))
        {
            managerId = await administration.CreateStaffAsync(
                "manager", "Store Manager", "Manager-Passphrase-2026!",
                "Manager", [store.MainBranchId]);
        }

        var managerLogin = await authentication.AuthenticateAsync(
            store.BusinessId, "manager", "Manager-Passphrase-2026!");
        Assert.NotNull(managerLogin);
        var staleManagerContext = await resolver.ResolveAsync(managerLogin.Token);
        Assert.NotNull(staleManagerContext);

        using (StaffRequestContextScope.Push(ownerContext!))
        {
            await administration.UpdateStaffAccessAsync(
                managerId, "Manager", false, [store.MainBranchId]);
        }

        using (StaffRequestContextScope.Push(staleManagerContext!))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                administration.ListStaffAsync());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                administration.CreateStaffAsync(
                    "blocked-user", "Blocked User", "Blocked-Passphrase-2026!",
                    "ReadOnly", [store.MainBranchId]));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                administration.ChangeOwnPasswordAsync(
                    "Manager-Passphrase-2026!", "New-Manager-Passphrase-2026!"));
        }

        Assert.False((await store.Db.StaffUsers.AsNoTracking()
            .SingleAsync(x => x.Id == managerId)).IsActive);
        Assert.DoesNotContain(await store.Db.StaffUsers.AsNoTracking().ToListAsync(),
            x => x.UserNameNormalized == "BLOCKED-USER");
    }

    [Fact]
    public async Task StaleContext_CannotReadOrMutateAfterBranchGrantRemovalOrBranchDeactivation()
    {
        await using var store = await TestStore.CreateAsync();
        var hasher = new StaffPasswordHasher();
        var authentication = new StaffAuthenticationService(store.Db, hasher);
        await authentication.CreateInitialOwnerAsync(
            store.BusinessId, [store.MainBranchId, store.SecondBranchId],
            "owner", "Store Owner", OwnerPassword);
        var login = await authentication.AuthenticateAsync(
            store.BusinessId, "owner", OwnerPassword);
        Assert.NotNull(login);

        var resolver = new StaffRequestContextResolver(store.Db, authentication);
        var mainContext = await resolver.ResolveAsync(login.Token, store.MainBranchId);
        var secondContext = await resolver.ResolveAsync(login.Token, store.SecondBranchId);
        Assert.NotNull(mainContext);
        Assert.NotNull(secondContext);

        var secondGrant = await store.Db.StaffBranchAccess.SingleAsync(
            x => x.StaffUserId == login.Identity.StaffUserId && x.BranchId == store.SecondBranchId);
        store.Db.StaffBranchAccess.Remove(secondGrant);
        var mainBranch = await store.Db.Branches.SingleAsync(x => x.Id == store.MainBranchId);
        mainBranch.IsActive = false;
        await store.Db.SaveChangesAsync();

        var administration = new StaffAdministrationService(store.Db, hasher);
        using (StaffRequestContextScope.Push(secondContext!))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                administration.ListStaffAsync());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                administration.CreateStaffAsync(
                    "blocked-grant", "Blocked Grant", "Blocked-Grant-Passphrase-2026!",
                    "Cashier", [store.SecondBranchId]));
        }

        using (StaffRequestContextScope.Push(mainContext!))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                administration.ListStaffAsync());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                administration.CreateStaffAsync(
                    "blocked-branch", "Blocked Branch", "Blocked-Branch-Passphrase-2026!",
                    "Cashier", [store.MainBranchId]));
        }

        Assert.Empty(await store.Db.StaffUsers.AsNoTracking()
            .Where(x => x.UserNameNormalized == "BLOCKED-GRANT"
                || x.UserNameNormalized == "BLOCKED-BRANCH")
            .ToListAsync());
    }

    [Fact]
    public async Task StaleContext_CannotReadOrMutateAfterBusinessDeactivation()
    {
        await using var store = await TestStore.CreateAsync();
        var hasher = new StaffPasswordHasher();
        var authentication = new StaffAuthenticationService(store.Db, hasher);
        await authentication.CreateInitialOwnerAsync(
            store.BusinessId, [store.MainBranchId], "owner", "Store Owner", OwnerPassword);
        var login = await authentication.AuthenticateAsync(
            store.BusinessId, "owner", OwnerPassword);
        Assert.NotNull(login);

        var resolver = new StaffRequestContextResolver(store.Db, authentication);
        var staleContext = await resolver.ResolveAsync(login.Token);
        Assert.NotNull(staleContext);

        var business = await store.Db.Businesses.SingleAsync(x => x.Id == store.BusinessId);
        business.IsActive = false;
        await store.Db.SaveChangesAsync();

        var administration = new StaffAdministrationService(store.Db, hasher);
        using (StaffRequestContextScope.Push(staleContext!))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                administration.ListStaffAsync());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                administration.CreateStaffAsync(
                    "blocked-business", "Blocked Business", "Blocked-Business-Passphrase-2026!",
                    "Cashier", [store.MainBranchId]));
        }

        Assert.Empty(await store.Db.StaffUsers.AsNoTracking()
            .Where(x => x.UserNameNormalized == "BLOCKED-BUSINESS").ToListAsync());
    }

    [Fact]
    public async Task ChangeOwnPassword_RequiresCurrentCredentialAndRevokesAllSessions()
    {
        await using var store = await TestStore.CreateAsync();
        var hasher = new StaffPasswordHasher();
        var authentication = new StaffAuthenticationService(store.Db, hasher);
        var ownerId = await authentication.CreateInitialOwnerAsync(
            store.BusinessId, [store.MainBranchId], "owner", "Store Owner", OwnerPassword);
        var first = await authentication.AuthenticateAsync(store.BusinessId, "owner", OwnerPassword);
        var second = await authentication.AuthenticateAsync(store.BusinessId, "owner", OwnerPassword);
        Assert.NotNull(first);
        Assert.NotNull(second);

        var resolver = new StaffRequestContextResolver(store.Db, authentication);
        var context = await resolver.ResolveAsync(first.Token);
        Assert.NotNull(context);
        var administration = new StaffAdministrationService(store.Db, hasher);

        using (StaffRequestContextScope.Push(context!))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                administration.ChangeOwnPasswordAsync("wrong-current-password", "New-Owner-Passphrase-2026!"));

            await administration.ChangeOwnPasswordAsync(
                OwnerPassword, "New-Owner-Passphrase-2026!");
        }

        Assert.Null(await authentication.ValidateSessionAsync(store.BusinessId, first.Token));
        Assert.Null(await authentication.ValidateSessionAsync(store.BusinessId, second.Token));
        var relogin = await authentication.AuthenticateAsync(
            store.BusinessId, "owner", "New-Owner-Passphrase-2026!");
        Assert.NotNull(relogin);
        Assert.Equal(ownerId, relogin.Identity.StaffUserId);
        Assert.Null(await authentication.AuthenticateAsync(store.BusinessId, "owner", OwnerPassword));
    }

    private sealed class TestStore(
        SqliteConnection connection,
        BusinessDbContext db,
        Guid businessId,
        Guid mainBranchId,
        Guid secondBranchId) : IAsyncDisposable
    {
        public BusinessDbContext Db { get; } = db;
        public Guid BusinessId { get; } = businessId;
        public Guid MainBranchId { get; } = mainBranchId;
        public Guid SecondBranchId { get; } = secondBranchId;

        public static async Task<TestStore> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
            var db = new BusinessDbContext(options);
            await db.Database.MigrateAsync();

            var businessId = Guid.NewGuid();
            var mainBranchId = Guid.NewGuid();
            var secondBranchId = Guid.NewGuid();
            db.Businesses.Add(new BusinessRecord
            {
                Id = businessId,
                LegalName = "Test business",
                DisplayName = "Test business",
                DefaultCurrencyCode = "IRR",
                TimeZoneId = "UTC",
                IsActive = true
            });
            db.Branches.AddRange(
                new BranchRecord
                {
                    Id = mainBranchId,
                    BusinessId = businessId,
                    Code = "MAIN",
                    Name = "Main branch",
                    IsActive = true
                },
                new BranchRecord
                {
                    Id = secondBranchId,
                    BusinessId = businessId,
                    Code = "SECOND",
                    Name = "Second branch",
                    IsActive = true
                });
            db.StoreSettings.Add(new StoreSettings
            {
                Id = 1,
                BusinessId = businessId,
                DefaultBranchId = mainBranchId,
                StoreName = "Test business",
                CurrencyCode = "IRR",
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            return new TestStore(connection, db, businessId, mainBranchId, secondBranchId);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
