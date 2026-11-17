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
        var businessId = await db.StoreSettings.AsNoTracking()
            .Where(settings => settings.Id == 1)
            .Select(settings => settings.BusinessId)
            .SingleAsync(cancellationToken)
            ?? throw new InvalidDataException("The local business identity was not persisted.");
        await BackfillBusinessOwnedRecordsAsync(db, businessId, cancellationToken);
        await BackfillStockMovementOwnershipAsync(db, businessId, cancellationToken);
        await BackfillBranchInventoryBalancesAsync(db, businessId, cancellationToken);

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

    private static async Task BackfillBusinessOwnedRecordsAsync(
        BusinessDbContext db,
        Guid businessId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        if (await db.StoreProducts.AnyAsync(
                product => product.BusinessId != null && product.BusinessId != businessId, cancellationToken)
            || await db.LedgerAccounts.AnyAsync(
                account => account.BusinessId != null && account.BusinessId != businessId, cancellationToken)
            || await db.Customers.AnyAsync(
                customer => customer.BusinessId != null && customer.BusinessId != businessId, cancellationToken)
            || await db.Suppliers.AnyAsync(
                supplier => supplier.BusinessId != null && supplier.BusinessId != businessId, cancellationToken))
        {
            throw new InvalidDataException(
                "Catalog or ledger records are already assigned to a different business. The legacy ownership backfill was stopped.");
        }

        await db.StoreProducts
            .Where(product => product.BusinessId == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(product => product.BusinessId, (Guid?)businessId),
                cancellationToken);
        await db.LedgerAccounts
            .Where(account => account.BusinessId == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(account => account.BusinessId, (Guid?)businessId),
                cancellationToken);
        await db.Customers
            .Where(customer => customer.BusinessId == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(customer => customer.BusinessId, (Guid?)businessId),
                cancellationToken);
        await db.Suppliers
            .Where(supplier => supplier.BusinessId == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(supplier => supplier.BusinessId, (Guid?)businessId),
                cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task BackfillStockMovementOwnershipAsync(
        BusinessDbContext db,
        Guid businessId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var settings = await db.StoreSettings.AsNoTracking()
            .Where(x => x.Id == 1)
            .Select(x => new { x.BusinessId, x.DefaultBranchId })
            .SingleAsync(cancellationToken);

        if (settings.BusinessId != businessId
            || settings.DefaultBranchId is not Guid defaultBranchId
            || defaultBranchId == Guid.Empty)
        {
            throw new InvalidDataException(
                "Stock movement ownership cannot be backfilled without a valid configured business and default branch.");
        }

        var defaultBranch = await db.Branches.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == defaultBranchId && x.BusinessId == businessId && x.IsActive,
                cancellationToken);
        if (defaultBranch is null)
            throw new InvalidDataException(
                "The configured default branch is missing, inactive, or owned by another business. Stock movement backfill was stopped.");

        var foreignMovementOwnership = await db.StockMovements.AsNoTracking()
            .AnyAsync(x => (x.BusinessId != null && x.BusinessId != businessId)
                || (x.BranchId != null && !db.Branches.Any(branch =>
                    branch.Id == x.BranchId && branch.BusinessId == businessId)),
                cancellationToken);
        if (foreignMovementOwnership)
            throw new InvalidDataException(
                "Stock movement history contains business or branch ownership that conflicts with this installation. Backfill was stopped.");

        var unassignedMovementExists = await db.StockMovements.AsNoTracking()
            .AnyAsync(x => x.BusinessId == null || x.BranchId == null, cancellationToken);
        if (unassignedMovementExists)
        {
            var branchCount = await db.Branches.CountAsync(
                x => x.BusinessId == businessId, cancellationToken);
            if (branchCount != 1 || defaultBranchId != await db.Branches
                    .Where(x => x.BusinessId == businessId)
                    .Select(x => x.Id)
                    .SingleAsync(cancellationToken))
            {
                throw new InvalidDataException(
                    "Legacy stock movements have no branch history and this business has multiple branches. An explicit stock-to-branch migration mapping is required; no stock history was reassigned.");
            }
        }

        var orphanedOrForeignProducts = await db.StockMovements.AsNoTracking()
            .AnyAsync(movement => !db.StoreProducts.Any(product =>
                product.Id == movement.ProductId && product.BusinessId == businessId),
                cancellationToken);
        if (orphanedOrForeignProducts)
            throw new InvalidDataException(
                "Stock movement history references a missing product or a product owned by another business. Backfill was stopped.");

        await db.StockMovements
            .Where(x => x.BusinessId == null || x.BranchId == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.BusinessId, (Guid?)businessId)
                    .SetProperty(x => x.BranchId, (Guid?)defaultBranchId),
                cancellationToken);

        var remainingUnassigned = await db.StockMovements.AsNoTracking()
            .AnyAsync(x => x.BusinessId != businessId || x.BranchId == null, cancellationToken);
        if (remainingUnassigned)
            throw new InvalidDataException(
                "Stock movement ownership backfill did not produce complete business and branch attribution.");

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task BackfillBranchInventoryBalancesAsync(
        BusinessDbContext db,
        Guid businessId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var inconsistentBalances = await db.BranchInventoryBalances.AsNoTracking()
            .AnyAsync(balance => balance.BusinessId != businessId
                || !db.Branches.Any(branch => branch.Id == balance.BranchId
                    && branch.BusinessId == businessId)
                || !db.StoreProducts.Any(product => product.Id == balance.ProductId
                    && product.BusinessId == businessId),
                cancellationToken);
        if (inconsistentBalances)
            throw new InvalidDataException(
                "Existing branch inventory balances contain cross-business ownership. Inventory initialization was stopped.");

        var productsWithExistingBalances = await db.BranchInventoryBalances.AsNoTracking()
            .Where(x => x.BusinessId == businessId)
            .Select(x => x.ProductId)
            .Distinct()
            .ToHashSetAsync(cancellationToken);

        var settings = await db.StoreSettings.AsNoTracking()
            .Where(x => x.Id == 1)
            .Select(x => new { x.BusinessId, x.DefaultBranchId })
            .SingleAsync(cancellationToken);
        if (settings.BusinessId != businessId
            || settings.DefaultBranchId is not Guid defaultBranchId
            || defaultBranchId == Guid.Empty)
        {
            throw new InvalidDataException(
                "Branch inventory initialization requires a configured business and default branch.");
        }

        var branches = await db.Branches.AsNoTracking()
            .Where(x => x.BusinessId == businessId)
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken);
        if (branches.Length == 0 || !branches.Contains(defaultBranchId))
            throw new InvalidDataException(
                "Branch inventory initialization could not resolve the configured default branch.");

        var products = await db.StoreProducts.AsNoTracking()
            .Where(x => x.BusinessId == businessId)
            .Select(x => new { x.Id, x.StockQuantity })
            .ToArrayAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var balances = new List<BranchInventoryBalanceRecord>();

        if (branches.Length == 1)
        {
            foreach (var product in products)
            {
                if (productsWithExistingBalances.Contains(product.Id))
                    continue;
                if (product.StockQuantity < 0)
                    throw new InvalidDataException("A product has a negative legacy stock quantity.");

                balances.Add(new BranchInventoryBalanceRecord
                {
                    BusinessId = businessId,
                    BranchId = defaultBranchId,
                    ProductId = product.Id,
                    OnHandQuantity = product.StockQuantity,
                    UpdatedAtUtc = now
                });
            }
        }
        else
        {
            var movementTotals = await db.StockMovements.AsNoTracking()
                .Where(x => x.BusinessId == businessId && x.BranchId != null)
                .GroupBy(x => new { x.ProductId, BranchId = x.BranchId!.Value })
                .Select(group => new
                {
                    group.Key.ProductId,
                    group.Key.BranchId,
                    Quantity = group.Sum(x => x.SignedQuantity)
                })
                .ToArrayAsync(cancellationToken);

            foreach (var product in products)
            {
                if (productsWithExistingBalances.Contains(product.Id))
                    continue;

                var productMovements = movementTotals
                    .Where(x => x.ProductId == product.Id)
                    .ToArray();

                if (productMovements.Any(x => x.Quantity < 0)
                    || productMovements.Sum(x => x.Quantity) != product.StockQuantity)
                {
                    throw new InvalidDataException(
                        $"Legacy stock for product {product.Id} cannot be mapped safely to {branches.Length} branches. " +
                        "The branch movement totals must be non-negative and exactly equal the legacy product stock quantity. " +
                        "Provide an explicit stock-to-branch opening balance mapping before continuing.");
                }

                if (productMovements.Length == 0 && product.StockQuantity == 0)
                {
                    balances.Add(new BranchInventoryBalanceRecord
                    {
                        BusinessId = businessId,
                        BranchId = defaultBranchId,
                        ProductId = product.Id,
                        OnHandQuantity = 0,
                        UpdatedAtUtc = now
                    });
                }

                foreach (var movement in productMovements.Where(x => x.Quantity > 0))
                {
                    if (!branches.Contains(movement.BranchId))
                        throw new InvalidDataException(
                            "A stock movement references a branch outside the configured business.");

                    balances.Add(new BranchInventoryBalanceRecord
                    {
                        BusinessId = businessId,
                        BranchId = movement.BranchId,
                        ProductId = product.Id,
                        OnHandQuantity = movement.Quantity,
                        UpdatedAtUtc = now
                    });
                }
            }
        }

        if (balances.Count > 0)
        {
            db.BranchInventoryBalances.AddRange(balances);
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
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
        Guid defaultId;
        if (settings.DefaultBranchId is Guid configuredDefaultBranchId)
        {
            defaultId = configuredDefaultBranchId;
        }
        else if (legacyBranchIds.Count > 1)
        {
            if (requestedDefaultBranchId is Guid requestedLegacyBranchId
                && legacyBranchIds.Contains(requestedLegacyBranchId))
            {
                defaultId = requestedLegacyBranchId;
            }
            else
            {
                throw new InvalidDataException(
                    "Multiple legacy branches exist and the configured desktop branch identity does not identify one of them. An explicit branch mapping is required.");
            }
        }
        else if (legacyBranchIds.Count == 1)
        {
            defaultId = legacyBranchIds.Single();
        }
        else if (requestedDefaultBranchId is Guid requestedBranchId)
        {
            defaultId = requestedBranchId;
        }
        else if (existingBusinessBranchIds.Length == 1)
        {
            defaultId = existingBusinessBranchIds[0];
        }
        else if (existingBusinessBranchIds.Length > 1)
        {
            throw new InvalidDataException(
                "Multiple branches exist but no default branch is configured. An explicit default branch selection is required.");
        }
        else
        {
            defaultId = Guid.NewGuid();
        }

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
