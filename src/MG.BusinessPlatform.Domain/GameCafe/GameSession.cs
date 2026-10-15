using MG.BusinessPlatform.Domain.Finance;

namespace MG.BusinessPlatform.Domain.GameCafe;

public enum GameSessionStatus
{
    Active = 1,
    Paused = 2,
    Completed = 3,
    Cancelled = 4
}

public sealed record GameSessionPlayer
{
    public Guid Id { get; }
    public string DisplayName { get; }
    public Money EntryAmount { get; }
    public Money TimeCharge { get; }
    public Money AdditionalAmount { get; }
    public Money FinalAmount => EntryAmount + TimeCharge + AdditionalAmount;

    public GameSessionPlayer(Guid id, string displayName, Money entryAmount, Money timeCharge, Money additionalAmount)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Player ID is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 120)
            throw new ArgumentException("Player name is required and must not exceed 120 characters.", nameof(displayName));
        if (entryAmount.Amount < 0m)
            throw new ArgumentOutOfRangeException(nameof(entryAmount), "Player entry amount cannot be negative.");
        if (timeCharge.Amount < 0m)
            throw new ArgumentOutOfRangeException(nameof(timeCharge), "Player time charge cannot be negative.");
        if (additionalAmount.Amount < 0m)
            throw new ArgumentOutOfRangeException(nameof(additionalAmount), "Additional player amount cannot be negative.");
        if (entryAmount.CurrencyCode != timeCharge.CurrencyCode || entryAmount.CurrencyCode != additionalAmount.CurrencyCode)
            throw new InvalidOperationException("All player charges must use the same currency.");

        Id = id;
        DisplayName = displayName.Trim();
        EntryAmount = entryAmount;
        TimeCharge = timeCharge;
        AdditionalAmount = additionalAmount;
    }

    public GameSessionPlayer WithCharges(Money entryAmount, Money timeCharge, Money additionalAmount) =>
        new(Id, DisplayName, entryAmount, timeCharge, additionalAmount);
}

/// <summary>
/// Domain aggregate for a timed game-cafe session. Time is stored as UTC instants and
/// paused duration is accumulated explicitly so a process restart can restore the timer.
/// </summary>
public sealed class GameSession
{
    private readonly List<GameSessionPlayer> _players = [];

    public Guid Id { get; }
    public Guid BranchId { get; }
    public Guid DeviceId { get; }
    public string GameName { get; }
    public Money RatePerHour { get; }
    public DateTimeOffset StartedAtUtc { get; }
    public DateTimeOffset? PausedAtUtc { get; private set; }
    public DateTimeOffset? EndedAtUtc { get; private set; }
    public TimeSpan AccumulatedPause { get; private set; }
    public DateTimeOffset LastTransitionAtUtc { get; private set; }
    public GameSessionStatus Status { get; private set; } = GameSessionStatus.Active;
    public IReadOnlyList<GameSessionPlayer> Players => _players.AsReadOnly();
    public Money TotalPlayerCharges => _players.Aggregate(Money.Zero(RatePerHour.CurrencyCode),
        (total, player) => total + player.FinalAmount);

    public GameSession(Guid id, Guid branchId, Guid deviceId, string gameName,
        DateTimeOffset startedAtUtc, Money ratePerHour, IEnumerable<GameSessionPlayer>? players = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Session ID is required.", nameof(id));
        if (branchId == Guid.Empty) throw new ArgumentException("Branch ID is required.", nameof(branchId));
        if (deviceId == Guid.Empty) throw new ArgumentException("Device ID is required.", nameof(deviceId));
        if (string.IsNullOrWhiteSpace(gameName) || gameName.Trim().Length > 160)
            throw new ArgumentException("Game name is required and must not exceed 160 characters.", nameof(gameName));
        if (ratePerHour.Amount < 0m)
            throw new ArgumentOutOfRangeException(nameof(ratePerHour), "Hourly rate cannot be negative.");

        Id = id;
        BranchId = branchId;
        DeviceId = deviceId;
        GameName = gameName.Trim();
        StartedAtUtc = startedAtUtc.ToUniversalTime();
        LastTransitionAtUtc = StartedAtUtc;
        RatePerHour = ratePerHour;

        if (players is not null)
        {
            foreach (var player in players)
                AddPlayer(player);
        }
    }

    public static GameSession Restore(Guid id, Guid branchId, Guid deviceId, string gameName,
        DateTimeOffset startedAtUtc, Money ratePerHour, IEnumerable<GameSessionPlayer> players,
        GameSessionStatus status, DateTimeOffset? pausedAtUtc, DateTimeOffset? endedAtUtc,
        TimeSpan accumulatedPause, DateTimeOffset lastTransitionAtUtc)
    {
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status), "Unknown session status.");
        if (accumulatedPause < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(accumulatedPause), "Accumulated pause cannot be negative.");

        var start = startedAtUtc.ToUniversalTime();
        var lastTransition = lastTransitionAtUtc.ToUniversalTime();
        var paused = pausedAtUtc?.ToUniversalTime();
        var ended = endedAtUtc?.ToUniversalTime();
        if (lastTransition < start || (paused is not null && (paused < start || paused != lastTransition))
            || (ended is not null && ended < start))
            throw new ArgumentException("Persisted session timestamps are inconsistent.");
        if ((status == GameSessionStatus.Active && (paused is not null || ended is not null))
            || (status == GameSessionStatus.Paused && (paused is null || ended is not null))
            || (status is GameSessionStatus.Completed or GameSessionStatus.Cancelled
                && (paused is not null || ended is null || ended != lastTransition)))
            throw new ArgumentException("Persisted session state does not match its timestamps.");

        var elapsedWallClock = (ended ?? paused ?? lastTransition) - start;
        if (accumulatedPause > elapsedWallClock)
            throw new ArgumentException("Persisted accumulated pause exceeds the session's elapsed wall-clock time.");

        var session = new GameSession(id, branchId, deviceId, gameName, start, ratePerHour, players);
        session.Status = status;
        session.PausedAtUtc = paused;
        session.EndedAtUtc = ended;
        session.AccumulatedPause = accumulatedPause;
        session.LastTransitionAtUtc = lastTransition;
        return session;
    }

    public TimeSpan ElapsedAt(DateTimeOffset now)
    {
        var utcNow = now.ToUniversalTime();
        if (utcNow < StartedAtUtc)
            throw new ArgumentOutOfRangeException(nameof(now), "The requested time cannot be before the session start.");
        if (Status == GameSessionStatus.Active && utcNow < LastTransitionAtUtc)
            throw new ArgumentOutOfRangeException(nameof(now), "The requested time cannot be before the last session transition.");
        if (Status == GameSessionStatus.Paused && utcNow < PausedAtUtc)
            throw new ArgumentOutOfRangeException(nameof(now), "The requested time cannot be before the current pause.");

        var end = EndedAtUtc ?? PausedAtUtc ?? utcNow;
        var elapsed = end - StartedAtUtc - AccumulatedPause;
        if (elapsed < TimeSpan.Zero)
            throw new InvalidOperationException("Session timing data is inconsistent.");
        return elapsed;
    }

    public Money CalculateTimeCharge(DateTimeOffset now, int billingIncrementMinutes = 1, bool roundUp = true)
    {
        if (Status == GameSessionStatus.Cancelled)
            return Money.Zero(RatePerHour.CurrencyCode);
        if (billingIncrementMinutes is < 1 or > 60)
            throw new ArgumentOutOfRangeException(nameof(billingIncrementMinutes), "Billing increment must be between 1 and 60 minutes.");

        var elapsedMinutes = ElapsedAt(now).Ticks / (decimal)TimeSpan.TicksPerMinute;
        if (elapsedMinutes == 0m || RatePerHour.Amount == 0m)
            return Money.Zero(RatePerHour.CurrencyCode);

        var increments = elapsedMinutes / billingIncrementMinutes;
        var billableMinutes = (roundUp ? decimal.Ceiling(increments) : decimal.Floor(increments))
            * billingIncrementMinutes;
        return new Money(checked(RatePerHour.Amount * billableMinutes / 60m), RatePerHour.CurrencyCode);
    }

    public void AddPlayer(GameSessionPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);
        EnsureStatus(GameSessionStatus.Active, "Players can only be added to an active session.");
        if (player.EntryAmount.CurrencyCode != RatePerHour.CurrencyCode)
            throw new InvalidOperationException("Player charges and session rate must use the same currency.");
        if (_players.Any(existing => existing.Id == player.Id))
            throw new InvalidOperationException($"Player ID {player.Id} already exists in this session.");
        _players.Add(player);
    }

    public void UpdatePlayerCharges(Guid playerId, Money entryAmount, Money timeCharge, Money additionalAmount)
    {
        EnsureStatus(GameSessionStatus.Active, "Player charges can only be changed while a session is active.");
        if (entryAmount.CurrencyCode != RatePerHour.CurrencyCode
            || timeCharge.CurrencyCode != RatePerHour.CurrencyCode
            || additionalAmount.CurrencyCode != RatePerHour.CurrencyCode)
            throw new InvalidOperationException("Player charges and session rate must use the same currency.");

        var index = _players.FindIndex(player => player.Id == playerId);
        if (index < 0)
            throw new KeyNotFoundException($"Player {playerId} is not part of this session.");

        _players[index] = _players[index].WithCharges(entryAmount, timeCharge, additionalAmount);
    }

    public void Pause(DateTimeOffset at)
    {
        EnsureStatus(GameSessionStatus.Active, "Only an active session can be paused.");
        var utc = at.ToUniversalTime();
        if (utc < LastTransitionAtUtc)
            throw new ArgumentOutOfRangeException(nameof(at), "Pause time cannot be before the last session transition.");
        PausedAtUtc = utc;
        LastTransitionAtUtc = utc;
        Status = GameSessionStatus.Paused;
    }

    public void Resume(DateTimeOffset at)
    {
        EnsureStatus(GameSessionStatus.Paused, "Only a paused session can be resumed.");
        var utc = at.ToUniversalTime();
        if (utc < PausedAtUtc)
            throw new ArgumentOutOfRangeException(nameof(at), "Resume time cannot be before pause time.");
        AccumulatedPause = AccumulatedPause.Add(utc - PausedAtUtc!.Value);
        PausedAtUtc = null;
        LastTransitionAtUtc = utc;
        Status = GameSessionStatus.Active;
    }

    public void Complete(DateTimeOffset at)
    {
        if (Status is not GameSessionStatus.Active and not GameSessionStatus.Paused)
            throw new InvalidOperationException("Only an active or paused session can be completed.");

        var utc = at.ToUniversalTime();
        if (utc < LastTransitionAtUtc)
            throw new ArgumentOutOfRangeException(nameof(at), "Completion time cannot be before the last session transition.");

        if (PausedAtUtc is not null)
        {
            AccumulatedPause = AccumulatedPause.Add(utc - PausedAtUtc.Value);
            PausedAtUtc = null;
        }

        EndedAtUtc = utc;
        LastTransitionAtUtc = utc;
        Status = GameSessionStatus.Completed;
    }

    public void Cancel(DateTimeOffset at)
    {
        if (Status is not GameSessionStatus.Active and not GameSessionStatus.Paused)
            throw new InvalidOperationException("Only an active or paused session can be cancelled.");

        var utc = at.ToUniversalTime();
        if (utc < LastTransitionAtUtc)
            throw new ArgumentOutOfRangeException(nameof(at), "Cancellation time cannot be before the last session transition.");

        if (PausedAtUtc is not null)
        {
            AccumulatedPause = AccumulatedPause.Add(utc - PausedAtUtc.Value);
            PausedAtUtc = null;
        }

        EndedAtUtc = utc;
        LastTransitionAtUtc = utc;
        Status = GameSessionStatus.Cancelled;
    }

    private void EnsureStatus(GameSessionStatus expected, string message)
    {
        if (Status != expected)
            throw new InvalidOperationException(message);
    }
}
