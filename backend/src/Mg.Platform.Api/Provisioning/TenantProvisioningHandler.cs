using System.Security.Cryptography;
using System.Text;
using Mg.Platform.Api.Data;
using Mg.Platform.Api.Models;
using Mg.Platform.Api.Secrets;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Mg.Platform.Api.Provisioning;

/// <summary>
/// Orchestrates database provisioning, secret persistence/readback and independent
/// tenant identity/schema verification. It never activates a tenant itself.
/// </summary>
public sealed class TenantProvisioningHandler(
    PlatformDbContext platformDb,
    ITenantDatabaseProvisioner databaseProvisioner,
    ITenantSecretStore secretStore) : ITenantProvisioningHandler
{
    public async Task ProvisionAsync(TenantProvisioningJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var tenant = await platformDb.Businesses.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == job.BusinessId, cancellationToken)
            ?? throw new TenantProvisioningException("TENANT_REGISTRY_ENTRY_MISSING", permanent: true);

        if (tenant.Status != "PendingProvisioning")
            throw new TenantProvisioningException("TENANT_STATE_NOT_PROVISIONABLE", permanent: true);

        var expectedReference = $"tenants/{tenant.Id:N}/postgres";
        if (!string.Equals(tenant.DatabaseSecretReference, expectedReference, StringComparison.Ordinal))
            throw new TenantProvisioningException("TENANT_SECRET_REFERENCE_INVALID", permanent: true);

        var databaseName = $"mg_tenant_{tenant.Id:N}";
        var connectionString = await secretStore.ReadAsync(expectedReference, cancellationToken);

        if (connectionString is null)
        {
            connectionString = await databaseProvisioner.ProvisionAsync(
                new TenantDatabaseProvisioningRequest(tenant.Id, job.IdempotencyKey, databaseName),
                cancellationToken);
            ValidateConnectionString(connectionString, databaseName);

            await secretStore.StoreAsync(expectedReference, connectionString, cancellationToken);
            var storedValue = await secretStore.ReadAsync(expectedReference, cancellationToken);
            if (storedValue is null || !SecretsEqual(connectionString, storedValue))
                throw new TenantProvisioningException("TENANT_SECRET_READBACK_FAILED");
            connectionString = storedValue;
        }
        else
        {
            ValidateConnectionString(connectionString, databaseName);
        }

        if (!await databaseProvisioner.VerifyAsync(connectionString, tenant.Id, cancellationToken))
            throw new TenantProvisioningException("TENANT_DATABASE_VERIFICATION_FAILED");
    }

    private static void ValidateConnectionString(string connectionString, string expectedDatabase)
    {
        if (string.IsNullOrWhiteSpace(connectionString) || connectionString.Length > 4096)
            throw new TenantProvisioningException("TENANT_CONNECTION_STRING_INVALID", permanent: true);

        try
        {
            var parsed = new NpgsqlConnectionStringBuilder(connectionString);
            if (!string.Equals(parsed.Database, expectedDatabase, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(parsed.Host)
                || string.IsNullOrWhiteSpace(parsed.Username)
                || parsed.Password.Length == 0
                || parsed.SslMode is not (SslMode.Require or SslMode.VerifyCA or SslMode.VerifyFull)
                || parsed.Pooling == false)
                throw new TenantProvisioningException("TENANT_CONNECTION_STRING_INVALID", permanent: true);
        }
        catch (ArgumentException)
        {
            throw new TenantProvisioningException("TENANT_CONNECTION_STRING_INVALID", permanent: true);
        }
    }

    private static bool SecretsEqual(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        try
        {
            return leftBytes.Length == rightBytes.Length
                && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(leftBytes);
            CryptographicOperations.ZeroMemory(rightBytes);
        }
    }
}
