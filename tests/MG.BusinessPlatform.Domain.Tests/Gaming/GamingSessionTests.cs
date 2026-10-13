using MG.BusinessPlatform.Domain.Finance;
using MG.BusinessPlatform.Domain.Gaming;

namespace MG.BusinessPlatform.Domain.Tests.Gaming;

public sealed class GamingSessionTests
{
    [Fact]
    public void CalculateTotal_BillsEachPlayerWithEntryAndRoundedTime()
    {
        var start = new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        var session = Create(start, [Player("A", 20m, 120m), Player("B", 10m, 60m)]);
        var total = session.CalculateTotal(start.AddMinutes(61), billingIntervalMinutes: 30, minimumChargeMinutes: 30);

        Assert.Equal(new Money(300m, "IRR"), total);
    }

    [Fact]
    public void CalculateTotal_AppliesMinimumBillingDuration()
    {
        var start = DateTimeOffset.UtcNow;
        var session = Create(start, [Player("A", 5m, 60m)]);
        Assert.Equal(new Money(15m, "IRR"), session.CalculateTotal(start.AddSeconds(1), 1, 10));
    }

    [Fact]
    public void Constructor_RejectsMixedCurrencies()
    {
        var start = DateTimeOffset.UtcNow;
        Assert.Throws<InvalidOperationException>(() => Create(start, [Player("A", 0m, 60m), Player("B", 0m, 60m, "USD")]));
    }

    [Fact]
    public void Complete_RejectsEndBeforeStartAndRepeatedCompletion()
    {
        var start = DateTimeOffset.UtcNow;
        var session = Create(start, [Player("A", 0m, 60m)]);
        Assert.Throws<ArgumentException>(() => session.Complete(start.AddSeconds(-1)));
        var completed = session.Complete(start.AddMinutes(10));
        Assert.Throws<InvalidOperationException>(() => completed.Complete(start.AddMinutes(20)));
    }

    [Fact]
    public void Constructor_RejectsDuplicatePlayerIds()
    {
        var player = Player("A", 0m, 60m);
        Assert.Throws<ArgumentException>(() => Create(DateTimeOffset.UtcNow, [player, player]));
    }

    private static GamingSession Create(DateTimeOffset start, IEnumerable<GamingPlayer> players) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "FIFA", start, players);

    private static GamingPlayer Player(string name, decimal entry, decimal hourly, string currency = "IRR") =>
        new(Guid.NewGuid(), name, new Money(entry, currency), new Money(hourly, currency));
}
