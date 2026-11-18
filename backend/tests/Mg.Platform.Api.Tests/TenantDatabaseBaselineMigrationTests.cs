using System.Security.Cryptography;
using System.Text;
using Npgsql;
using Xunit;

namespace Mg.Platform.Api.Tests;

public sealed class TenantDatabaseBaselineMigrationTests
{
    [Fact]
    public async Task BaselineMigrationBindsImmutableTenantIdentityAndRecordsExactChecksum()
    {
        var configured = Environment.GetEnvironmentVariable("MG_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException(
                "MG_TEST_POSTGRES must point to a disposable PostgreSQL test database.");

        var migrationPath = Path.Combine(AppContext.BaseDirectory,
            "tenant-database", "migrations", "001_tenant_core_baseline.sql");
        var migrationBytes = await File.ReadAllBytesAsync(migrationPath);
        var migrationSql = Encoding.UTF8.GetString(migrationBytes);
        var checksum = Convert.ToHexString(SHA256.HashData(migrationBytes)).ToLowerInvariant();
        var tenantId = Guid.NewGuid();
        var databaseName = "mg_tenant_migration_test_" + Guid.NewGuid().ToString("N");
        var adminBuilder = new NpgsqlConnectionStringBuilder(configured)
        {
            Database = "postgres",
            Pooling = false
        };

        await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            var tenantBuilder = new NpgsqlConnectionStringBuilder(configured)
            {
                Database = databaseName,
                Pooling = false
            };
            await using var tenantConnection = new NpgsqlConnection(tenantBuilder.ConnectionString);
            await tenantConnection.OpenAsync();

            await using (var settings = new NpgsqlCommand(
                "SELECT set_config('mg.tenant_id', @tenant_id, false), " +
                "set_config('mg.migration_checksum', @checksum, false)",
                tenantConnection))
            {
                settings.Parameters.AddWithValue("tenant_id", tenantId.ToString());
                settings.Parameters.AddWithValue("checksum", checksum);
                await settings.ExecuteNonQueryAsync();
            }

            await using (var migration = new NpgsqlCommand(migrationSql, tenantConnection))
                await migration.ExecuteNonQueryAsync();

            await using (var identity = new NpgsqlCommand(
                "SELECT tenant_id FROM mg.tenant_identity WHERE singleton = true",
                tenantConnection))
            {
                Assert.Equal(tenantId, await identity.ExecuteScalarAsync());
            }

            await using (var ledger = new NpgsqlCommand(
                "SELECT checksum_sha256 FROM mg.schema_migrations WHERE version = 1",
                tenantConnection))
            {
                Assert.Equal(checksum, (string?)await ledger.ExecuteScalarAsync());
            }

            await using var mutate = new NpgsqlCommand(
                "UPDATE mg.tenant_identity SET tenant_id = @other_tenant",
                tenantConnection);
            mutate.Parameters.AddWithValue("other_tenant", Guid.NewGuid());
            var exception = await Assert.ThrowsAsync<PostgresException>(
                () => mutate.ExecuteNonQueryAsync());
            Assert.Contains("tenant identity is immutable", exception.MessageText,
                StringComparison.Ordinal);
        }
        finally
        {
            await using var admin = new NpgsqlConnection(adminBuilder.ConnectionString);
            await admin.OpenAsync();
            await using (var terminate = new NpgsqlCommand(
                "SELECT pg_terminate_backend(pid) FROM pg_stat_activity " +
                "WHERE datname = @database_name AND pid <> pg_backend_pid()", admin))
            {
                terminate.Parameters.AddWithValue("database_name", databaseName);
                await terminate.ExecuteNonQueryAsync();
            }
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\"", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
