namespace Mg.Provisioning.Agent.Core;

/// <summary>
/// Coordinates a tenant operation while holding a PostgreSQL session advisory lock.
/// Every external side-effect stage is idempotent and phase advancement follows success only.
/// </summary>
public sealed class AgentProvisioningCoordinator
{
    private readonly AgentProvisioningOperationStore operationStore;
    private readonly ITenantResourceProvisioner provisioner;
    private readonly TenantSchemaMigrationArtifact migration;

    public AgentProvisioningCoordinator(
        AgentProvisioningOperationStore operationStore,
        ITenantResourceProvisioner provisioner,
        TenantSchemaMigrationArtifact migration)
    {
        this.operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
        this.provisioner = provisioner ?? throw new ArgumentNullException(nameof(provisioner));
        this.migration = migration ?? throw new ArgumentNullException(nameof(migration));
        if (migration.Version != 1 || !string.Equals(migration.Name, "tenant_core_baseline", StringComparison.Ordinal))
            throw new ArgumentException("Only the reviewed tenant core baseline migration is supported for initial provisioning.", nameof(migration));
    }

    public async Task<string> ProvisionAsync(
        AgentProvisioningRequest request,
        Func<CancellationToken, ValueTask<AgentProvisioningCredentials>> credentialFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(credentialFactory);
        if (request.TenantId == Guid.Empty || request.IdempotencyKey == Guid.Empty)
            throw new ArgumentException("Tenant and idempotency identifiers must be non-empty.");

        var databaseName = $"mg_tenant_{request.TenantId:N}";
        var execution = await operationStore.TryExecuteTenantExclusiveAsync(
            request.TenantId,
            async token =>
            {
                var credentials = await operationStore.GetOrCreatePreparedCredentialsAsync(
                    request.TenantId, request.IdempotencyKey, databaseName,
                    credentialFactory, token);

                var phase = await operationStore.GetPhaseAsync(
                    request.TenantId, request.IdempotencyKey, token);

                if (phase == "Prepared")
                {
                    await provisioner.EnsureDatabaseAndRolesAsync(
                        request.TenantId, request.IdempotencyKey, databaseName, credentials, token);
                    await operationStore.AdvancePhaseAsync(
                        request.TenantId, request.IdempotencyKey, "Prepared", "DatabaseCreated", token);
                    phase = "DatabaseCreated";
                }

                if (phase == "DatabaseCreated")
                {
                    await provisioner.ApplyTenantSchemaAsync(
                        request.TenantId, credentials, migration, token);
                    await operationStore.AdvancePhaseAsync(
                        request.TenantId, request.IdempotencyKey, "DatabaseCreated", "SchemaApplied", token);
                    phase = "SchemaApplied";
                }

                if (phase == "SchemaApplied")
                {
                    var verified = await provisioner.VerifyRuntimeAsync(
                        request.TenantId, databaseName, credentials.ApplicationConnectionString,
                        migration.Version, migration.Sha256, token);
                    if (!verified)
                        throw new InvalidOperationException(
                            "Tenant database failed independent identity, migration or runtime privilege verification.");

                    await operationStore.AdvancePhaseAsync(
                        request.TenantId, request.IdempotencyKey, "SchemaApplied", "Verified", token);
                    phase = "Verified";
                }

                if (!string.Equals(phase, "Verified", StringComparison.Ordinal))
                    throw new InvalidOperationException("Tenant provisioning stopped in an unexpected durable phase.");

                return credentials.ApplicationConnectionString;
            },
            cancellationToken);

        if (!execution.Acquired)
            throw new AgentProvisioningBusyException();

        return execution.Value
            ?? throw new InvalidOperationException("Tenant provisioning completed without an application credential.");
    }
}
