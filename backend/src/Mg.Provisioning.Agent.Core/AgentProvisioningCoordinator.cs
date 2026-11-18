using System.Text.RegularExpressions;

namespace Mg.Provisioning.Agent.Core;

/// <summary>
/// Coordinates a tenant operation while holding a PostgreSQL session advisory lock.
/// Every external side-effect stage is idempotent and phase advancement follows success only.
/// </summary>
public sealed class AgentProvisioningCoordinator
{
    private const int TenantSchemaMigrationVersion = 1;
    private static readonly Regex Sha256Pattern = new("^[a-fA-F0-9]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly AgentProvisioningOperationStore operationStore;
    private readonly ITenantResourceProvisioner provisioner;
    private readonly string migrationSha256;

    public AgentProvisioningCoordinator(
        AgentProvisioningOperationStore operationStore,
        ITenantResourceProvisioner provisioner,
        string migrationSha256)
    {
        this.operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
        this.provisioner = provisioner ?? throw new ArgumentNullException(nameof(provisioner));
        if (string.IsNullOrWhiteSpace(migrationSha256) || !Sha256Pattern.IsMatch(migrationSha256))
            throw new ArgumentException("Tenant migration SHA-256 must be a 64-character hexadecimal digest.", nameof(migrationSha256));
        this.migrationSha256 = migrationSha256.ToLowerInvariant();
    }

    public async Task<string> ProvisionAsync(
        AgentProvisioningRequest request,
        Func<CancellationToken, ValueTask<string>> applicationCredentialFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(applicationCredentialFactory);
        if (request.TenantId == Guid.Empty || request.IdempotencyKey == Guid.Empty)
            throw new ArgumentException("Tenant and idempotency identifiers must be non-empty.");

        var databaseName = $"mg_tenant_{request.TenantId:N}";
        var execution = await operationStore.TryExecuteTenantExclusiveAsync(
            request.TenantId,
            async token =>
            {
                var connectionString = await operationStore.GetOrCreatePreparedCredentialAsync(
                    request.TenantId, request.IdempotencyKey, databaseName,
                    applicationCredentialFactory, token);

                var phase = await operationStore.GetPhaseAsync(
                    request.TenantId, request.IdempotencyKey, token);

                if (phase == "Prepared")
                {
                    await provisioner.EnsureDatabaseAndRolesAsync(
                        request.TenantId, request.IdempotencyKey, databaseName, connectionString, token);
                    await operationStore.AdvancePhaseAsync(
                        request.TenantId, request.IdempotencyKey, "Prepared", "DatabaseCreated", token);
                    phase = "DatabaseCreated";
                }

                if (phase == "DatabaseCreated")
                {
                    await provisioner.ApplyTenantSchemaAsync(
                        request.TenantId, connectionString, TenantSchemaMigrationVersion, migrationSha256, token);
                    await operationStore.AdvancePhaseAsync(
                        request.TenantId, request.IdempotencyKey, "DatabaseCreated", "SchemaApplied", token);
                    phase = "SchemaApplied";
                }

                if (phase == "SchemaApplied")
                {
                    var verified = await provisioner.VerifyRuntimeAsync(
                        request.TenantId, databaseName, connectionString,
                        TenantSchemaMigrationVersion, migrationSha256, token);
                    if (!verified)
                        throw new InvalidOperationException(
                            "Tenant database failed independent identity, migration or runtime privilege verification.");

                    await operationStore.AdvancePhaseAsync(
                        request.TenantId, request.IdempotencyKey, "SchemaApplied", "Verified", token);
                    phase = "Verified";
                }

                if (!string.Equals(phase, "Verified", StringComparison.Ordinal))
                    throw new InvalidOperationException("Tenant provisioning stopped in an unexpected durable phase.");

                return connectionString;
            },
            cancellationToken);

        if (!execution.Acquired)
            throw new AgentProvisioningBusyException();

        return execution.Value
            ?? throw new InvalidOperationException("Tenant provisioning completed without an application credential.");
    }
}
