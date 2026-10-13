using MG.BusinessPlatform.Domain.Finance;

namespace MG.BusinessPlatform.Domain.Tests.Finance;

public sealed class TrialBalanceCalculatorTests
{
    [Fact]
    public void Calculate_ReturnsBalancedTurnoverAndBalances()
    {
        var cash = new Account("1000", "Cash", AccountType.Asset);
        var revenue = new Account("4000", "Sales", AccountType.Revenue);
        var entries = new[]
        {
            Entry(new DateOnly(2026, 1, 1), "Sale", new JournalLine("1000", "Cash", EntrySide.Debit, new Money(100m, "IRR")),
                new JournalLine("4000", "Sales", EntrySide.Credit, new Money(100m, "IRR"))),
            Entry(new DateOnly(2026, 1, 2), "Second sale", new JournalLine("1000", "Cash", EntrySide.Debit, new Money(50m, "IRR")),
                new JournalLine("4000", "Sales", EntrySide.Credit, new Money(50m, "IRR")))
        };

        var report = TrialBalanceCalculator.Calculate([cash, revenue], entries, new DateOnly(2026, 1, 2), "irr");

        Assert.True(report.IsBalanced);
        Assert.Equal(new Money(150m, "IRR"), report.TotalDebitTurnover);
        Assert.Equal(new Money(150m, "IRR"), report.TotalCreditTurnover);
        Assert.Equal(new Money(150m, "IRR"), report.TotalDebitBalance);
        Assert.Equal(new Money(150m, "IRR"), report.TotalCreditBalance);
    }

    [Fact]
    public void Calculate_ExcludesEntriesAfterAsOfDateAndOtherCurrencies()
    {
        var cash = new Account("1000", "Cash", AccountType.Asset);
        var revenue = new Account("4000", "Sales", AccountType.Revenue);
        var entries = new[]
        {
            Entry(new DateOnly(2026, 1, 1), "IRR", new JournalLine("1000", "Cash", EntrySide.Debit, new Money(10m, "IRR")),
                new JournalLine("4000", "Sales", EntrySide.Credit, new Money(10m, "IRR"))),
            Entry(new DateOnly(2026, 1, 3), "Future", new JournalLine("1000", "Cash", EntrySide.Debit, new Money(20m, "IRR")),
                new JournalLine("4000", "Sales", EntrySide.Credit, new Money(20m, "IRR"))),
            Entry(new DateOnly(2026, 1, 1), "USD", new JournalLine("1000", "Cash", EntrySide.Debit, new Money(30m, "USD")),
                new JournalLine("4000", "Sales", EntrySide.Credit, new Money(30m, "USD")))
        };

        var report = TrialBalanceCalculator.Calculate([cash, revenue], entries, new DateOnly(2026, 1, 2), "IRR");

        Assert.Equal(new Money(10m, "IRR"), report.TotalDebitTurnover);
        Assert.Equal(new Money(10m, "IRR"), report.TotalCreditTurnover);
    }

    [Fact]
    public void Calculate_RejectsUnknownAndNonPostingAccounts()
    {
        var entry = Entry(new DateOnly(2026, 1, 1), "Bad", new JournalLine("9999", "Unknown", EntrySide.Debit, new Money(1m, "IRR")),
            new JournalLine("4000", "Sales", EntrySide.Credit, new Money(1m, "IRR")));
        Assert.Throws<InvalidOperationException>(() => TrialBalanceCalculator.Calculate(
            [new Account("4000", "Sales", AccountType.Revenue)], [entry], new DateOnly(2026, 1, 1), "IRR"));

        var nonPosting = new Account("1000", "Assets", AccountType.Asset, isPostingAllowed: false);
        var parentEntry = Entry(new DateOnly(2026, 1, 1), "Parent", new JournalLine("1000", "Assets", EntrySide.Debit, new Money(1m, "IRR")),
            new JournalLine("4000", "Sales", EntrySide.Credit, new Money(1m, "IRR")));
        Assert.Throws<InvalidOperationException>(() => TrialBalanceCalculator.Calculate(
            [nonPosting, new Account("4000", "Sales", AccountType.Revenue)], [parentEntry], new DateOnly(2026, 1, 1), "IRR"));
    }

    private static JournalEntry Entry(DateOnly date, string description, params JournalLine[] lines) =>
        new(Guid.NewGuid(), date, description, lines);
}
