using MG.BusinessPlatform.Domain.Finance;

namespace MG.BusinessPlatform.Domain.Tests.Finance;

public sealed class JournalEntryTests
{
    private static JournalLine Debit(decimal amount, string currency = "IRR") =>
        new("1000", "Cash", EntrySide.Debit, new Money(amount, currency));

    private static JournalLine Credit(decimal amount, string currency = "IRR") =>
        new("4000", "Sales", EntrySide.Credit, new Money(amount, currency));

    [Fact]
    public void Constructor_AcceptsBalancedEntryAndTrimsDescription()
    {
        var entry = new JournalEntry(Guid.NewGuid(), new DateOnly(2026, 10, 13), "  Cash sale  ",
            [Debit(125000m), Credit(125000m)]);

        Assert.Equal("Cash sale", entry.Description);
        Assert.Equal(new Money(125000m, "IRR"), entry.TotalDebits);
        Assert.Equal(entry.TotalDebits, entry.TotalCredits);
        Assert.Equal(2, entry.Lines.Count);
    }

    [Fact]
    public void Constructor_RejectsUnbalancedEntry()
    {
        Assert.Throws<InvalidOperationException>(() => new JournalEntry(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today),
            "Unbalanced", [Debit(100m), Credit(99m)]));
    }

    [Fact]
    public void Constructor_RequiresBothDebitAndCredit()
    {
        Assert.Throws<ArgumentException>(() => new JournalEntry(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today),
            "Debit only", [Debit(100m), Debit(100m)]));
    }

    [Fact]
    public void Constructor_RejectsMixedCurrencies()
    {
        Assert.Throws<InvalidOperationException>(() => new JournalEntry(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today),
            "Mixed", [Debit(100m, "IRR"), Credit(100m, "USD")]));
    }

    [Fact]
    public void Constructor_RequiresAtLeastTwoLines()
    {
        Assert.Throws<ArgumentException>(() => new JournalEntry(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today),
            "One line", [Debit(100m)]));
    }

    [Fact]
    public void JournalLine_RejectsZeroOrNegativeAmounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Debit(0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => Credit(-1m));
    }

    [Fact]
    public void Constructor_RejectsEmptyIdentifierAndDescription()
    {
        Assert.Throws<ArgumentException>(() => new JournalEntry(Guid.Empty, DateOnly.FromDateTime(DateTime.Today),
            "Sale", [Debit(1m), Credit(1m)]));
        Assert.Throws<ArgumentException>(() => new JournalEntry(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today),
            " ", [Debit(1m), Credit(1m)]));
    }

    [Fact]
    public void Lines_CannotBeChangedThroughExposedCollection()
    {
        var entry = new JournalEntry(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today),
            "Sale", [Debit(1m), Credit(1m)]);

        Assert.IsAssignableFrom<IReadOnlyList<JournalLine>>(entry.Lines);
        Assert.Equal(2, entry.Lines.Count);
    }
}
