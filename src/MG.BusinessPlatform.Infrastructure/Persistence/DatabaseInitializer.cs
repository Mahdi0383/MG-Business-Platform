using Microsoft.EntityFrameworkCore;

namespace MG.BusinessPlatform.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    private static readonly (string Code, string Name, string Type)[] DefaultAccounts =
    [
        ("1000", "Cash", "Asset"),
        ("1010", "Card Clearing", "Asset"),
        ("1020", "Bank", "Asset"),
        ("1100", "Accounts Receivable", "Asset"),
        ("1200", "Inventory", "Asset"),
        ("2000", "Accounts Payable", "Liability"),
        ("3000", "Owner Equity", "Equity"),
        ("4000", "Sales Revenue", "Revenue"),
        ("5000", "Cost of Goods Sold", "Expense"),
        ("6000", "Operating Expenses", "Expense")
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

        if (!await db.LedgerAccounts.AnyAsync(cancellationToken))
        {
            db.LedgerAccounts.AddRange(DefaultAccounts.Select(account => new LedgerAccountRecord
            {
                Id = Guid.NewGuid(),
                Code = account.Code,
                Name = account.Name,
                AccountType = account.Type,
                IsPostingAllowed = true,
                IsActive = true
            }));
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
