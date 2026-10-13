using MG.BusinessPlatform.Domain.Finance;

namespace MG.BusinessPlatform.Domain.Gaming;

public sealed record GamingPlayer
{
    public Guid Id { get; }
    public string Name { get; }
    public Money EntryFee { get; }
    public Money HourlyRate { get; }

    public GamingPlayer(Guid id, string name, Money entryFee, Money hourlyRate)
    {
        if (id == Guid.Empty) throw new ArgumentException("Player ID is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 160)
            throw new ArgumentException("Player name is required and must not exceed 160 characters.", nameof(name));
        if (entryFee.Amount < 0m) throw new ArgumentOutOfRangeException(nameof(entryFee));
        if (hourlyRate.Amount < 0m) throw new ArgumentOutOfRangeException(nameof(hourlyRate));
        if (entryFee.CurrencyCode != hourlyRate.CurrencyCode)
            throw new InvalidOperationException("Player entry fee and hourly rate currencies must match.");

        Id = id;
        Name = name.Trim();
        EntryFee = entryFee;
        HourlyRate = hourlyRate;
    }

    public Money CalculateTotal(TimeSpan elapsed, int billingIntervalMinutes, int minimumChargeMinutes)
    {
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        if (billingIntervalMinutes is < 1 or > 1440) throw new ArgumentOutOfRangeException(nameof(billingIntervalMinutes));
        if (minimumChargeMinutes is < 0 or > 10080) throw new ArgumentOutOfRangeException(nameof(minimumChargeMinutes));

        var actualMinutes = (decimal)elapsed.Ticks / TimeSpan.TicksPerMinute;
        var roundedMinutes = actualMinutes == 0m ? 0m
            : decimal.Ceiling(actualMinutes / billingIntervalMinutes) * billingIntervalMinutes;
        var billableMinutes = Math.Max(roundedMinutes, minimumChargeMinutes);
        var usageCharge = HourlyRate.Multiply(billableMinutes / 60m);
        return EntryFee + usageCharge;
    }
}

public sealed class GamingSession
{
    private readonly IReadOnlyList<GamingPlayer> _players;

    public Guid Id { get; }
    public Guid BranchId { get; }
    public Guid StationId { get; }
    public string GameName { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset? EndedAt { get; }
    public bool IsCompleted => EndedAt is not null;
    public IReadOnlyList<GamingPlayer> Players => _players;
    public string CurrencyCode { get; }

    public GamingSession(Guid id, Guid branchId, Guid stationId, string gameName,
        DateTimeOffset startedAt, IEnumerable<GamingPlayer> players, DateTimeOffset? endedAt = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Session ID is required.", nameof(id));
        if (branchId == Guid.Empty) throw new ArgumentException("Branch ID is required.", nameof(branchId));
        if (stationId == Guid.Empty) throw new ArgumentException("Station ID is required.", nameof(stationId));
        if (string.IsNullOrWhiteSpace(gameName) || gameName.Trim().Length > 200)
            throw new ArgumentException("Game name is required and must not exceed 200 characters.", nameof(gameName));
        if (endedAt is not null && endedAt.Value < startedAt)
            throw new ArgumentException("Session end cannot precede its start.", nameof(endedAt));
        ArgumentNullException.ThrowIfNull(players);

        var materialized = players.ToArray();
        if (materialized.Length is < 1 or > 100)
            throw new ArgumentException("A gaming session requires between 1 and 100 players.", nameof(players));
        if (materialized.Any(player => player is null))
            throw new ArgumentException("Players cannot contain null values.", nameof(players));
        if (materialized.Select(player => player.Id).Distinct().Count() != materialized.Length)
            throw new ArgumentException("Player IDs must be unique within a session.", nameof(players));

        var currency = materialized[0].EntryFee.CurrencyCode;
        if (materialized.Any(player => player.EntryFee.CurrencyCode != currency))
            throw new InvalidOperationException("All players in a session must use the same currency.");

        Id = id;
        BranchId = branchId;
        StationId = stationId;
        GameName = gameName.Trim();
        StartedAt = startedAt;
        EndedAt = endedAt;
        CurrencyCode = currency;
        _players = Array.AsReadOnly(materialized);
    }

    public Money CalculateTotal(DateTimeOffset asOf, int billingIntervalMinutes = 1, int minimumChargeMinutes = 0)
    {
        var effectiveEnd = EndedAt ?? asOf;
        if (effectiveEnd < StartedAt) throw new ArgumentOutOfRangeException(nameof(asOf));
        return _players.Aggregate(Money.Zero(CurrencyCode),
            (total, player) => total + player.CalculateTotal(effectiveEnd - StartedAt, billingIntervalMinutes, minimumChargeMinutes));
    }

    public GamingSession Complete(DateTimeOffset endedAt)
    {
        if (IsCompleted)
            throw new InvalidOperationException("A completed gaming session cannot be completed again.");
        return new GamingSession(Id, BranchId, StationId, GameName, StartedAt, _players, endedAt);
    }
}
