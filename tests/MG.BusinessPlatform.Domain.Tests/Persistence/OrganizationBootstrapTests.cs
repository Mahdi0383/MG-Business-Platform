using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using MG.BusinessPlatform.Infrastructure.Persistence;

namespace MG.BusinessPlatform.Domain.Tests.Persistence;

public sealed class OrganizationBootstrapTests
{
    [Fact]
    public async Task InitializeAsync_PreservesEveryLegacyBranchIdAndUsesRequestedDefault()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await db.Database.MigrateAsync();

        var legacyGameBranchId = Guid.NewGuid();
        var legacyReservationBranchId = Guid.NewGuid();
        var requestedDefaultBranchId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        db.StoreSettings.Add(new StoreSettings
        {
            Id = 1,
            StoreName = "Legacy Cafe",
            CurrencyCode = "IRR",
            UpdatedAt = now
        });
        db.GameSessions.Add(new GameSessionRecord
        {
            Id = Guid.NewGuid(),
            BranchId = legacyGameBranchId,
            DeviceId = Guid.NewGuid(),
            GameName = "Legacy session",
            RatePerHour = 0,
            CurrencyCode = "IRR",
            StartedAtUtc = now,
            LastTransitionAtUtc = now,
            Status = "Completed"
        });
        db.ReservationResources.Add(new ReservationResourceRecord
        {
            Id = Guid.NewGuid(),
            BranchId = legacyReservationBranchId,
            Name = "Legacy resource",
            ResourceType = "GameStation",
            Capacity = 1,
            IsAvailable = true
        });
        await db.SaveChangesAsync();

        await DatabaseInitializer.InitializeAsync(db, defaultBranchId: requestedDefaultBranchId);

        var settings = await db.StoreSettings.AsNoTracking().SingleAsync();
        var business = await db.Businesses.AsNoTracking().SingleAsync();
        var branches = await db.Branches.AsNoTracking().ToDictionaryAsync(x => x.Id);

        Assert.Equal(business.Id, settings.BusinessId);
        Assert.Equal(requestedDefaultBranchId, settings.DefaultBranchId);
        Assert.Equal(3, branches.Count);
        Assert.Equal(business.Id, branches[legacyGameBranchId].BusinessId);
        Assert.Equal(business.Id, branches[legacyReservationBranchId].BusinessId);
        Assert.Equal(business.Id, branches[requestedDefaultBranchId].BusinessId);
        Assert.Equal("MAIN", branches[requestedDefaultBranchId].Code);
        Assert.StartsWith("LEGACY-", branches[legacyGameBranchId].Code, StringComparison.Ordinal);
        Assert.StartsWith("LEGACY-", branches[legacyReservationBranchId].Code, StringComparison.Ordinal);

        await DatabaseInitializer.InitializeAsync(db, defaultBranchId: Guid.NewGuid());

        Assert.Equal(1, await db.Businesses.CountAsync());
        Assert.Equal(3, await db.Branches.CountAsync());
        Assert.Equal(requestedDefaultBranchId,
            (await db.StoreSettings.AsNoTracking().SingleAsync()).DefaultBranchId);
    }

    [Fact]
    public async Task InitializeAsync_FailsClosedWhenSettingsReferenceMissingBusiness()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await db.Database.MigrateAsync();

        var missingBusinessId = Guid.NewGuid();
        db.StoreSettings.Add(new StoreSettings
        {
            Id = 1,
            StoreName = "Orphaned store",
            CurrencyCode = "IRR",
            BusinessId = missingBusinessId,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => DatabaseInitializer.InitializeAsync(db));
        Assert.Empty(await db.Businesses.ToListAsync());
        Assert.Empty(await db.Branches.ToListAsync());
    }

    [Fact]
    public async Task StoreSettings_CannotReferenceBranchOwnedByAnotherBusiness()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await db.Database.MigrateAsync();

        var firstBusiness = new BusinessRecord
        {
            Id = Guid.NewGuid(),
            LegalName = "First",
            DisplayName = "First",
            DefaultCurrencyCode = "IRR",
            TimeZoneId = "UTC",
            IsActive = true
        };
        var secondBusiness = new BusinessRecord
        {
            Id = Guid.NewGuid(),
            LegalName = "Second",
            DisplayName = "Second",
            DefaultCurrencyCode = "IRR",
            TimeZoneId = "UTC",
            IsActive = true
        };
        var foreignBranch = new BranchRecord
        {
            Id = Guid.NewGuid(),
            BusinessId = secondBusiness.Id,
            Code = "MAIN",
            Name = "Second main branch",
            IsActive = true
        };
        db.Businesses.AddRange(firstBusiness, secondBusiness);
        db.Branches.Add(foreignBranch);
        await db.SaveChangesAsync();

        db.StoreSettings.Add(new StoreSettings
        {
            Id = 1,
            BusinessId = firstBusiness.Id,
            DefaultBranchId = foreignBranch.Id,
            StoreName = "First store",
            CurrencyCode = "IRR",
            UpdatedAt = DateTimeOffset.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task InitializeAsync_RejectsEmptyLegacyBranchIdentity()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await db.Database.MigrateAsync();

        db.StoreSettings.Add(new StoreSettings
        {
            Id = 1,
            StoreName = "Invalid legacy store",
            CurrencyCode = "IRR",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.ReservationResources.Add(new ReservationResourceRecord
        {
            Id = Guid.NewGuid(),
            BranchId = Guid.Empty,
            Name = "Invalid branch resource",
            ResourceType = "GameStation",
            Capacity = 1,
            IsAvailable = true
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => DatabaseInitializer.InitializeAsync(db));
        Assert.Empty(await db.Businesses.ToListAsync());
    }
}
