namespace Mg.Platform.Api.Models;

/// <summary>Allowed lifecycle states persisted by migration 002.</summary>
public static class TenantProvisioningStatus
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Retry = "Retry";
    public const string Succeeded = "Succeeded";
    public const string DeadLetter = "DeadLetter";
}
