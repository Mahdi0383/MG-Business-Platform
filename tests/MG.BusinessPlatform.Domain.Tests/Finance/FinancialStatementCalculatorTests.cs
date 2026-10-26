using MG.BusinessPlatform.Domain.Finance;

namespace MG.BusinessPlatform.Domain.Tests.Finance;

public sealed class FinancialStatementCalculatorTests
{
    [Fact]
    public void ProfitAndLoss_CalculatesRevenueExpensesAndNetProfitForDateRange()
    {
        var accounts = Accounts();
        var entries = new[]
        {
            Entry(new DateOnly(2026, 1, 1), "Capital", Debit("1000", 1000m), Credit("3000", 1000m)),
            Entry(new DateOnly(2026, 2, 1), "Sale", Debit("1000", 200m), Credit("4000", 200m)),
            Entry(new DateOnly(2026, 2, 2), "Expense", Debit("6000", 50m), Credit("1000", 50m)),
            Entry(new DateOnly(2026, 3, 1), "Later sale", Debit("1000", 500m), Credit("4000", 500m))
        };

        var report = FinancialStatementCalculator.CalculateProfitAndLoss(
            accounts, entries, new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28), "IRR");

        Assert.Equal(new Money(200m, "IRR"), report.TotalRevenue);
        Assert.Equal(new Money(50m, "IRR"), report.TotalExpenses);
        Assert.Equal(new Money(150m, "IRR"), report.NetProfit);
    }

    [Fact]
    public void BalanceSheet_IncludesAccumulatedEarningsAndBalances()
    {
        var entries = new[]
        {
            Entry(new DateOnly(2026, 1, 1), "Capital", Debit("1000", 1000m), Credit("3000", 1000m)),
            Entry(new DateOnly(2026, 2, 1), "Sale", Debit("1000", 200m), Credit("4000", 200m)),
            Entry(new DateOnly(2026, 2, 2), "Expense", Debit("6000", 50m), Credit("1000", 50m))
        };

        var report = FinancialStatementCalculator.CalculateBalanceSheet(
            Accounts(), entries, new DateOnly(2026, 2, 28), "IRR");

        Assert.True(report.IsBalanced);
        Assert.Equal(new Money(1150m, "IRR"), report.TotalAssets);
        Assert.Equal(new Money(1000m, "IRR"), report.TotalEquity);
        Assert.Equal(new Money(150m, "IRR"), report.AccumulatedEarnings);
        Assert.Equal(new Money(1150m, "IRR"), report.TotalLiabilitiesAndEquity);
    }

    [Fact]
    public void FinancialStatements_IncludeHistoricalPostingsForAccountsNowMarkedNonPosting()
    {
        var accounts = Accounts()
            .Select(account => account.Code == "1000"
                ? new Account(account.Code, account.Name, account.Type, isPostingAllowed: false)
                : account)
            .ToArray();
        var entries = new[]
        {
            Entry(new DateOnly(2026, 1, 1), "Capital", Debit("1000", 1000m), Credit("3000", 1000m)),
            Entry(new DateOnly(2026, 2, 1), "Sale", Debit("1000", 200m), Credit("4000", 200m))
        };

        var profitAndLoss = FinancialStatementCalculator.CalculateProfitAndLoss(
            accounts, entries, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "IRR");
        var balanceSheet = FinancialStatementCalculator.CalculateBalanceSheet(
            accounts, entries, new DateOnly(2026, 12, 31), "IRR");

        Assert.Equal(new Money(200m, "IRR"), profitAndLoss.TotalRevenue);
        Assert.Equal(new Money(1200m, "IRR"), balanceSheet.TotalAssets);
        Assert.True(balanceSheet.IsBalanced);
    }

    [Fact]
    public void ProfitAndLoss_RejectsInvertedDateRange()
    {
        Assert.Throws<ArgumentException>(() => FinancialStatementCalculator.CalculateProfitAndLoss(
            Accounts(), [], new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 1), "IRR"));
    }

    private static Account[] Accounts() =>
    [
        new("1000", "Cash", AccountType.Asset),
        new("2000", "Payables", AccountType.Liability),
        new("3000", "Equity", AccountType.Equity),
        new("4000", "Revenue", AccountType.Revenue),
        new("6000", "Expense", AccountType.Expense)
    ];

    private static JournalLine Debit(string code, decimal amount) =>
        new(code, code, EntrySide.Debit, new Money(amount, "IRR"));

    private static JournalLine Credit(string code, decimal amount) =>
        new(code, code, EntrySide.Credit, new Money(amount, "IRR"));

    private static JournalEntry Entry(DateOnly date, string description, params JournalLine[] lines) =>
        new(Guid.NewGuid(), date, description, lines);
}
