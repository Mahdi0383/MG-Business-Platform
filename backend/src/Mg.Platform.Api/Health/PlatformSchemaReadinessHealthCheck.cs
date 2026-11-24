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

        try
        {
            if (closeConnection)
                await db.Database.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand("""
                SELECT
                    to_regclass(format('%I.%I', current_schema(), 'platform_schema_migrations')) IS NOT NULL
                    AND (
                        SELECT COUNT(*) = 3
                        FROM (VALUES
                            ('migration_id', 'character varying', 160),
                            ('checksum_sha256', 'character', 64),
                            ('applied_at_utc', 'timestamp with time zone', NULL::integer)
                        ) AS required(column_name, data_type, max_length)
                        JOIN information_schema.columns actual
                          ON actual.table_schema = (
                              SELECT table_namespace.nspname
                              FROM pg_class table_relation
                              JOIN pg_namespace table_namespace
                                ON table_namespace.oid = table_relation.relnamespace
                              WHERE table_relation.oid = to_regclass(format('%I.%I', current_schema(), 'platform_schema_migrations'))
                          )
                         AND actual.table_name = 'platform_schema_migrations'
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
                        FROM pg_constraint migration_primary_key
                        WHERE migration_primary_key.conrelid = to_regclass(format('%I.%I', current_schema(), 'platform_schema_migrations'))
                          AND migration_primary_key.conname = 'pk_platform_schema_migrations'
                          AND migration_primary_key.contype = 'p'
                          AND migration_primary_key.convalidated
                          AND pg_get_constraintdef(migration_primary_key.oid) = 'PRIMARY KEY (migration_id)'
                    )
                    AND EXISTS (
                        SELECT 1
                        FROM pg_constraint migration_checksum_check
                        WHERE migration_checksum_check.conrelid = to_regclass(format('%I.%I', current_schema(), 'platform_schema_migrations'))
                          AND migration_checksum_check.conname = 'ck_platform_schema_migrations_checksum'
                          AND migration_checksum_check.contype = 'c'
                          AND migration_checksum_check.convalidated
                          AND regexp_replace(
                              pg_get_expr(migration_checksum_check.conbin, migration_checksum_check.conrelid),
                              '[()[:space:]]', '', 'g'
                          ) IN (
                              'checksum_sha256~''^[0-9a-f]{64}$''',
                              'checksum_sha256~''^[0-9a-f]{64}$''::bpchar',
                              'checksum_sha256~''^[0-9a-f]{64}$''::character',
                              'checksum_sha256~''^[0-9a-f]{64}$''::text'
                          )
                    )
                    AND to_regclass(format('%I.%I', current_schema(), 'business_tenants')) IS NOT NULL
                    AND to_regclass(format('%I.%I', current_schema(), 'tenant_provisioning_jobs')) IS NOT NULL
                    AND to_regclass(format('%I.%I', current_schema(), 'business_creation_requests')) IS NOT NULL
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
                              WHERE table_relation.oid = to_regclass(format('%I.%I', current_schema(), 'business_creation_requests'))
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
                        WHERE primary_key.conrelid = to_regclass(format('%I.%I', current_schema(), 'business_creation_requests'))
                          AND primary_key.conname = 'pk_business_creation_requests'
                          AND primary_key.contype = 'p'
                          AND primary_key.convalidated
                          AND pg_get_constraintdef(primary_key.oid) = 'PRIMARY KEY (idempotency_key)'
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
                        WHERE foreign_key.conrelid = to_regclass(format('%I.%I', current_schema(), 'business_creation_requests'))
                          AND foreign_key.confrelid = to_regclass(format('%I.%I', current_schema(), 'business_tenants'))
                          AND foreign_key.contype = 'f'
                          AND foreign_key.conkey = ARRAY[source_column.attnum]::smallint[]
                          AND foreign_key.confkey = ARRAY[target_column.attnum]::smallint[]
                          AND foreign_key.confdeltype = 'r'
                          AND foreign_key.confupdtype = 'a'
                          AND foreign_key.confmatchtype = 's'
                          AND NOT foreign_key.condeferrable
                          AND NOT foreign_key.condeferred
                          AND foreign_key.convalidated
                    )
                    AND EXISTS (
                        SELECT 1
                        FROM pg_constraint creator_hash_check
                        WHERE creator_hash_check.conrelid = to_regclass(format('%I.%I', current_schema(), 'business_creation_requests'))
                          AND creator_hash_check.conname = 'ck_business_creation_creator_hash'
                          AND creator_hash_check.contype = 'c'
                          AND creator_hash_check.convalidated
                          AND regexp_replace(
                              pg_get_expr(creator_hash_check.conbin, creator_hash_check.conrelid),
                              '[()[:space:]]', '', 'g'
                          ) IN (
                              'creator_identity_hash~''^[0-9a-f]{64}$''',
                              'creator_identity_hash~''^[0-9a-f]{64}$''::bpchar',
                              'creator_identity_hash~''^[0-9a-f]{64}$''::character',
                              'creator_identity_hash~''^[0-9a-f]{64}$''::text'
                          )
                    )
                    AND EXISTS (
                        SELECT 1
                        FROM pg_constraint request_hash_check
                        WHERE request_hash_check.conrelid = to_regclass(format('%I.%I', current_schema(), 'business_creation_requests'))
                          AND request_hash_check.conname = 'ck_business_creation_request_hash'
                          AND request_hash_check.contype = 'c'
                          AND request_hash_check.convalidated
                          AND regexp_replace(
                              pg_get_expr(request_hash_check.conbin, request_hash_check.conrelid),
                              '[()[:space:]]', '', 'g'
                          ) IN (
                              'request_fingerprint~''^[0-9a-f]{64}$''',
                              'request_fingerprint~''^[0-9a-f]{64}$''::bpchar',
                              'request_fingerprint~''^[0-9a-f]{64}$''::character',
                              'request_fingerprint~''^[0-9a-f]{64}$''::text'
                          )
                    )
                    AND EXISTS (
                        SELECT 1
                        FROM pg_index business_lookup_index
                        WHERE business_lookup_index.indrelid = to_regclass(format('%I.%I', current_schema(), 'business_creation_requests'))
                          AND business_lookup_index.indexrelid =
                              to_regclass(format('%I.%I', current_schema(), 'ix_business_creation_requests_business'))
                          AND business_lookup_index.indisvalid
                          AND business_lookup_index.indisready
                          AND NOT business_lookup_index.indisunique
                          AND business_lookup_index.indnkeyatts = 2
                          AND business_lookup_index.indnatts = 2
                          AND business_lookup_index.indpred IS NULL
                          AND business_lookup_index.indexprs IS NULL
                          AND position(
                              '(business_id, created_at_utc)'
                              IN pg_get_indexdef(business_lookup_index.indexrelid)
                          ) > 0
                    )
                """, connection);

            var schemaIsReady = await command.ExecuteScalarAsync(cancellationToken) is true;
            if (!schemaIsReady)
                return HealthCheckResult.Unhealthy(MissingSchemaDescription);

            await using var schemaCommand = new NpgsqlCommand("SELECT current_schema()", connection);
            var schemaName = await schemaCommand.ExecuteScalarAsync(cancellationToken) as string;
            if (string.IsNullOrWhiteSpace(schemaName))
                return HealthCheckResult.Unhealthy(MissingSchemaDescription);

            // Qualify the data read explicitly: PostgreSQL resolves temporary relations
            // before ordinary schemas even when the temporary schema is absent from search_path.
            var quotedSchema = new NpgsqlCommandBuilder().QuoteIdentifier(schemaName);
            await using var checksumCommand = new NpgsqlCommand(
                $"SELECT EXISTS (SELECT 1 FROM {quotedSchema}.platform_schema_migrations WHERE migration_id = @migration_id AND btrim(checksum_sha256) = @migration_checksum)",
                connection);
            checksumCommand.Parameters.AddWithValue("migration_id", PlatformMigrationChecksums.Migration006Id);
            checksumCommand.Parameters.AddWithValue("migration_checksum", PlatformMigrationChecksums.Migration006Sha256);
            var checksumMatches = await checksumCommand.ExecuteScalarAsync(cancellationToken) is true;

            return checksumMatches
                ? HealthCheckResult.Healthy("Required platform registry and idempotency schema is ready.")
                : HealthCheckResult.Unhealthy(MissingSchemaDescription);
        }
        catch (NpgsqlException)
        {
            return HealthCheckResult.Unhealthy(MissingSchemaDescription);
        }
        finally
        {
            if (closeConnection && connection.State == ConnectionState.Open)
                await db.Database.CloseConnectionAsync();
        }
    }
}
