using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

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

    public static async Task InitializeAsync(BusinessDbContext db, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        await BackupBeforePendingMigrationsAsync(db, cancellationToken);
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
        }

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
    private static async Task BackupBeforePendingMigrationsAsync(
        BusinessDbContext db,
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
        var backupPath = $"${databasePath}.pre-migration-${timestamp}-${Guid.NewGuid():N}.bak";
        await new DatabaseMaintenanceService().CreateBackupAsync(databasePath, backupPath, cancellationToken);
    }

}
