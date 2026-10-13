namespace MG.BusinessPlatform.Domain.Reservations;

public enum ReservationResourceType
{
    Table = 1,
    Room = 2,
    GameStation = 3,
    ServiceSlot = 4
}

public enum ReservationStatus
{
    Requested = 1,
    Confirmed = 2,
    CheckedIn = 3,
    Completed = 4,
    Cancelled = 5,
    NoShow = 6
}

public sealed record Reservation
{
    public Guid Id { get; }
    public Guid BranchId { get; }
    public Guid ResourceId { get; }
    public ReservationResourceType ResourceType { get; }
    public string GuestName { get; }
    public string? GuestPhone { get; }
    public int GuestCount { get; }
    public DateTimeOffset StartsAt { get; }
    public DateTimeOffset EndsAt { get; }
    public ReservationStatus Status { get; }
    public string? Notes { get; }

    public Reservation(Guid id, Guid branchId, Guid resourceId, ReservationResourceType resourceType,
        string guestName, int guestCount, DateTimeOffset startsAt, DateTimeOffset endsAt,
        ReservationStatus status = ReservationStatus.Requested, string? guestPhone = null, string? notes = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Reservation ID is required.", nameof(id));
        if (branchId == Guid.Empty) throw new ArgumentException("Branch ID is required.", nameof(branchId));
        if (resourceId == Guid.Empty) throw new ArgumentException("Resource ID is required.", nameof(resourceId));
        if (!Enum.IsDefined(resourceType)) throw new ArgumentOutOfRangeException(nameof(resourceType));
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        if (string.IsNullOrWhiteSpace(guestName) || guestName.Trim().Length > 200)
            throw new ArgumentException("Guest name is required and must not exceed 200 characters.", nameof(guestName));
        if (guestCount < 1 || guestCount > 1000) throw new ArgumentOutOfRangeException(nameof(guestCount));
        if (endsAt <= startsAt) throw new ArgumentException("Reservation end must be after its start.", nameof(endsAt));
        if (guestPhone?.Length > 40) throw new ArgumentOutOfRangeException(nameof(guestPhone));
        if (notes?.Length > 1000) throw new ArgumentOutOfRangeException(nameof(notes));

        Id = id;
        BranchId = branchId;
        ResourceId = resourceId;
        ResourceType = resourceType;
        GuestName = guestName.Trim();
        GuestPhone = string.IsNullOrWhiteSpace(guestPhone) ? null : guestPhone.Trim();
        GuestCount = guestCount;
        StartsAt = startsAt;
        EndsAt = endsAt;
        Status = status;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    /// <summary>Uses half-open intervals: adjacent bookings (end == next start) do not overlap.</summary>
    public bool Overlaps(Reservation other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (BranchId != other.BranchId || ResourceId != other.ResourceId || ResourceType != other.ResourceType)
            return false;
        if (Status is not (ReservationStatus.Requested or ReservationStatus.Confirmed or ReservationStatus.CheckedIn)
            || other.Status is not (ReservationStatus.Requested or ReservationStatus.Confirmed or ReservationStatus.CheckedIn))
            return false;
        return StartsAt < other.EndsAt && other.StartsAt < EndsAt;
    }
}
