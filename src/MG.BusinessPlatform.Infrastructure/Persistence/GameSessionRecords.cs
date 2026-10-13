namespace MG.BusinessPlatform.Infrastructure.Persistence;

/// <summary>
/// Durable timer state. All instants are UTC; a paused session stores its pause instant,
/// and accumulated pause duration is retained across process restarts.
/// </summary>
public sealed class GameSessionRecord
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public Guid DeviceId { get; set; }
    public string GameName { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public string CurrencyCode { get; set; } = "IRR";
    public decimal RatePerHour { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? PausedAtUtc { get; set; }
    public DateTimeOffset? EndedAtUtc { get; set; }
    public DateTimeOffset LastTransitionAtUtc { get; set; }
    public long AccumulatedPauseTicks { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public List<GameSessionPlayerRecord> Players { get; set; } = [];
}

public sealed class GameSessionPlayerRecord
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public GameSessionRecord? Session { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public decimal EntryAmount { get; set; }
    public decimal TimeCharge { get; set; }
    public decimal AdditionalAmount { get; set; }
    public string CurrencyCode { get; set; } = "IRR";
    public DateTimeOffset CreatedAtUtc { get; set; }
}
