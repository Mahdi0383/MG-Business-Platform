using Npgsql;
using Xunit;

namespace Mg.Platform.Api.Tests;

public sealed class LeaseFencingMigrationPostgresTests
{
    [Fact]
    public async Task MigrationFencesExistingRunningJobsAndRecoversLeaseLessRows()
    {
        var configured = Environment.GetEnvironmentVariable("MG_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException(
                "MG_TEST_POSTGRES must point to a disposable PostgreSQL test database.");

        var schema = "mg_lease_migration_" + Guid.NewGuid().ToString("N");
        var adminBuilder = new NpgsqlConnectionStringBuilder(configured)
        {
            Pooling = false
        };
        var schemaCreated = false;

        try
        {
            await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
            {
                await admin.OpenAsync();
                await using var createSchema = new NpgsqlCommand(
                    $"CREATE SCHEMA \"{schema}\"", admin);
                await createSchema.ExecuteNonQueryAsync();
                schemaCreated = true;
            }

            var tenantBuilder = new NpgsqlConnectionStringBuilder(configured)
            {
                SearchPath = schema,
                Pooling = false
            };
            var legacyRunningId = Guid.NewGuid();
            var leasedRunningId = Guid.NewGuid();
            var pendingId = Guid.NewGuid();
            var oldLeaseOwner = "legacy-worker";

            await using (var tenant = new NpgsqlConnection(tenantBuilder.ConnectionString))
            {
                await tenant.OpenAsync();
                await using var setup = new NpgsqlCommand("""
                    CREATE TABLE tenant_provisioning_jobs (
                        id uuid PRIMARY KEY,
                        status varchar(20) NOT NULL,
                        next_attempt_at_utc timestamptz NOT NULL,
                        last_error_code varchar(80) NULL,
                        lease_owner varchar(120) NULL,
                        lease_expires_at_utc timestamptz NULL,
                        CONSTRAINT ck_old_lease_pair
                            CHECK ((lease_owner IS NULL) = (lease_expires_at_utc IS NULL))
                    );
                    """, tenant);
                await setup.ExecuteNonQueryAsync();

                await using var insert = new NpgsqlCommand("""
                    INSERT INTO tenant_provisioning_jobs
                        (id, status, next_attempt_at_utc, lease_owner, lease_expires_at_utc)
                    VALUES
                        (@legacy, 'Running', now(), NULL, NULL),
                        (@leased, 'Running', now(), @owner, now() + interval '2 minutes'),
                        (@pending, 'Pending', now(), NULL, NULL)
                    """, tenant);
                insert.Parameters.AddWithValue("legacy", legacyRunningId);
                insert.Parameters.AddWithValue("leased", leasedRunningId);
                insert.Parameters.AddWithValue("pending", pendingId);
                insert.Parameters.AddWithValue("owner", oldLeaseOwner);
                await insert.ExecuteNonQueryAsync();

                var migrationPath = Path.Combine(AppContext.BaseDirectory,
                    "database", "migrations", "005_add_provisioning_lease_fencing_token.sql");
                var migrationSql = await File.ReadAllTextAsync(migrationPath);
                await using var migration = new NpgsqlCommand(migrationSql, tenant);
                await migration.ExecuteNonQueryAsync();

                await using var verify = new NpgsqlCommand("""
                    SELECT id, status, last_error_code, lease_owner, lease_token, lease_expires_at_utc
                    FROM tenant_provisioning_jobs
                    ORDER BY id
                    """, tenant);
                await using var reader = await verify.ExecuteReaderAsync();
                var rows = new Dictionary<Guid, (string Status, string? Error, string? Owner, Guid? Token, DateTimeOffset? Expiry)>();
                while (await reader.ReadAsync())
                {
                    rows.Add(reader.GetGuid(0), (
                        reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetGuid(4),
                        reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5)));
                }

                Assert.Equal("Retry", rows[legacyRunningId].Status);
                Assert.Equal("LEASE_MISSING_ON_UPGRADE", rows[legacyRunningId].Error);
                Assert.Null(rows[legacyRunningId].Owner);
                Assert.Null(rows[legacyRunningId].Token);
                Assert.Null(rows[legacyRunningId].Expiry);

                Assert.Equal("Running", rows[leasedRunningId].Status);
                Assert.Equal(oldLeaseOwner, rows[leasedRunningId].Owner);
                Assert.NotNull(rows[leasedRunningId].Token);
                Assert.NotNull(rows[leasedRunningId].Expiry);

                Assert.Equal("Pending", rows[pendingId].Status);
                Assert.Null(rows[pendingId].Owner);
                Assert.Null(rows[pendingId].Token);

                await reader.DisposeAsync();

                await using var invalidRunning = new NpgsqlCommand("""
                    INSERT INTO tenant_provisioning_jobs (id, status, next_attempt_at_utc)
                    VALUES (@id, 'Running', now())
                    """, tenant);
                invalidRunning.Parameters.AddWithValue("id", Guid.NewGuid());
                var missingLease = await Assert.ThrowsAsync<PostgresException>(
                    () => invalidRunning.ExecuteNonQueryAsync());
                Assert.Equal("ck_tenant_provisioning_jobs_running_lease", missingLease.ConstraintName);

                await using var invalidPending = new NpgsqlCommand("""
                    INSERT INTO tenant_provisioning_jobs
                        (id, status, next_attempt_at_utc, lease_owner, lease_token, lease_expires_at_utc)
                    VALUES (@id, 'Pending', now(), 'unexpected-owner', gen_random_uuid(), now() + interval '1 minute')
                    """, tenant);
                invalidPending.Parameters.AddWithValue("id", Guid.NewGuid());
                var unexpectedLease = await Assert.ThrowsAsync<PostgresException>(
                    () => invalidPending.ExecuteNonQueryAsync());
                Assert.Equal("ck_tenant_provisioning_jobs_running_lease", unexpectedLease.ConstraintName);
            }
        }
        finally
        {
            if (schemaCreated)
            {
                await using var admin = new NpgsqlConnection(adminBuilder.ConnectionString);
                await admin.OpenAsync();
                await using var dropSchema = new NpgsqlCommand(
                    $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", admin);
                await dropSchema.ExecuteNonQueryAsync();
            }
        }
    }
}
