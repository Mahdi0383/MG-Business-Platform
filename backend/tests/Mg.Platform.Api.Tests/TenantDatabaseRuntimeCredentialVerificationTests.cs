using System.Security.Cryptography;
using System.Text;
using System.Net;
using Mg.Platform.Api.Provisioning;
using Npgsql;
using Xunit;

namespace Mg.Platform.Api.Tests;

public sealed class TenantDatabaseRuntimeCredentialVerificationTests
{
    [Fact]
    public async Task AcceptsLeastPrivilegeRuntimeRoleAndRejectsProvisioningCapabilities()
    {
        var configured = Environment.GetEnvironmentVariable("MG_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException(
                "MG_TEST_POSTGRES must point to a disposable PostgreSQL test database.");

        var tenantId = Guid.NewGuid();
        var databaseName = "mg_verify_" + Guid.NewGuid().ToString("N");
        var roleName = "mg_runtime_" + Guid.NewGuid().ToString("N");
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var adminBuilder = new NpgsqlConnectionStringBuilder(configured)
        {
            Database = "postgres",
            Pooling = false
        };
        var quotedDatabase = $"\"{databaseName}\"";
        var quotedRole = $"\"{roleName}\"";
        var databaseCreated = false;
        var roleCreated = false;

        try
        {
            await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
            {
                await admin.OpenAsync();
                await using var createRole = new NpgsqlCommand(
                    $"CREATE ROLE {quotedRole} LOGIN PASSWORD '{password}'", admin);
                await createRole.ExecuteNonQueryAsync();
                roleCreated = true;
                await using var createDatabase = new NpgsqlCommand(
                    $"CREATE DATABASE {quotedDatabase} OWNER postgres", admin);
                await createDatabase.ExecuteNonQueryAsync();
                databaseCreated = true;
                await using var grantConnect = new NpgsqlCommand(
                    $"GRANT CONNECT ON DATABASE {quotedDatabase} TO {quotedRole}", admin);
                await grantConnect.ExecuteNonQueryAsync();
            }

            var tenantBuilder = new NpgsqlConnectionStringBuilder(configured)
            {
                Database = databaseName,
                Pooling = false
            };
            var migrationPath = Path.Combine(AppContext.BaseDirectory,
                "tenant-database", "migrations", "001_tenant_core_baseline.sql");
            var migrationBytes = await File.ReadAllBytesAsync(migrationPath);
            var migrationSql = Encoding.UTF8.GetString(migrationBytes);
            var checksum = Convert.ToHexString(SHA256.HashData(migrationBytes)).ToLowerInvariant();

            await using (var ownerConnection = new NpgsqlConnection(tenantBuilder.ConnectionString))
            {
                await ownerConnection.OpenAsync();
                await using (var settings = new NpgsqlCommand(
                    "SELECT set_config('mg.tenant_id', @tenant_id, false), " +
                    "set_config('mg.migration_checksum', @checksum, false)",
                    ownerConnection))
                {
                    settings.Parameters.AddWithValue("tenant_id", tenantId.ToString());
                    settings.Parameters.AddWithValue("checksum", checksum);
                    await settings.ExecuteNonQueryAsync();
                }
                await using var migration = new NpgsqlCommand(migrationSql, ownerConnection);
                await migration.ExecuteNonQueryAsync();
                await using var grantRuntime = new NpgsqlCommand(
                    $"GRANT USAGE ON SCHEMA mg TO {quotedRole}; " +
                    $"GRANT SELECT ON mg.tenant_identity, mg.schema_migrations TO {quotedRole}",
                    ownerConnection);
                await grantRuntime.ExecuteNonQueryAsync();
            }

            var runtimeBuilder = new NpgsqlConnectionStringBuilder(configured)
            {
                Database = databaseName,
                Username = roleName,
                Password = password,
                SslMode = SslMode.Disable,
                Pooling = false
            };
            using var httpClient = new HttpClient(new RejectingHandler());
            var provisioner = new HttpTenantDatabaseProvisioner(httpClient);

            Assert.True(await provisioner.VerifyAsync(runtimeBuilder.ConnectionString,
                tenantId, CancellationToken.None));
            Assert.False(await provisioner.VerifyAsync(runtimeBuilder.ConnectionString,
                Guid.NewGuid(), CancellationToken.None));

            await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
            {
                await admin.OpenAsync();
                await using var elevate = new NpgsqlCommand(
                    $"ALTER ROLE {quotedRole} CREATEDB", admin);
                await elevate.ExecuteNonQueryAsync();
            }

            Assert.False(await provisioner.VerifyAsync(runtimeBuilder.ConnectionString,
                tenantId, CancellationToken.None));

            await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
            {
                await admin.OpenAsync();
                await using var revoke = new NpgsqlCommand(
                    $"ALTER ROLE {quotedRole} NOCREATEDB", admin);
                await revoke.ExecuteNonQueryAsync();
                await using var transfer = new NpgsqlCommand(
                    $"ALTER DATABASE {quotedDatabase} OWNER TO {quotedRole}", admin);
                await transfer.ExecuteNonQueryAsync();
            }

            Assert.False(await provisioner.VerifyAsync(runtimeBuilder.ConnectionString,
                tenantId, CancellationToken.None));
        }
        finally
        {
            await using var admin = new NpgsqlConnection(adminBuilder.ConnectionString);
            await admin.OpenAsync();
            if (databaseCreated)
            {
                await using var terminate = new NpgsqlCommand(
                    "SELECT pg_terminate_backend(pid) FROM pg_stat_activity " +
                    "WHERE datname = @database_name AND pid <> pg_backend_pid()", admin);
                terminate.Parameters.AddWithValue("database_name", databaseName);
                await terminate.ExecuteNonQueryAsync();
                await using var dropDatabase = new NpgsqlCommand(
                    $"DROP DATABASE IF EXISTS {quotedDatabase}", admin);
                await dropDatabase.ExecuteNonQueryAsync();
            }
            if (roleCreated)
            {
                await using var dropRole = new NpgsqlCommand(
                    $"DROP ROLE IF EXISTS {quotedRole}", admin);
                await dropRole.ExecuteNonQueryAsync();
            }
        }
    }

    private sealed class RejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
