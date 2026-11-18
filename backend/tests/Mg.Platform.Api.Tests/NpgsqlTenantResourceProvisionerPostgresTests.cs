using System.Security.Cryptography;
using Mg.Provisioning.Agent.Core;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace Mg.Platform.Api.Tests;

public sealed class NpgsqlTenantResourceProvisionerPostgresTests
{
    [Fact]
    public async Task CreatesTenantDatabaseRolesAppliesBaselineAndRecoversIdempotently()
    {
        await using var fixture = await Fixture.CreateAsync();

        await fixture.Provisioner.EnsureDatabaseAndRolesAsync(
            fixture.TenantId, fixture.IdempotencyKey, fixture.DatabaseName,
            fixture.Credentials, CancellationToken.None);

        // Simulate a crash after CREATE DATABASE but before its immutable comment was stored.
        await fixture.SetDatabaseCommentAsync(null);
        await fixture.Provisioner.EnsureDatabaseAndRolesAsync(
            fixture.TenantId, fixture.IdempotencyKey, fixture.DatabaseName,
            fixture.Credentials, CancellationToken.None);

        var migration = TenantSchemaMigrationArtifact.LoadTenantCoreBaseline(AppContext.BaseDirectory);
        await fixture.Provisioner.ApplyTenantSchemaAsync(
            fixture.TenantId, fixture.Credentials, migration, CancellationToken.None);

        // Simulate a retry after the migration committed but before the coordinator advanced its phase.
        await fixture.Provisioner.ApplyTenantSchemaAsync(
            fixture.TenantId, fixture.Credentials, migration, CancellationToken.None);

        var verified = await fixture.Provisioner.VerifyRuntimeAsync(
            fixture.TenantId, fixture.DatabaseName, fixture.Credentials.ApplicationConnectionString,
            migration.Version, migration.Sha256, CancellationToken.None);

        Assert.True(verified);
        await fixture.AssertOwnerRoleIsRestrictedAsync();
        await fixture.AssertRuntimeRoleIsNotOwnerOrPrivilegedAsync();
    }

    [Fact]
    public async Task RejectsExistingTenantDatabaseWithMismatchedImmutableMarker()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Provisioner.EnsureDatabaseAndRolesAsync(
            fixture.TenantId, fixture.IdempotencyKey, fixture.DatabaseName,
            fixture.Credentials, CancellationToken.None);
        await fixture.SetDatabaseCommentAsync("mg-tenant:wrong:database:v1");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Provisioner.EnsureDatabaseAndRolesAsync(
                fixture.TenantId, fixture.IdempotencyKey, fixture.DatabaseName,
                fixture.Credentials, CancellationToken.None));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string adminConnectionString;
        private readonly string controlRoleName;
        private readonly string ownerRoleName;
        private readonly string applicationRoleName;

        private Fixture(
            string adminConnectionString,
            string controlConnectionString,
            string controlRoleName,
            Guid tenantId,
            Guid idempotencyKey,
            string databaseName,
            string applicationConnectionString,
            string ownerConnectionString)
        {
            this.adminConnectionString = adminConnectionString;
            this.controlRoleName = controlRoleName;
            TenantId = tenantId;
            IdempotencyKey = idempotencyKey;
            DatabaseName = databaseName;
            Credentials = new AgentProvisioningCredentials(applicationConnectionString, ownerConnectionString);
            ownerRoleName = $"{databaseName}_owner";
            applicationRoleName = $"{databaseName}_app";
            Provisioner = new NpgsqlTenantResourceProvisioner(controlConnectionString);
        }

        public Guid TenantId { get; }
        public Guid IdempotencyKey { get; }
        public string DatabaseName { get; }
        public AgentProvisioningCredentials Credentials { get; }
        public string ApplicationConnectionString => Credentials.ApplicationConnectionString;
        private string OwnerConnectionString => Credentials.OwnerConnectionString;
        public NpgsqlTenantResourceProvisioner Provisioner { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var configured = Environment.GetEnvironmentVariable("MG_TEST_POSTGRES");
            var rootCertificate = Environment.GetEnvironmentVariable("MG_TEST_PG_ROOT_CERT");
            if (string.IsNullOrWhiteSpace(configured) || string.IsNullOrWhiteSpace(rootCertificate)
                || !File.Exists(rootCertificate))
                throw new InvalidOperationException("MG_TEST_POSTGRES and MG_TEST_PG_ROOT_CERT must point to the disposable TLS-enabled PostgreSQL service.");

            var admin = new NpgsqlConnectionStringBuilder(configured)
            {
                SslMode = SslMode.VerifyFull,
                RootCertificate = rootCertificate,
                Pooling = false
            };
            var controlRole = "mg_agent_control_" + Guid.NewGuid().ToString("N")[..12];
            var controlPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

            await using (var connection = new NpgsqlConnection(admin.ConnectionString))
            {
                await connection.OpenAsync();
                await using var format = new NpgsqlCommand(
                    "SELECT format('CREATE ROLE %I LOGIN PASSWORD %L CREATEDB CREATEROLE NOINHERIT NOSUPERUSER NOREPLICATION NOBYPASSRLS', @role, @password);",
                    connection);
                format.Parameters.AddWithValue("role", NpgsqlDbType.Text, controlRole);
                format.Parameters.AddWithValue("password", NpgsqlDbType.Text, controlPassword);
                var ddl = (string)(await format.ExecuteScalarAsync())!;
                await using var create = new NpgsqlCommand(ddl, connection);
                await create.ExecuteNonQueryAsync();
            }

            var control = new NpgsqlConnectionStringBuilder(admin.ConnectionString)
            {
                Username = controlRole,
                Password = controlPassword,
                Pooling = false
            };
            var tenantId = Guid.NewGuid();
            var databaseName = $"mg_tenant_{tenantId:N}";
            var appPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var ownerPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var app = new NpgsqlConnectionStringBuilder(control.ConnectionString)
            {
                Database = databaseName,
                Username = $"{databaseName}_app",
                Password = appPassword,
                Pooling = true
            };
            var owner = new NpgsqlConnectionStringBuilder(control.ConnectionString)
            {
                Database = databaseName,
                Username = $"{databaseName}_owner",
                Password = ownerPassword,
                Pooling = false
            };

            return new Fixture(
                admin.ConnectionString,
                control.ConnectionString,
                controlRole,
                tenantId,
                Guid.NewGuid(),
                databaseName,
                app.ConnectionString,
                owner.ConnectionString);
        }

        public async Task SetDatabaseCommentAsync(string? comment)
        {
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync();
            await using var format = new NpgsqlCommand(
                "SELECT format('COMMENT ON DATABASE %I IS %L', @database, @comment);", connection);
            format.Parameters.AddWithValue("database", NpgsqlDbType.Text, DatabaseName);
            format.Parameters.AddWithValue("comment", NpgsqlDbType.Text, (object?)comment ?? DBNull.Value);
            var ddl = (string)(await format.ExecuteScalarAsync())!;
            await using var command = new NpgsqlCommand(ddl, connection);
            await command.ExecuteNonQueryAsync();
        }

        public async Task AssertOwnerRoleIsRestrictedAsync()
        {
            await using var connection = new NpgsqlConnection(OwnerConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                SELECT current_user,
                       pg_get_userbyid(database.datdba),
                       role.rolcanlogin,
                       role.rolsuper,
                       role.rolcreatedb,
                       role.rolcreaterole,
                       role.rolreplication,
                       role.rolbypassrls,
                       role.rolinherit,
                       EXISTS (SELECT 1 FROM pg_auth_members m WHERE m.member = role.oid),
                       EXISTS (SELECT 1 FROM pg_auth_members m
                               JOIN pg_roles member_role ON member_role.oid = m.member
                               JOIN pg_roles grantor_role ON grantor_role.oid = m.grantor
                               WHERE m.roleid = role.oid
                                 AND member_role.rolname = @control_role
                                 AND grantor_role.rolname = @control_role
                                 AND m.set_option AND NOT m.inherit_option AND NOT m.admin_option),
                       EXISTS (SELECT 1 FROM pg_auth_members m
                               WHERE m.roleid = role.oid
                                 AND pg_get_userbyid(m.member) <> @control_role),
                       EXISTS (SELECT 1 FROM pg_auth_members m
                               JOIN pg_roles member_role ON member_role.oid = m.member
                               JOIN pg_roles grantor_role ON grantor_role.oid = m.grantor
                               WHERE m.roleid = role.oid
                                 AND member_role.rolname = @control_role
                                 AND NOT (
                                     (grantor_role.rolsuper AND m.admin_option
                                      AND NOT m.inherit_option AND NOT m.set_option)
                                     OR (grantor_role.rolname = @control_role AND m.set_option
                                         AND NOT m.inherit_option AND NOT m.admin_option)
                                 ))
                FROM pg_roles role
                JOIN pg_database database ON database.datname = current_database()
                WHERE role.rolname = current_user;
                """, connection);
            command.Parameters.AddWithValue("control_role", NpgsqlDbType.Text, controlRoleName);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(ownerRoleName, reader.GetString(0));
            Assert.Equal(ownerRoleName, reader.GetString(1));
            Assert.True(reader.GetBoolean(2));
            for (var index = 3; index <= 8; index++)
                Assert.False(reader.GetBoolean(index));
            Assert.False(reader.GetBoolean(9));
            Assert.True(reader.GetBoolean(10));
            Assert.False(reader.GetBoolean(11));
            Assert.False(reader.GetBoolean(12));
        }

        public async Task AssertRuntimeRoleIsNotOwnerOrPrivilegedAsync()
        {
            await using var connection = new NpgsqlConnection(ApplicationConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                SELECT current_user,
                       pg_get_userbyid(database.datdba),
                       role.rolsuper, role.rolcreatedb, role.rolcreaterole,
                       role.rolreplication, role.rolbypassrls, role.rolinherit,
                       EXISTS (SELECT 1 FROM pg_auth_members m
                               WHERE m.member = role.oid
                                  OR (m.roleid = role.oid
                                      AND (pg_get_userbyid(m.member) <> @control_role
                                           OR NOT EXISTS (SELECT 1 FROM pg_roles grantor_role
                                                          WHERE grantor_role.oid = m.grantor AND grantor_role.rolsuper)
                                           OR m.set_option OR m.inherit_option OR NOT m.admin_option))),
                       has_schema_privilege(current_user, 'mg', 'CREATE'),
                       has_database_privilege(current_user, current_database(), 'CREATE')
                FROM pg_roles role
                JOIN pg_database database ON database.datname = current_database()
                WHERE role.rolname = current_user;
                """, connection);
            command.Parameters.AddWithValue("control_role", NpgsqlDbType.Text, controlRoleName);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(applicationRoleName, reader.GetString(0));
            Assert.Equal(ownerRoleName, reader.GetString(1));
            for (var index = 2; index <= 7; index++)
                Assert.False(reader.GetBoolean(index));
            Assert.False(reader.GetBoolean(8));
            Assert.False(reader.GetBoolean(9));
            Assert.False(reader.GetBoolean(10));
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync();

            await using (var terminate = new NpgsqlCommand(
                "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @database AND pid <> pg_backend_pid();",
                connection))
            {
                terminate.Parameters.AddWithValue("database", NpgsqlDbType.Text, DatabaseName);
                await terminate.ExecuteNonQueryAsync();
            }

            var dropDatabase = $"DROP DATABASE IF EXISTS \"{DatabaseName}\";";
            await using (var command = new NpgsqlCommand(dropDatabase, connection))
                await command.ExecuteNonQueryAsync();

            foreach (var role in new[] { applicationRoleName, ownerRoleName, controlRoleName })
            {
                var format = new NpgsqlCommand("SELECT format('DROP ROLE IF EXISTS %I', @role);", connection);
                format.Parameters.AddWithValue("role", NpgsqlDbType.Text, role);
                var ddl = (string)(await format.ExecuteScalarAsync())!;
                await using var drop = new NpgsqlCommand(ddl, connection);
                await drop.ExecuteNonQueryAsync();
                await format.DisposeAsync();
            }
        }
    }
}
