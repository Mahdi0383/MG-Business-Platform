using MG.BusinessPlatform.Domain.Finance;

namespace MG.BusinessPlatform.Domain.Tests.Finance;

public sealed class FinancialPeriodTests
{
    [Fact]
    public void Contains_IncludesBothBoundaryDates()
    {
        var period = OpenPeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        Assert.True(period.Contains(new DateOnly(2026, 1, 1)));
        Assert.True(period.Contains(new DateOnly(2026, 12, 31)));
        Assert.False(period.Contains(new DateOnly(2025, 12, 31)));
        Assert.False(period.Contains(new DateOnly(2027, 1, 1)));
    }

    [Fact]
    public void Constructor_RejectsReversedDates()
    {
        Assert.Throws<ArgumentException>(() => OpenPeriod(new DateOnly(2026, 12, 31), new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public void Constructor_RequiresClosingMetadataForClosedPeriod()
    {
        Assert.Throws<ArgumentException>(() => new FinancialPeriod(Guid.NewGuid(), "FY26",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), true));
    }

    [Fact]
    public void Constructor_RejectsClosingMetadataForOpenPeriod()
    {
        Assert.Throws<ArgumentException>(() => new FinancialPeriod(Guid.NewGuid(), "FY26",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), false, DateTimeOffset.UtcNow, Guid.NewGuid()));
    }

    private static FinancialPeriod OpenPeriod(DateOnly start, DateOnly end) =>
        new(Guid.NewGuid(), "FY26", start, end);
}
