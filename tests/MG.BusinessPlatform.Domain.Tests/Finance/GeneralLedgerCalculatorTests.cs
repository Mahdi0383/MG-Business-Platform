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

    [Fact]
    public void Calculate_UsesCreditNormalBalanceForLiability()
    {
        var accounts = new[]
        {
            new Account("1000", "Cash", AccountType.Asset),
            new Account("2000", "Payables", AccountType.Liability)
        };
        var entries = new[]
        {
            Entry(new DateOnly(2026, 1, 1), "Opening payable", Debit("1000", 500m), Credit("2000", 500m)),
            Entry(new DateOnly(2026, 2, 1), "Payment", Debit("2000", 100m), Credit("1000", 100m))
        };

        var report = GeneralLedgerCalculator.Calculate(accounts, entries, "2000",
            new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28), "IRR");

        Assert.Equal(new Money(500m, "IRR"), report.OpeningBalance);
        Assert.Equal(new Money(100m, "IRR"), report.PeriodDebits);
        Assert.Equal(new Money(400m, "IRR"), report.ClosingBalance);
    }

    [Fact]
    public void Calculate_RejectsInvalidDateRangeAndUnknownAccount()
    {
        var accounts = new[] { new Account("1000", "Cash", AccountType.Asset) };
        Assert.Throws<ArgumentException>(() => GeneralLedgerCalculator.Calculate(accounts, [],
            "1000", new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 1), "IRR"));
        Assert.Throws<KeyNotFoundException>(() => GeneralLedgerCalculator.Calculate(accounts, [],
            "9999", new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28), "IRR"));
    }

    private static JournalLine Debit(string code, decimal amount) =>
        new(code, code, EntrySide.Debit, new Money(amount, "IRR"));

    private static JournalLine Credit(string code, decimal amount) =>
        new(code, code, EntrySide.Credit, new Money(amount, "IRR"));

    private static JournalEntry Entry(DateOnly date, string description, params JournalLine[] lines) =>
        new(Guid.NewGuid(), date, description, lines);
}
