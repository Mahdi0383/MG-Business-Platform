using MG.BusinessPlatform.Domain.Finance;

namespace MG.BusinessPlatform.Domain.Tests.Finance;

public sealed class CashMovementReportCalculatorTests
{
    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateOnly To = new(2026, 10, 31);

    [Fact]
    public void Calculate_SeparatesOpeningBalancesAndPeriodMovements()
    {
        var accounts = new[]
        {
            new Account("1000", "Cash", AccountType.Asset),
            new Account("1010", "Bank", AccountType.Asset),
            new Account("4000", "Sales", AccountType.Revenue)
        };
        var entries = new[]
        {
            Entry(new DateOnly(2026, 9, 30), "Opening cash", ("1000", EntrySide.Debit, 100m), ("4000", EntrySide.Credit, 100m)),
            Entry(new DateOnly(2026, 10, 5), "Cash receipt", ("1000", EntrySide.Debit, 40m), ("4000", EntrySide.Credit, 40m)),
            Entry(new DateOnly(2026, 10, 8), "Bank payment", ("4000", EntrySide.Debit, 25m), ("1010", EntrySide.Credit, 25m)),
            Entry(new DateOnly(2026, 11, 1), "Outside range", ("1000", EntrySide.Debit, 900m), ("4000", EntrySide.Credit, 900m))
        };

        var report = CashMovementReportCalculator.Calculate(accounts, entries, From, To, "IRR", ["1000", "1010"]);

        Assert.Equal(100m, report.OpeningBalance.Amount);
        Assert.Equal(40m, report.TotalInflows.Amount);
        Assert.Equal(25m, report.TotalOutflows.Amount);
        Assert.Equal(15m, report.NetMovement.Amount);
        Assert.Equal(115m, report.ClosingBalance.Amount);
        var cash = Assert.Single(report.Lines.Where(line => line.AccountCode == "1000"));
        Assert.Equal(100m, cash.OpeningBalance.Amount);
        Assert.Equal(40m, cash.Inflows.Amount);
        Assert.Equal(0m, cash.Outflows.Amount);
        Assert.Equal(140m, cash.ClosingBalance.Amount);
        var bank = Assert.Single(report.Lines.Where(line => line.AccountCode == "1010"));
        Assert.Equal(-25m, bank.ClosingBalance.Amount);
    }

    [Fact]
    public void Calculate_IgnoresEntriesInOtherCurrencies()
    {
        var accounts = new[] { new Account("1000", "Cash", AccountType.Asset) };
        var entries = new[]
        {
            Entry(new DateOnly(2026, 10, 5), "IRR", ("1000", EntrySide.Debit, 100m, "IRR"), ("4000", EntrySide.Credit, 100m, "IRR")),
            Entry(new DateOnly(2026, 10, 6), "USD", ("1000", EntrySide.Debit, 500m, "USD"), ("4000", EntrySide.Credit, 500m, "USD"))
        };

        var report = CashMovementReportCalculator.Calculate(accounts, entries, From, To, "IRR", ["1000"]);

        Assert.Equal(100m, report.TotalInflows.Amount);
        Assert.Equal(100m, report.ClosingBalance.Amount);
    }

    [Fact]
    public void Calculate_RejectsNonAssetOrUnknownCashAccounts()
    {
        var accounts = new[]
        {
            new Account("1000", "Cash", AccountType.Asset),
            new Account("4000", "Sales", AccountType.Revenue)
        };

        Assert.Throws<InvalidOperationException>(() =>
            CashMovementReportCalculator.Calculate(accounts, [], From, To, "IRR", ["9999"]));
        Assert.Throws<InvalidOperationException>(() =>
            CashMovementReportCalculator.Calculate(accounts, [], From, To, "IRR", ["4000"]));
    }

    [Fact]
    public void Calculate_RejectsInvalidDateRangeAndEmptyCashAccountSelection()
    {
        var accounts = new[] { new Account("1000", "Cash", AccountType.Asset) };

        Assert.Throws<ArgumentException>(() =>
            CashMovementReportCalculator.Calculate(accounts, [], To, From, "IRR", ["1000"]));
        Assert.Throws<ArgumentException>(() =>
            CashMovementReportCalculator.Calculate(accounts, [], From, To, "IRR", []));
    }

    private static JournalEntry Entry(
        DateOnly date,
        string description,
        params (string Code, EntrySide Side, decimal Amount)[] lines) =>
        new(Guid.NewGuid(), date, description,
            lines.Select(line => new JournalLine(line.Code, line.Code, line.Side, new Money(line.Amount, "IRR"))));

    private static JournalEntry Entry(
        DateOnly date,
        string description,
        params (string Code, EntrySide Side, decimal Amount, string Currency)[] lines) =>
        new(Guid.NewGuid(), date, description,
            lines.Select(line => new JournalLine(line.Code, line.Code, line.Side, new Money(line.Amount, line.Currency))));
}
