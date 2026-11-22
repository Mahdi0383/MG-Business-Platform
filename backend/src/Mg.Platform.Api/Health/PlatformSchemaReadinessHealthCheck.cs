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
                    AND (
                        SELECT COUNT(*) = 5
                        FROM (VALUES
                            ('idempotency_key', 'uuid', NULL::integer),
                            ('creator_identity_hash', 'character', 64),
                            ('request_fingerprint', 'character', 64),
                            ('business_id', 'uuid', NULL::integer),
                            ('created_at_utc', 'timestamp with time zone', NULL::integer)
                        ) AS required(column_name, data_type, max_length)
                        JOIN information_schema.columns actual
                          ON actual.table_schema = (
                              SELECT table_namespace.nspname
                              FROM pg_class table_relation
                              JOIN pg_namespace table_namespace
                                ON table_namespace.oid = table_relation.relnamespace
                              WHERE table_relation.oid = to_regclass('business_creation_requests')
                          )
                         AND actual.table_name = 'business_creation_requests'
                         AND actual.column_name = required.column_name
                         AND actual.data_type = required.data_type
                         AND actual.is_nullable = 'NO'
                         AND (
                             required.max_length IS NULL
                             OR actual.character_maximum_length = required.max_length
                         )
                    )
                    AND EXISTS (
                        SELECT 1
                        FROM pg_constraint primary_key
                        WHERE primary_key.conrelid = to_regclass('business_creation_requests')
                          AND primary_key.conname = 'pk_business_creation_requests'
                          AND primary_key.contype = 'p'
                    )
                    AND EXISTS (
                        SELECT 1
                        FROM pg_constraint foreign_key
                        JOIN pg_attribute source_column
                          ON source_column.attrelid = foreign_key.conrelid
                         AND source_column.attname = 'business_id'
                         AND NOT source_column.attisdropped
                        JOIN pg_attribute target_column
                          ON target_column.attrelid = foreign_key.confrelid
                         AND target_column.attname = 'id'
                         AND NOT target_column.attisdropped
                        WHERE foreign_key.conrelid = to_regclass('business_creation_requests')
                          AND foreign_key.confrelid = to_regclass('business_tenants')
                          AND foreign_key.contype = 'f'
                          AND foreign_key.conkey = ARRAY[source_column.attnum]::smallint[]
                          AND foreign_key.confkey = ARRAY[target_column.attnum]::smallint[]
                          AND foreign_key.confdeltype = 'r'
                    )
                    AND EXISTS (
                        SELECT 1
                        FROM pg_constraint creator_hash_check
                        WHERE creator_hash_check.conrelid = to_regclass('business_creation_requests')
                          AND creator_hash_check.conname = 'ck_business_creation_creator_hash'
                          AND creator_hash_check.contype = 'c'
                    )
                    AND EXISTS (
                        SELECT 1
                        FROM pg_constraint request_hash_check
                        WHERE request_hash_check.conrelid = to_regclass('business_creation_requests')
                          AND request_hash_check.conname = 'ck_business_creation_request_hash'
                          AND request_hash_check.contype = 'c'
                    )
                    AND EXISTS (
                        SELECT 1
                        FROM pg_index business_lookup_index
                        WHERE business_lookup_index.indrelid = to_regclass('business_creation_requests')
                          AND business_lookup_index.indexrelid =
                              to_regclass('ix_business_creation_requests_business')
                          AND business_lookup_index.indisvalid
                          AND business_lookup_index.indisready
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
