using MG.BusinessPlatform.Domain.Finance;

namespace MG.BusinessPlatform.Domain.Tests.Finance;

public sealed class GeneralLedgerCalculatorTests
{
    [Fact]
    public void Calculate_IncludesOpeningBalanceAndRunningBalance()
    {
        var accounts = new[]
        {
            new Account("1000", "Cash", AccountType.Asset),
            new Account("3000", "Equity", AccountType.Equity),
            new Account("4000", "Revenue", AccountType.Revenue)
        };
        var entries = new[]
        {
            Entry(new DateOnly(2026, 1, 1), "Opening", Debit("1000", 1000m), Credit("3000", 1000m)),
            Entry(new DateOnly(2026, 2, 1), "Sale", Debit("1000", 200m), Credit("4000", 200m)),
            Entry(new DateOnly(2026, 2, 2), "Refund", Credit("1000", 50m), Debit("4000", 50m))
        };

        var report = GeneralLedgerCalculator.Calculate(accounts, entries, "1000",
            new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28), "IRR");

        Assert.Equal(new Money(1000m, "IRR"), report.OpeningBalance);
        Assert.Equal(2, report.Lines.Count);
        Assert.Equal(new Money(200m, "IRR"), report.PeriodDebits);
        Assert.Equal(new Money(50m, "IRR"), report.PeriodCredits);
        Assert.Equal(new Money(1150m, "IRR"), report.ClosingBalance);
    }

    private static JournalLine Debit(string code, decimal amount) =>
        new(code, code, EntrySide.Debit, new Money(amount, "IRR"));

    private static JournalLine Credit(string code, decimal amount) =>
        new(code, code, EntrySide.Credit, new Money(amount, "IRR"));

    private static JournalEntry Entry(DateOnly date, string description, params JournalLine[] lines) =>
        new(Guid.NewGuid(), date, description, lines);
}
