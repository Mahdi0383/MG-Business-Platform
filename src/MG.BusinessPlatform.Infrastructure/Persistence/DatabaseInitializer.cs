using Microsoft.EntityFrameworkCore;

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
}
