namespace Mg.Provisioning.Agent.Core;

/// <summary>
/// Implemented only inside the isolated agent process. Implementations must reconcile
/// existing PostgreSQL state and be safe to repeat after a crash at any instruction.
/// </summary>
public interface ITenantResourceProvisioner
{
    Task EnsureDatabaseAndRolesAsync(
        Guid tenantId,
        Guid idempotencyKey,
        string databaseName,
        string applicationConnectionString,
        CancellationToken cancellationToken);

    Task ApplyTenantSchemaAsync(
        Guid tenantId,
        string applicationConnectionString,
        int migrationVersion,
        string migrationSha256,
        CancellationToken cancellationToken);

    Task<bool> VerifyRuntimeAsync(
        Guid tenantId,
        string databaseName,
        string applicationConnectionString,
        int expectedMigrationVersion,
        string expectedMigrationSha256,
        CancellationToken cancellationToken);
}

public sealed record AgentProvisioningRequest(Guid TenantId, Guid IdempotencyKey);

public sealed class AgentProvisioningBusyException()
    : Exception("A provisioning operation for this tenant is already in progress.");

