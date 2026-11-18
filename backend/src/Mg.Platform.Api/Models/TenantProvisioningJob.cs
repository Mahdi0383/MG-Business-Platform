namespace Mg.Platform.Api.Models;

/// <summary>
/// Durable central-control-plane work item. A worker will provision tenant databases
/// from this queue; business activation is forbidden until the job succeeds.
/// </summary>
public sealed class TenantProvisioningJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BusinessId { get; set; }
    public string Status { get; set; } = "Pending";
    public int Attempts { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset NextAttemptAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? LastErrorCode { get; set; }
}
