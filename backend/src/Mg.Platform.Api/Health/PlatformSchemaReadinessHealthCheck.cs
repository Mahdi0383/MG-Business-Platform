using System.Data;
using Mg.Platform.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Mg.Platform.Api.Health;

/// <summary>
/// Fails readiness when the central registry/outbox/idempotency schema is not fully migrated.
/// This prevents an API build that requires Idempotency-Key from serving traffic before migration 006.
/// </summary>
public sealed class PlatformSchemaReadinessHealthCheck(PlatformDbContext db) : IHealthCheck
{
    private const string MissingSchemaDescription =
        "Required platform business registry or idempotency schema is missing or incomplete.";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
            await db.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            await using var command = new NpgsqlCommand("""
                SELECT
                    to_regclass('business_tenants') IS NOT NULL
                    AND to_regclass('tenant_provisioning_jobs') IS NOT NULL
                    AND to_regclass('business_creation_requests') IS NOT NULL
                    AND EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = ANY(current_schemas(false))
                          AND table_name = 'business_creation_requests'
                          AND column_name = 'idempotency_key'
                          AND data_type = 'uuid'
                    )
                    AND EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = ANY(current_schemas(false))
                          AND table_name = 'business_creation_requests'
                          AND column_name = 'creator_identity_hash'
                          AND character_maximum_length = 64
                    )
                    AND EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = ANY(current_schemas(false))
                          AND table_name = 'business_creation_requests'
                          AND column_name = 'request_fingerprint'
                          AND character_maximum_length = 64
                    )
                    AND EXISTS (
                        SELECT 1
                        FROM pg_constraint
                        WHERE conrelid = to_regclass('business_creation_requests')
                          AND conname = 'pk_business_creation_requests'
                          AND contype = 'p'
                    )
                """, connection);

            var ready = await command.ExecuteScalarAsync(cancellationToken) is true;
            return ready
                ? HealthCheckResult.Healthy("Required platform registry and idempotency schema is ready.")
                : HealthCheckResult.Unhealthy(MissingSchemaDescription);
        }
        catch (PostgresException)
        {
            return HealthCheckResult.Unhealthy(MissingSchemaDescription);
        }
        finally
        {
            if (closeConnection)
                await db.Database.CloseConnectionAsync();
        }
    }
}
