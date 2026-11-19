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
        var databaseName = $"mg_tenant_{tenantId:N}";
        var roleName = $"{databaseName}_app";
        var rootCertificate = Environment.GetEnvironmentVariable("MG_TEST_PG_ROOT_CERT");
        if (string.IsNullOrWhiteSpace(rootCertificate) || !File.Exists(rootCertificate))
            throw new InvalidOperationException("MG_TEST_PG_ROOT_CERT must point to the disposable PostgreSQL TLS root certificate.");
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var adminBuilder = new NpgsqlConnectionStringBuilder(configured)
        {
            Database = "postgres",
            SslMode = SslMode.VerifyFull,
            RootCertificate = rootCertificate,
            Pooling = false
        };
        var quotedDatabase = $"\"{databaseName}\"";
        var quotedRole = $"\"{roleName}\"";
        var controlRoleName = "mg_agent_control_" + Guid.NewGuid().ToString("N")[..12];
        var quotedControlRole = $"\"{controlRoleName}\"";
        var controlPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var holderRoleName = $"mg_runtime_holder_{Guid.NewGuid():N}";
        var quotedHolderRole = $"\"{holderRoleName}\"";
        var databaseCreated = false;
        var roleCreated = false;
        var controlRoleCreated = false;
        var holderRoleCreated = false;

        try
        {
            await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
            {
                await admin.OpenAsync();
                await using var createRole = new NpgsqlCommand(
                    $"CREATE ROLE {quotedRole} LOGIN PASSWORD '{password}' NOINHERIT", admin);
                await createRole.ExecuteNonQueryAsync();
                roleCreated = true;
                await using var createControlRole = new NpgsqlCommand(
                    $"CREATE ROLE {quotedControlRole} LOGIN PASSWORD '{controlPassword}' " +
                    "CREATEDB CREATEROLE NOINHERIT NOSUPERUSER NOREPLICATION NOBYPASSRLS", admin);
                await createControlRole.ExecuteNonQueryAsync();
                controlRoleCreated = true;
                await using var grantBootstrapMembership = new NpgsqlCommand(
                    $"GRANT {quotedRole} TO {quotedControlRole} WITH ADMIN TRUE, INHERIT FALSE, SET FALSE", admin);
                await grantBootstrapMembership.ExecuteNonQueryAsync();
                await using var createDatabase = new NpgsqlCommand(
                    $"CREATE DATABASE {quotedDatabase} OWNER postgres", admin);
                await createDatabase.ExecuteNonQueryAsync();
                databaseCreated = true;
                await using var grantConnect = new NpgsqlCommand(
                    $"REVOKE ALL PRIVILEGES ON DATABASE {quotedDatabase} FROM PUBLIC; " +
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
                SslMode = SslMode.VerifyFull,
                RootCertificate = rootCertificate,
                Pooling = false
            };
            using var httpClient = new HttpClient(new RejectingHandler());
            var provisioner = new HttpTenantDatabaseProvisioner(httpClient);

            Assert.True(await provisioner.VerifyAsync(runtimeBuilder.ConnectionString,
                tenantId, CancellationToken.None));

            // Only PostgreSQL 17's exact bootstrap ADMIN edge is tolerated.
            foreach (var unsafeOptions in new[]
            {
                "WITH ADMIN FALSE, INHERIT FALSE, SET FALSE",
                "WITH ADMIN TRUE, INHERIT TRUE, SET FALSE",
                "WITH ADMIN TRUE, INHERIT FALSE, SET TRUE"
            })
            {
                await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
                {
                    await admin.OpenAsync();
                    await using var replaceBootstrap = new NpgsqlCommand(
                        $"REVOKE {quotedRole} FROM {quotedControlRole}; " +
                        $"GRANT {quotedRole} TO {quotedControlRole} {unsafeOptions}",
                        admin);
                    await replaceBootstrap.ExecuteNonQueryAsync();
                }
                Assert.False(await provisioner.VerifyAsync(runtimeBuilder.ConnectionString,
                    tenantId, CancellationToken.None));
            }
            await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
            {
                await admin.OpenAsync();
                await using var restoreBootstrap = new NpgsqlCommand(
                    $"REVOKE {quotedRole} FROM {quotedControlRole}; " +
                    $"GRANT {quotedRole} TO {quotedControlRole} WITH ADMIN TRUE, INHERIT FALSE, SET FALSE",
                    admin);
                await restoreBootstrap.ExecuteNonQueryAsync();
            }
            Assert.True(await provisioner.VerifyAsync(runtimeBuilder.ConnectionString,
                tenantId, CancellationToken.None));

            await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
            {
                await admin.OpenAsync();
                await using var grantPublicTemp = new NpgsqlCommand(
                    $"GRANT TEMPORARY ON DATABASE {quotedDatabase} TO PUBLIC", admin);
                await grantPublicTemp.ExecuteNonQueryAsync();
            }
            Assert.False(await provisioner.VerifyAsync(runtimeBuilder.ConnectionString,
                tenantId, CancellationToken.None));
            await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
            {
                await admin.OpenAsync();
                await using var revokePublicTemp = new NpgsqlCommand(
                    $"REVOKE TEMPORARY ON DATABASE {quotedDatabase} FROM PUBLIC", admin);
                await revokePublicTemp.ExecuteNonQueryAsync();
            }
            Assert.True(await provisioner.VerifyAsync(runtimeBuilder.ConnectionString,
                tenantId, CancellationToken.None));

            await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
            {
                await admin.OpenAsync();
                await using var createPublicFunction = new NpgsqlCommand(
                    "CREATE FUNCTION mg.runtime_probe() RETURNS integer LANGUAGE sql AS 'SELECT 1'",
                    admin);
                await createPublicFunction.ExecuteNonQueryAsync();
            }
            Assert.False(await provisioner.VerifyAsync(runtimeBuilder.ConnectionString,
                tenantId, CancellationToken.None));
            await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
            {
                await admin.OpenAsync();
                await using var revokePublicFunction = new NpgsqlCommand(
                    "REVOKE ALL ON FUNCTION mg.runtime_probe() FROM PUBLIC", admin);
                await revokePublicFunction.ExecuteNonQueryAsync();
            }
            Assert.True(await provisioner.VerifyAsync(runtimeBuilder.ConnectionString,
                tenantId, CancellationToken.None));

            await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
            {
                await admin.OpenAsync();
                await using var enableInheritance = new NpgsqlCommand(
                    $"ALTER ROLE {quotedRole} INHERIT", admin);
                await enableInheritance.ExecuteNonQueryAsync();
            }
            Assert.False(await provisioner.VerifyAsync(runtimeBuilder.ConnectionString,
                tenantId, CancellationToken.None));

            await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
            {
                await admin.OpenAsync();
                await using var disableInheritance = new NpgsqlCommand(
                    $"ALTER ROLE {quotedRole} NOINHERIT", admin);
                await disableInheritance.ExecuteNonQueryAsync();
            }
            Assert.True(await provisioner.VerifyAsync(runtimeBuilder.ConnectionString,
                tenantId, CancellationToken.None));

            await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
            {
                await admin.OpenAsync();
                await using var createHolder = new NpgsqlCommand(
                    $"CREATE ROLE {quotedHolderRole} NOLOGIN; GRANT {quotedRole} TO {quotedHolderRole}", admin);
                await createHolder.ExecuteNonQueryAsync();
                holderRoleCreated = true;
            }
            Assert.False(await provisioner.VerifyAsync(runtimeBuilder.ConnectionString,
                tenantId, CancellationToken.None));

            await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
            {
                await admin.OpenAsync();
                await using var revokeHolder = new NpgsqlCommand(
                    $"REVOKE {quotedRole} FROM {quotedHolderRole}; DROP ROLE {quotedHolderRole}", admin);
                await revokeHolder.ExecuteNonQueryAsync();
                holderRoleCreated = false;
            }
            Assert.True(await provisioner.VerifyAsync(runtimeBuilder.ConnectionString,
                tenantId, CancellationToken.None));

            Assert.False(await provisioner.VerifyAsync(runtimeBuilder.ConnectionString,
                Guid.NewGuid(), CancellationToken.None));

            await using (var owner = new NpgsqlConnection(tenantBuilder.ConnectionString))
            {
                await owner.OpenAsync();
                await using var grantAuditUpdate = new NpgsqlCommand(
                    $"GRANT UPDATE ON mg.audit_events TO {quotedRole}", owner);
                await grantAuditUpdate.ExecuteNonQueryAsync();
            }
            Assert.False(await provisioner.VerifyAsync(runtimeBuilder.ConnectionString,
                tenantId, CancellationToken.None));

            await using (var owner = new NpgsqlConnection(tenantBuilder.ConnectionString))
            {
                await owner.OpenAsync();
                await using var revokeAuditUpdate = new NpgsqlCommand(
                    $"REVOKE UPDATE ON mg.audit_events FROM {quotedRole}", owner);
                await revokeAuditUpdate.ExecuteNonQueryAsync();
            }
            Assert.True(await provisioner.VerifyAsync(runtimeBuilder.ConnectionString,
                tenantId, CancellationToken.None));

            await using (var owner = new NpgsqlConnection(tenantBuilder.ConnectionString))
            {
                await owner.OpenAsync();
                await using var grantAuditDelete = new NpgsqlCommand(
                    $"GRANT DELETE ON mg.audit_events TO {quotedRole}", owner);
                await grantAuditDelete.ExecuteNonQueryAsync();
            }
            Assert.False(await provisioner.VerifyAsync(runtimeBuilder.ConnectionString,
                tenantId, CancellationToken.None));

            await using (var owner = new NpgsqlConnection(tenantBuilder.ConnectionString))
            {
                await owner.OpenAsync();
                await using var revokeAuditDelete = new NpgsqlCommand(
                    $"REVOKE DELETE ON mg.audit_events FROM {quotedRole}", owner);
                await revokeAuditDelete.ExecuteNonQueryAsync();
            }
            Assert.True(await provisioner.VerifyAsync(runtimeBuilder.ConnectionString,
                tenantId, CancellationToken.None));

            await using (var owner = new NpgsqlConnection(tenantBuilder.ConnectionString))
            {
                await owner.OpenAsync();
                await using var grantAuditInsert = new NpgsqlCommand(
                    $"GRANT INSERT ON mg.audit_events TO {quotedRole}", owner);
                await grantAuditInsert.ExecuteNonQueryAsync();
            }
            Assert.False(await provisioner.VerifyAsync(runtimeBuilder.ConnectionString,
                tenantId, CancellationToken.None));

            await using (var owner = new NpgsqlConnection(tenantBuilder.ConnectionString))
            {
                await owner.OpenAsync();
                await using var revokeAuditInsert = new NpgsqlCommand(
                    $"REVOKE INSERT ON mg.audit_events FROM {quotedRole}", owner);
                await revokeAuditInsert.ExecuteNonQueryAsync();
            }
            Assert.True(await provisioner.VerifyAsync(runtimeBuilder.ConnectionString,
                tenantId, CancellationToken.None));

            await using (var owner = new NpgsqlConnection(tenantBuilder.ConnectionString))
            {
                await owner.OpenAsync();
                await using var grantAuditTrigger = new NpgsqlCommand(
                    $"GRANT TRIGGER ON mg.audit_events TO {quotedRole}", owner);
                await grantAuditTrigger.ExecuteNonQueryAsync();
            }
            Assert.False(await provisioner.VerifyAsync(runtimeBuilder.ConnectionString,
                tenantId, CancellationToken.None));

            await using (var owner = new NpgsqlConnection(tenantBuilder.ConnectionString))
            {
                await owner.OpenAsync();
                await using var revokeAuditTrigger = new NpgsqlCommand(
                    $"REVOKE TRIGGER ON mg.audit_events FROM {quotedRole}", owner);
                await revokeAuditTrigger.ExecuteNonQueryAsync();
            }
            Assert.True(await provisioner.VerifyAsync(runtimeBuilder.ConnectionString,
                tenantId, CancellationToken.None));

            await using (var owner = new NpgsqlConnection(tenantBuilder.ConnectionString))
            {
                await owner.OpenAsync();
                await using var grantSchemaCreate = new NpgsqlCommand(
                    $"GRANT CREATE ON SCHEMA mg TO {quotedRole}", owner);
                await grantSchemaCreate.ExecuteNonQueryAsync();
            }
            Assert.False(await provisioner.VerifyAsync(runtimeBuilder.ConnectionString,
                tenantId, CancellationToken.None));

            await using (var owner = new NpgsqlConnection(tenantBuilder.ConnectionString))
            {
                await owner.OpenAsync();
                await using var revokeSchemaCreate = new NpgsqlCommand(
                    $"REVOKE CREATE ON SCHEMA mg FROM {quotedRole}", owner);
                await revokeSchemaCreate.ExecuteNonQueryAsync();
            }
            Assert.True(await provisioner.VerifyAsync(runtimeBuilder.ConnectionString,
                tenantId, CancellationToken.None));

            await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
            {
                await admin.OpenAsync();
                await using var grantMembership = new NpgsqlCommand(
                    $"GRANT pg_read_all_data TO {quotedRole}", admin);
                await grantMembership.ExecuteNonQueryAsync();
            }
            Assert.False(await provisioner.VerifyAsync(runtimeBuilder.ConnectionString,
                tenantId, CancellationToken.None));

            await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
            {
                await admin.OpenAsync();
                await using var revokeMembership = new NpgsqlCommand(
                    $"REVOKE pg_read_all_data FROM {quotedRole}", admin);
                await revokeMembership.ExecuteNonQueryAsync();
            }
            Assert.True(await provisioner.VerifyAsync(runtimeBuilder.ConnectionString,
                tenantId, CancellationToken.None));

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
            if (holderRoleCreated)
            {
                await using var dropHolderRole = new NpgsqlCommand(
                    $"DROP ROLE IF EXISTS {quotedHolderRole}", admin);
                await dropHolderRole.ExecuteNonQueryAsync();
            }
            if (controlRoleCreated)
            {
                await using var dropControlRole = new NpgsqlCommand(
                    $"DROP ROLE IF EXISTS {quotedControlRole}", admin);
                await dropControlRole.ExecuteNonQueryAsync();
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
