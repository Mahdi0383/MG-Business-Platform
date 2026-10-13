using MG.BusinessPlatform.Domain.Reservations;

namespace MG.BusinessPlatform.Domain.Tests.Reservations;

public sealed class ReservationTests
{
    [Fact]
    public void Overlaps_ReturnsTrueForSameResourceAndIntersectingTimes()
    {
        var start = new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        var first = Create(start, start.AddHours(2));
        var second = Create(start.AddHours(1), start.AddHours(3));
        Assert.True(first.Overlaps(second));
    }

    [Fact]
    public void Overlaps_UsesHalfOpenIntervalsForAdjacentBookings()
    {
        var start = new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        Assert.False(Create(start, start.AddHours(1)).Overlaps(Create(start.AddHours(1), start.AddHours(2))));
    }

    [Fact]
    public void Overlaps_IgnoresCancelledOrCompletedReservations()
    {
        var start = new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        var active = Create(start, start.AddHours(2));
        Assert.False(active.Overlaps(Create(start.AddMinutes(30), start.AddHours(1), ReservationStatus.Cancelled)));
        Assert.False(active.Overlaps(Create(start.AddMinutes(30), start.AddHours(1), ReservationStatus.Completed)));
    }

    [Fact]
    public void Overlaps_DoesNotConflictAcrossBranches()
    {
        var start = new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        var first = Create(start, start.AddHours(2));
        var otherBranch = Create(start, start.AddHours(2), branchId: Guid.NewGuid());
        Assert.False(first.Overlaps(otherBranch));
    }

    [Fact]
    public void Constructor_RejectsInvalidTimeRange()
    {
        var start = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(() => Create(start, start));
    }

    private static Reservation Create(DateTimeOffset start, DateTimeOffset end,
        ReservationStatus status = ReservationStatus.Confirmed, Guid? branchId = null) =>
        new(Guid.NewGuid(), branchId ?? Branch, Resource, ReservationResourceType.Table,
            "Guest", 2, start, end, status);

    private static readonly Guid Branch = Guid.NewGuid();
    private static readonly Guid Resource = Guid.NewGuid();
}
