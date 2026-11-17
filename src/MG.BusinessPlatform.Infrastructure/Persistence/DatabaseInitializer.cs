using System.Globalization;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using MG.BusinessPlatform.Domain.Organizations;

namespace MG.BusinessPlatform.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    private static readonly (string Code, string Name, string Type)[] DefaultAccounts =
    [
        ("1000", "صندوق", "Asset"),
        ("1010", "وجوه در راه کارت‌خوان", "Asset"),
        ("1020", "بانک", "Asset"),
        ("1100", "حساب‌های دریافتنی", "Asset"),
        ("1200", "موجودی کالا", "Asset"),
        ("2000", "حساب‌های پرداختنی", "Liability"),
        ("3000", "سرمایه مالک", "Equity"),
        ("4000", "درآمد فروش", "Revenue"),
        ("5000", "بهای تمام‌شده کالای فروش‌رفته", "Expense"),
        ("6000", "هزینه‌های عملیاتی", "Expense")
    ];

    public static async Task InitializeAsync(
        BusinessDbContext db,
        CancellationToken cancellationToken = default,
        Guid? defaultBranchId = null)
    {
        ArgumentNullException.ThrowIfNull(db);

        // Serialize backup, migrations and seeding across desktop/storefront processes
        // that point at the same file-backed SQLite database.
        await using var initializationLock = await AcquireInitializationLockAsync(db, cancellationToken);

        await BackupBeforePendingMigrationsAsync(db, initializationLock, cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);

        if (!await db.StoreSettings.AnyAsync(cancellationToken))
        {
            db.StoreSettings.Add(new StoreSettings
            {
                Id = 1,
                StoreName = "فروشگاه شما",
                CurrencyCode = "IRR",
                OnlineOrderingEnabled = false,
                PublicPublicationEnabled = false,
                PublicHostname = string.Empty,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        await EnsureSingleBusinessOrganizationAsync(db, defaultBranchId, cancellationToken);

        // Seed by account code rather than table emptiness. A partially populated chart
        // must still receive every required system account without overwriting custom data.
        var existingCodes = await db.LedgerAccounts
            .Select(account => account.Code)
            .ToHashSetAsync(cancellationToken);
        var missingAccounts = DefaultAccounts
            .Where(account => !existingCodes.Contains(account.Code))
            .Select(account => new LedgerAccountRecord
            {
                Id = Guid.NewGuid(),
                Code = account.Code,
                Name = account.Name,
                AccountType = account.Type,
                IsPostingAllowed = true,
                IsActive = true
            })
            .ToArray();

        if (missingAccounts.Length > 0)
            db.LedgerAccounts.AddRange(missingAccounts);

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureSingleBusinessOrganizationAsync(
        BusinessDbContext db,
        Guid? requestedDefaultBranchId,
        CancellationToken cancellationToken)
    {
        if (requestedDefaultBranchId == Guid.Empty)
            throw new ArgumentException("The requested default branch identity cannot be empty.", nameof(requestedDefaultBranchId));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var settings = await db.StoreSettings.SingleAsync(x => x.Id == 1, cancellationToken);

        var storeName = string.IsNullOrWhiteSpace(settings.StoreName)
            ? "فروشگاه شما"
            : settings.StoreName.Trim();

        BusinessRecord business;
        if (settings.BusinessId is Guid existingBusinessId)
        {
            business = await db.Businesses.SingleOrDefaultAsync(
                x => x.Id == existingBusinessId, cancellationToken)
                ?? throw new InvalidDataException(
                    "Store settings reference a business that does not exist. Organization bootstrap was stopped without changing ownership.");
        }
        else
        {
            if (await db.Businesses.AnyAsync(cancellationToken)
                || await db.Branches.AnyAsync(cancellationToken))
            {
                throw new InvalidDataException(
                    "Organization records exist but the legacy store settings have no business owner. An explicit ownership mapping is required.");
            }

            if (string.IsNullOrWhiteSpace(settings.CurrencyCode))
                throw new InvalidDataException("The store currency is missing and must be corrected before organization bootstrap.");

            var currency = settings.CurrencyCode.Trim().ToUpperInvariant();
            if (currency.Length != 3 || currency.Any(character => character is < 'A' or > 'Z'))
                throw new InvalidDataException("The store currency must be a valid three-letter ISO-style code before organization bootstrap.");

            var organization = new Business(
                Guid.NewGuid(), storeName, storeName, currency, TimeZoneInfo.Local.Id);
            business = new BusinessRecord
            {
                Id = organization.Id,
                LegalName = organization.LegalName,
                DisplayName = organization.DisplayName,
                DefaultCurrencyCode = organization.DefaultCurrencyCode,
                TimeZoneId = organization.TimeZoneId,
                IsActive = organization.IsActive
            };
            db.Businesses.Add(business);
            settings.BusinessId = business.Id;
            await db.SaveChangesAsync(cancellationToken);
        }

        if (await db.Businesses.CountAsync(cancellationToken) != 1)
            throw new InvalidDataException(
                "This installation is provisioned for one business per database. Shared-database multi-business mode is not enabled.");

        var legacyBranchIds = new HashSet<Guid>();
        legacyBranchIds.UnionWith(await db.GameSessions.Select(x => x.BranchId).Distinct().ToListAsync(cancellationToken));
        legacyBranchIds.UnionWith(await db.ReservationResources.Select(x => x.BranchId).Distinct().ToListAsync(cancellationToken));
        legacyBranchIds.UnionWith(await db.PurchaseInvoices.Select(x => x.BranchId).Distinct().ToListAsync(cancellationToken));

        if (legacyBranchIds.Contains(Guid.Empty))
            throw new InvalidDataException("A legacy branch-scoped record contains an empty BranchId. Organization bootstrap was stopped.");

        var existingBranches = await db.Branches.ToListAsync(cancellationToken);
        if (settings.DefaultBranchId is Guid storedDefaultBranchId)
        {
            if (!existingBranches.Any(x => x.Id == storedDefaultBranchId && x.BusinessId == business.Id))
                throw new InvalidDataException(
                    "Store settings reference a default branch that is missing or belongs to another business.");
        }

        if (settings.DefaultBranchId == Guid.Empty)
            throw new InvalidDataException("Store settings contain an empty default BranchId.");

        var existingBusinessBranchIds = existingBranches
            .Where(x => x.BusinessId == business.Id)
            .Select(x => x.Id)
            .OrderBy(x => x.ToString("N"), StringComparer.Ordinal)
            .ToArray();
        var matchingLegacyDefaultId = requestedDefaultBranchId is Guid requestedId
            && legacyBranchIds.Contains(requestedId)
                ? requestedId
                : (Guid?)null;
        var defaultId = settings.DefaultBranchId
            ?? (legacyBranchIds.Count > 0
                ? matchingLegacyDefaultId
                    ?? legacyBranchIds.OrderBy(x => x.ToString("N"), StringComparer.Ordinal).First()
                : requestedDefaultBranchId
                    ?? (existingBusinessBranchIds.Length == 1
                        ? existingBusinessBranchIds[0]
                        : existingBusinessBranchIds.Length > 1
                            ? throw new InvalidDataException(
                                "Multiple branches exist but no default branch is configured. An explicit default branch selection is required.")
                            : Guid.NewGuid()));

        legacyBranchIds.Add(defaultId);

        var foreignOwnedBranch = existingBranches.FirstOrDefault(
            branch => legacyBranchIds.Contains(branch.Id) && branch.BusinessId != business.Id);
        if (foreignOwnedBranch is not null)
            throw new InvalidDataException(
                $"Legacy BranchId {foreignOwnedBranch.Id} is already owned by a different business. Organization bootstrap was stopped.");

        if (!existingBranches.Any(x => x.Id == defaultId && x.BusinessId == business.Id)
            && existingBranches.Any(x => x.BusinessId == business.Id && x.Code == "MAIN"))
            throw new InvalidDataException(
                "A different branch already uses the MAIN code. Select that branch as the default or resolve the code conflict explicitly.");

        var businessBranches = existingBranches
            .Where(x => x.BusinessId == business.Id)
            .Select(x => x.Id)
            .ToHashSet();

        foreach (var branchId in legacyBranchIds.OrderBy(x => x.ToString("N"), StringComparer.Ordinal))
        {
            if (businessBranches.Contains(branchId))
                continue;

            var isDefault = branchId == defaultId;
            var branchName = isDefault
                ? (storeName.Length <= 160 ? storeName : storeName[..160])
                : $"شعبه قدیمی {branchId.ToString("N")[..8]}";
            var organizationBranch = new Branch(
                branchId, business.Id, isDefault ? "MAIN" : $"LEGACY-{branchId:N}", branchName);
            db.Branches.Add(new BranchRecord
            {
                Id = organizationBranch.Id,
                BusinessId = organizationBranch.BusinessId,
                Code = organizationBranch.Code,
                Name = organizationBranch.Name,
                Address = organizationBranch.Address,
                Phone = organizationBranch.Phone,
                IsActive = organizationBranch.IsActive
            });
        }

        settings.DefaultBranchId = defaultId;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<FileStream?> AcquireInitializationLockAsync(
        BusinessDbContext db,
        CancellationToken cancellationToken)
    {
        if (db.Database.GetDbConnection() is not SqliteConnection connection)
            return null;

        var dataSource = connection.DataSource;
        if (string.IsNullOrWhiteSpace(dataSource)
            || string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase)
            || dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            || new SqliteConnectionStringBuilder(connection.ConnectionString).Mode == SqliteOpenMode.Memory)
            return null;

        return await SqliteDatabaseFileLock.AcquireAsync(dataSource, cancellationToken);
    }

    private static async Task BackupBeforePendingMigrationsAsync(
        BusinessDbContext db,
        FileStream? initializationLock,
        CancellationToken cancellationToken)
    {
        if (db.Database.GetDbConnection() is not SqliteConnection connection)
            return;

        var dataSource = connection.DataSource;
        if (string.IsNullOrWhiteSpace(dataSource)
            || string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase)
            || dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return;

        var databasePath = Path.GetFullPath(dataSource);
        if (!File.Exists(databasePath) || new FileInfo(databasePath).Length == 0)
            return;

        var pendingMigrations = await db.Database.GetPendingMigrationsAsync(cancellationToken);
        if (!pendingMigrations.Any())
            return;

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture);
        var backupPath = $"{databasePath}.pre-migration-{timestamp}-{Guid.NewGuid():N}.bak";
        var heldLock = initializationLock
            ?? throw new InvalidOperationException("A file-backed database migration backup requires the initialization lock.");
        await new DatabaseMaintenanceService().CreateBackupUnderLockAsync(
            databasePath, backupPath, heldLock, cancellationToken);
    }
}
