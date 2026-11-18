namespace Mg.Platform.Api.Models;

/// <summary>
/// Durable central-control-plane work item. Provisioning handlers must be idempotent,
/// and a worker must hold a renewable, expiring lease before acting on this job.
/// </summary>
public sealed class TenantProvisioningJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BusinessId { get; set; }
    public string Status { get; set; } = TenantProvisioningStatus.Pending;
    public int Attempts { get; set; }
    public Guid IdempotencyKey { get; set; } = Guid.NewGuid();
    public string? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset NextAttemptAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? LastErrorCode { get; set; }
}
