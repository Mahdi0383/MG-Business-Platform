using MG.BusinessPlatform.Domain.Finance;
using MG.BusinessPlatform.Domain.GameCafe;

namespace MG.BusinessPlatform.Domain.Tests.GameCafe;

public sealed class GameSessionTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ElapsedAt_ExcludesPauseAcrossResumeAndCompletion()
    {
        var session = CreateSession();
        session.Pause(Start.AddMinutes(30));
        session.Resume(Start.AddMinutes(90));
        session.Complete(Start.AddMinutes(120));

        Assert.Equal(TimeSpan.FromHours(1), session.ElapsedAt(Start.AddHours(4)));
        Assert.Equal(TimeSpan.FromHours(1), session.AccumulatedPause);
        Assert.Equal(GameSessionStatus.Completed, session.Status);
    }

    [Fact]
    public void PausedSession_TimerAndChargeRemainFrozen()
    {
        var session = CreateSession();
        session.Pause(Start.AddMinutes(17));

        Assert.Equal(TimeSpan.FromMinutes(17), session.ElapsedAt(Start.AddHours(5)));
        Assert.Equal(new Money(200m, "IRR"), session.CalculateTimeCharge(Start.AddHours(5), 15));
    }

    [Fact]
    public void CalculateTimeCharge_RoundsUpToConfiguredBillingIncrement()
    {
        var session = CreateSession(ratePerHour: 600m);

        Assert.Equal(new Money(200m, "IRR"), session.CalculateTimeCharge(Start.AddMinutes(16), 15));
        Assert.Equal(new Money(100m, "IRR"), session.CalculateTimeCharge(Start.AddMinutes(16), 15, roundUp: false));
    }

    [Fact]
    public void Pause_CannotMoveBackBeforeLastResume()
    {
        var session = CreateSession();
        session.Pause(Start.AddMinutes(30));
        session.Resume(Start.AddMinutes(60));

        Assert.Throws<ArgumentOutOfRangeException>(() => session.Pause(Start.AddMinutes(45)));
        Assert.Equal(GameSessionStatus.Active, session.Status);
        Assert.Equal(Start.AddMinutes(60), session.LastTransitionAtUtc);
    }

    [Fact]
    public void InvalidStateTransitions_AreRejected()
    {
        var session = CreateSession();
        Assert.Throws<InvalidOperationException>(() => session.Resume(Start.AddMinutes(1)));
        session.Complete(Start.AddMinutes(5));
        Assert.Throws<InvalidOperationException>(() => session.Pause(Start.AddMinutes(6)));
        Assert.Throws<InvalidOperationException>(() => session.Complete(Start.AddMinutes(6)));
        Assert.Throws<InvalidOperationException>(() => session.AddPlayer(Player(Guid.NewGuid(), "Ali")));
    }

    [Fact]
    public void PlayerCharges_AreCurrencySafeAndIncludedInSessionTotal()
    {
        var session = CreateSession();
        var playerId = Guid.NewGuid();
        session.AddPlayer(Player(playerId, "Ali", entry: 50m, time: 100m, extra: 25m));
        session.AddPlayer(Player(Guid.NewGuid(), "Sara", entry: 75m, time: 100m, extra: 0m));

        Assert.Equal(new Money(350m, "IRR"), session.TotalPlayerCharges);
        Assert.Throws<InvalidOperationException>(() => session.UpdatePlayerCharges(
            playerId, new Money(10m, "USD"), new Money(20m, "USD"), new Money(0m, "USD")));
        Assert.Equal(new Money(175m, "IRR"), session.Players.Single(x => x.Id == playerId).FinalAmount);
        Assert.Throws<InvalidOperationException>(() => session.AddPlayer(Player(Guid.NewGuid(), "Bob", currency: "USD")));
    }

    [Fact]
    public void CancelledSession_HasNoTimeChargeAndCannotResume()
    {
        var session = CreateSession(ratePerHour: 900m);
        session.Cancel(Start.AddMinutes(20));

        Assert.Equal(new Money(0m, "IRR"), session.CalculateTimeCharge(Start.AddHours(1)));
        Assert.Equal(TimeSpan.FromMinutes(20), session.ElapsedAt(Start.AddHours(1)));
        Assert.Throws<InvalidOperationException>(() => session.Resume(Start.AddMinutes(21)));
    }

    [Fact]
    public void Constructor_RejectsNegativeRateAndDuplicatePlayerIds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateSession(ratePerHour: -1m));
        var playerId = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => CreateSession(players:
        [
            Player(playerId, "Ali"),
            Player(playerId, "Sara")
        ]));
    }

    private static GameSession CreateSession(decimal ratePerHour = 600m, IEnumerable<GameSessionPlayer>? players = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Tekken 8", Start,
            new Money(ratePerHour, "IRR"), players);

    private static GameSessionPlayer Player(Guid id, string name, decimal entry = 0m,
        decimal time = 0m, decimal extra = 0m, string currency = "IRR") =>
        new(id, name, new Money(entry, currency), new Money(time, currency), new Money(extra, currency));
}
