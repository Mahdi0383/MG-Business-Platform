namespace MG.BusinessPlatform.Domain.Finance;

/// <summary>
/// Financial period boundaries are inclusive and must be validated by the caller.
/// Closed periods should reject new postings and edits except through an explicit
/// authorized reopening workflow that records an audit event.
/// </summary>
public sealed record FinancialPeriod
{
    public Guid Id { get; }
    public string Name { get; }
    public DateOnly StartsOn { get; }
    public DateOnly EndsOn { get; }
    public bool IsClosed { get; }
    public DateTimeOffset? ClosedAt { get; }
    public Guid? ClosedByUserId { get; }

    public FinancialPeriod(Guid id, string name, DateOnly startsOn, DateOnly endsOn,
        bool isClosed = false, DateTimeOffset? closedAt = null, Guid? closedByUserId = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Period ID is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Period name is required.", nameof(name));
        if (endsOn < startsOn) throw new ArgumentException("Period end must not precede its start.", nameof(endsOn));
        if (closedByUserId == Guid.Empty) throw new ArgumentException("Closed-by user ID must be null or non-empty.", nameof(closedByUserId));
        if (isClosed && (closedAt is null || closedByUserId is null))
            throw new ArgumentException("A closed period requires a closing timestamp and user.");
        if (!isClosed && (closedAt is not null || closedByUserId is not null))
            throw new ArgumentException("An open period cannot carry closing metadata.");

        Id = id;
        Name = name.Trim();
        StartsOn = startsOn;
        EndsOn = endsOn;
        IsClosed = isClosed;
        ClosedAt = closedAt;
        ClosedByUserId = closedByUserId;
    }

    public bool Contains(DateOnly date) => date >= StartsOn && date <= EndsOn;
}
