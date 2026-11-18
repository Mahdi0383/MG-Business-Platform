namespace Mg.Platform.Api.Provisioning;

/// <summary>
/// Implementations must provision deterministically for (tenantId, idempotencyKey),
/// use isolated least-privilege infrastructure credentials, and never log connection strings.
/// </summary>
public interface ITenantDatabaseProvisioner
{
    Task<string> ProvisionAsync(
        TenantDatabaseProvisioningRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Verifies the database is reachable, belongs to tenantId, and has the expected
    /// schema/migration baseline. A TCP connection alone is not sufficient.
    /// </summary>
    Task<bool> VerifyAsync(
        string connectionString,
        Guid tenantId,
        CancellationToken cancellationToken);
}

public sealed record TenantDatabaseProvisioningRequest(
    Guid TenantId,
    Guid IdempotencyKey,
    string DatabaseName);
