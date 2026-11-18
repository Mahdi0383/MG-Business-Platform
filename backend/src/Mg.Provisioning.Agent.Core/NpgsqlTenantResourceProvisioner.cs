using System.Text;
using Npgsql;
using NpgsqlTypes;

namespace Mg.Provisioning.Agent.Core;

/// <summary>
/// PostgreSQL implementation for the isolated agent. It uses a dedicated non-superuser
/// control identity, deterministic role/database names and an immutable tenant marker.
/// Invoke only through AgentProvisioningCoordinator, which validates the app TLS string
/// and holds the per-tenant session advisory lock.
/// </summary>
public sealed class NpgsqlTenantResourceProvisioner : ITenantResourceProvisioner
{
    private readonly string controlConnectionString;
    private readonly string controlRoleName;

    public NpgsqlTenantResourceProvisioner(string controlConnectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlConnectionString);
        var parsed = new NpgsqlConnectionStringBuilder(controlConnectionString);
        if (string.IsNullOrWhiteSpace(parsed.Host)
            || string.IsNullOrWhiteSpace(parsed.Database)
            || string.IsNullOrWhiteSpace(parsed.Username)
            || string.IsNullOrWhiteSpace(parsed.Password))
            throw new ArgumentException("Agent PostgreSQL control connection is incomplete.", nameof(controlConnectionString));

        this.controlConnectionString = parsed.ConnectionString;
        controlRoleName = parsed.Username;
    }

    public async Task EnsureDatabaseAndRolesAsync(
        Guid tenantId,
        Guid idempotencyKey,
        string databaseName,
        string applicationConnectionString,
        CancellationToken cancellationToken)
    {
        ValidateTenant(tenantId, idempotencyKey, databaseName);
        var app = ValidateApplicationConnection(applicationConnectionString, tenantId, databaseName);
        await using var connection = new NpgsqlConnection(controlConnectionString);
        await connection.OpenAsync(cancellationToken);
        await VerifyControlRoleAsync(connection, cancellationToken);

        var ownerRole = OwnerRoleName(tenantId);
        var appRole = ApplicationRoleName(tenantId);
        await EnsureRoleAsync(connection, ownerRole, RoleMarker(tenantId, "owner"), login: false, password: null, cancellationToken);
        await EnsureRoleAsync(connection, appRole, RoleMarker(tenantId, "application"), login: true, password: app.Password, cancellationToken);
        await EnsureOwnerMembershipAsync(connection, ownerRole, cancellationToken);
        await EnsureDatabaseAsync(connection, tenantId, databaseName, ownerRole, cancellationToken);
        await ConfigureDatabaseAccessAsync(connection, databaseName, appRole, cancellationToken);
    }

    public async Task ApplyTenantSchemaAsync(
        Guid tenantId,
        string applicationConnectionString,
        TenantSchemaMigrationArtifact migration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(migration);
        if (migration.Version != 1 || migration.Name != "tenant_core_baseline")
            throw new InvalidOperationException("Only the reviewed tenant core baseline migration is supported.");

        var databaseName = $"mg_tenant_{tenantId:N}";
        _ = ValidateApplicationConnection(applicationConnectionString, tenantId, databaseName);
        var ownerRole = OwnerRoleName(tenantId);
        var appRole = ApplicationRoleName(tenantId);

        var builder = new NpgsqlConnectionStringBuilder(controlConnectionString)
        {
            Database = databaseName,
            Pooling = false
        };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await VerifyControlRoleAsync(connection, cancellationToken);
        await SetTenantMigrationContextAsync(connection, tenantId, migration.Sha256, cancellationToken);

        var ledgerExists = await SchemaMigrationLedgerExistsAsync(connection, cancellationToken);
        if (ledgerExists)
        {
            await VerifyExistingBaselineAsync(connection, tenantId, migration, cancellationToken);
        }
        else
        {
            var sql = new UTF8Encoding(false, true).GetString(migration.GetSqlBytes().Span);
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await VerifyExistingBaselineAsync(connection, tenantId, migration, cancellationToken);
        }

        await ApplyRuntimeGrantsAsync(connection, appRole, cancellationToken);
    }

    public async Task<bool> VerifyRuntimeAsync(
        Guid tenantId,
        string databaseName,
        string applicationConnectionString,
        int expectedMigrationVersion,
        string expectedMigrationSha256,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty
            || !string.Equals(databaseName, $"mg_tenant_{tenantId:N}", StringComparison.Ordinal)
            || expectedMigrationVersion != 1
            || string.IsNullOrWhiteSpace(expectedMigrationSha256))
            return false;

        try
        {
            var app = ValidateApplicationConnection(applicationConnectionString, tenantId, databaseName);
            await using var connection = new NpgsqlConnection(app.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                """
                SELECT identity.tenant_id,
                       migration.version,
                       migration.checksum_sha256,
                       current_user,
                       pg_get_userbyid(database.datdba),
                       pg_get_userbyid(tenant_schema.nspowner),
                       COALESCE(shobj_description(database.oid, 'pg_database'), ''),
                       COALESCE(shobj_description(role.oid, 'pg_authid'), ''),
                       role.rolcanlogin,
                       role.rolsuper,
                       role.rolcreatedb,
                       role.rolcreaterole,
                       role.rolreplication,
                       role.rolbypassrls,
                       role.rolinherit,
                       EXISTS (SELECT 1 FROM pg_auth_members membership
                               WHERE membership.member = role.oid OR membership.roleid = role.oid),
                       has_schema_privilege(current_user, 'mg', 'USAGE'),
                       has_schema_privilege(current_user, 'mg', 'CREATE'),
                       has_database_privilege(current_user, current_database(), 'CREATE'),
                       has_table_privilege(current_user, 'mg.tenant_identity', 'INSERT'),
                       has_table_privilege(current_user, 'mg.tenant_identity', 'UPDATE'),
                       has_table_privilege(current_user, 'mg.tenant_identity', 'DELETE'),
                       has_table_privilege(current_user, 'mg.tenant_identity', 'TRUNCATE'),
                       has_table_privilege(current_user, 'mg.tenant_identity', 'REFERENCES'),
                       has_table_privilege(current_user, 'mg.tenant_identity', 'TRIGGER'),
                       has_table_privilege(current_user, 'mg.schema_migrations', 'INSERT'),
                       has_table_privilege(current_user, 'mg.schema_migrations', 'UPDATE'),
                       has_table_privilege(current_user, 'mg.schema_migrations', 'DELETE'),
                       has_table_privilege(current_user, 'mg.schema_migrations', 'TRUNCATE'),
                       has_table_privilege(current_user, 'mg.schema_migrations', 'REFERENCES'),
                       has_table_privilege(current_user, 'mg.schema_migrations', 'TRIGGER'),
                       has_table_privilege(current_user, 'mg.audit_events', 'INSERT'),
                       has_table_privilege(current_user, 'mg.audit_events', 'UPDATE'),
                       has_table_privilege(current_user, 'mg.audit_events', 'DELETE'),
                       has_table_privilege(current_user, 'mg.audit_events', 'TRUNCATE'),
                       has_table_privilege(current_user, 'mg.audit_events', 'REFERENCES'),
                       has_table_privilege(current_user, 'mg.audit_events', 'TRIGGER'),
                       has_table_privilege(current_user, 'mg.customers', 'SELECT'),
                       has_table_privilege(current_user, 'mg.customers', 'INSERT'),
                       has_table_privilege(current_user, 'mg.customers', 'UPDATE'),
                       has_table_privilege(current_user, 'mg.customers', 'DELETE')
                FROM mg.tenant_identity identity
                JOIN mg.schema_migrations migration ON migration.version = @version
                JOIN pg_roles role ON role.rolname = current_user
                JOIN pg_database database ON database.datname = current_database()
                JOIN pg_namespace tenant_schema ON tenant_schema.nspname = 'mg'
                WHERE identity.singleton = TRUE;
                """, connection);
            command.Parameters.AddWithValue("version", NpgsqlDbType.Integer, expectedMigrationVersion);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return false;

            var expectedOwner = OwnerRoleName(tenantId);
            var expectedAppRole = ApplicationRoleName(tenantId);
            var expectedDatabaseMarker = DatabaseMarker(tenantId);
            var expectedAppMarker = RoleMarker(tenantId, "application");

            return reader.GetGuid(0) == tenantId
                && reader.GetInt32(1) == expectedMigrationVersion
                && string.Equals(reader.GetString(2), expectedMigrationSha256, StringComparison.OrdinalIgnoreCase)
                && string.Equals(reader.GetString(3), expectedAppRole, StringComparison.Ordinal)
                && string.Equals(reader.GetString(4), expectedOwner, StringComparison.Ordinal)
                && string.Equals(reader.GetString(5), expectedOwner, StringComparison.Ordinal)
                && string.Equals(reader.GetString(6), expectedDatabaseMarker, StringComparison.Ordinal)
                && string.Equals(reader.GetString(7), expectedAppMarker, StringComparison.Ordinal)
                && reader.GetBoolean(8)
                && !reader.GetBoolean(9)
                && !reader.GetBoolean(10)
                && !reader.GetBoolean(11)
                && !reader.GetBoolean(12)
                && !reader.GetBoolean(13)
                && !reader.GetBoolean(14)
                && !reader.GetBoolean(15)
                && reader.GetBoolean(16)
                && !reader.GetBoolean(17)
                && !reader.GetBoolean(18)
                && !reader.GetBoolean(19)
                && !reader.GetBoolean(20)
                && !reader.GetBoolean(21)
                && !reader.GetBoolean(22)
                && !reader.GetBoolean(23)
                && !reader.GetBoolean(24)
                && !reader.GetBoolean(25)
                && !reader.GetBoolean(26)
                && !reader.GetBoolean(27)
                && !reader.GetBoolean(28)
                && !reader.GetBoolean(29)
                && !reader.GetBoolean(30)
                && !reader.GetBoolean(31)
                && !reader.GetBoolean(32)
                && !reader.GetBoolean(33)
                && !reader.GetBoolean(34)
                && !reader.GetBoolean(35)
                && !reader.GetBoolean(36)
                && reader.GetBoolean(37)
                && reader.GetBoolean(38)
                && reader.GetBoolean(39)
                && reader.GetBoolean(40);
        }
        catch (NpgsqlException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static NpgsqlConnectionStringBuilder ValidateApplicationConnection(
        string applicationString, Guid tenantId, string databaseName)
    {
        NpgsqlConnectionStringBuilder app;
        try
        {
            app = new NpgsqlConnectionStringBuilder(applicationString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("Application database credential is malformed.");
        }

        if (!string.Equals(app.Database, databaseName, StringComparison.Ordinal)
            || !string.Equals(app.Username, ApplicationRoleName(tenantId), StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(app.Host)
            || string.IsNullOrWhiteSpace(app.Password)
            || app.SslMode is not (SslMode.VerifyCA or SslMode.VerifyFull))
            throw new InvalidOperationException("Application database credential does not match the tenant identity or strict TLS policy.");

        return app;
    }

    private async Task VerifyControlRoleAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT rolcanlogin, rolsuper, rolcreatedb, rolcreaterole,
                   rolreplication, rolbypassrls, rolinherit
            FROM pg_roles WHERE rolname = current_user;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)
            || !reader.GetBoolean(0)
            || reader.GetBoolean(1)
            || !reader.GetBoolean(2)
            || !reader.GetBoolean(3)
            || reader.GetBoolean(4)
            || reader.GetBoolean(5)
            || reader.GetBoolean(6))
            throw new InvalidOperationException("Agent database control role does not meet the restricted CREATEDB/CREATEROLE contract.");
    }

    private async Task EnsureRoleAsync(
        NpgsqlConnection connection,
        string roleName,
        string marker,
        bool login,
        string? password,
        CancellationToken cancellationToken)
    {
        var state = await GetRoleStateAsync(connection, roleName, cancellationToken);
        if (state is null)
        {
            var sql = login
                ? await FormatSqlAsync(connection,
                    "SELECT format('CREATE ROLE %I WITH LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', @role, @password);",
                    cancellationToken, ("role", roleName), ("password", password!))
                : await FormatSqlAsync(connection,
                    "SELECT format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', @role);",
                    cancellationToken, ("role", roleName));
            await ExecuteDdlAsync(connection, sql, cancellationToken);
            var commentSql = await FormatSqlAsync(connection,
                "SELECT format('COMMENT ON ROLE %I IS %L', @role, @comment);",
                cancellationToken, ("role", roleName), ("comment", marker));
            await ExecuteDdlAsync(connection, commentSql, cancellationToken);
            return;
        }

        if (!string.Equals(state.Comment, marker, StringComparison.Ordinal)
            || state.CanLogin != login
            || state.IsSuperuser
            || state.CanCreateDatabase
            || state.CanCreateRole
            || state.CanReplicate
            || state.CanBypassRls
            || state.Inherit
            || (login && (state.MemberOfAnyRole || state.HasAnyMembers))
            || (!login && state.MemberOfUnexpectedRole))
            throw new InvalidOperationException("Existing tenant role has an unexpected identity, privilege or membership state.");

        if (login)
        {
            var alterSql = await FormatSqlAsync(connection,
                "SELECT format('ALTER ROLE %I WITH PASSWORD %L', @role, @password);",
                cancellationToken, ("role", roleName), ("password", password!));
            await ExecuteDdlAsync(connection, alterSql, cancellationToken);
        }
    }

    private async Task<RoleState?> GetRoleStateAsync(
        NpgsqlConnection connection, string roleName, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT role.rolcanlogin, role.rolsuper, role.rolcreatedb, role.rolcreaterole,
                   role.rolreplication, role.rolbypassrls, role.rolinherit,
                   COALESCE(shobj_description(role.oid, 'pg_authid'), ''),
                   EXISTS (SELECT 1 FROM pg_auth_members m WHERE m.member = role.oid),
                   EXISTS (SELECT 1 FROM pg_auth_members m WHERE m.roleid = role.oid),
                   EXISTS (SELECT 1 FROM pg_auth_members m
                           WHERE m.roleid = role.oid AND pg_get_userbyid(m.member) <> @control_role)
            FROM pg_roles role
            WHERE role.rolname = @role_name;
            """, connection);
        command.Parameters.AddWithValue("role_name", NpgsqlDbType.Text, roleName);
        command.Parameters.AddWithValue("control_role", NpgsqlDbType.Text, controlRoleName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new RoleState(
            reader.GetBoolean(0), reader.GetBoolean(1), reader.GetBoolean(2), reader.GetBoolean(3),
            reader.GetBoolean(4), reader.GetBoolean(5), reader.GetBoolean(6), reader.GetString(7),
            reader.GetBoolean(8), reader.GetBoolean(9), reader.GetBoolean(10));
    }

    private async Task EnsureOwnerMembershipAsync(
        NpgsqlConnection connection, string ownerRole, CancellationToken cancellationToken)
    {
        var state = await GetRoleStateAsync(connection, ownerRole, cancellationToken)
            ?? throw new InvalidOperationException("Tenant owner role was not created.");
        if (state.MemberOfAnyRole || state.MemberOfUnexpectedRole)
            throw new InvalidOperationException("Tenant owner role has an unexpected role membership.");

        if (!state.HasAnyMembers)
        {
            var grantSql = await FormatSqlAsync(connection,
                "SELECT format('GRANT %I TO %I', @role, @member);",
                cancellationToken, ("role", ownerRole), ("member", controlRoleName));
            await ExecuteDdlAsync(connection, grantSql, cancellationToken);
        }
        else
        {
            var controlMembership = await RoleMembershipExistsAsync(connection, ownerRole, controlRoleName, cancellationToken);
            if (!controlMembership)
                throw new InvalidOperationException("Tenant owner role membership is not held by the isolated agent control identity.");
        }
    }

    private async Task<bool> RoleMembershipExistsAsync(
        NpgsqlConnection connection, string roleName, string memberName, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1 FROM pg_auth_members membership
                JOIN pg_roles granted_role ON granted_role.oid = membership.roleid
                JOIN pg_roles member_role ON member_role.oid = membership.member
                WHERE granted_role.rolname = @role AND member_role.rolname = @member
            );
            """, connection);
        command.Parameters.AddWithValue("role", NpgsqlDbType.Text, roleName);
        command.Parameters.AddWithValue("member", NpgsqlDbType.Text, memberName);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private async Task EnsureDatabaseAsync(
        NpgsqlConnection connection,
        Guid tenantId,
        string databaseName,
        string ownerRole,
        CancellationToken cancellationToken)
    {
        var state = await GetDatabaseStateAsync(connection, databaseName, cancellationToken);
        if (state is null)
        {
            var createSql = await FormatSqlAsync(connection,
                "SELECT format('CREATE DATABASE %I OWNER %I', @database, @owner);",
                cancellationToken, ("database", databaseName), ("owner", ownerRole));
            await ExecuteDdlAsync(connection, createSql, cancellationToken);
            var commentSql = await FormatSqlAsync(connection,
                "SELECT format('COMMENT ON DATABASE %I IS %L', @database, @comment);",
                cancellationToken, ("database", databaseName), ("comment", DatabaseMarker(tenantId)));
            await ExecuteDdlAsync(connection, commentSql, cancellationToken);
            state = await GetDatabaseStateAsync(connection, databaseName, cancellationToken);
        }

        if (state is null
            || !string.Equals(state.Owner, ownerRole, StringComparison.Ordinal)
            || !string.Equals(state.Comment, DatabaseMarker(tenantId), StringComparison.Ordinal))
            throw new InvalidOperationException("Existing tenant database owner or immutable database marker does not match.");
    }

    private async Task<DatabaseState?> GetDatabaseStateAsync(
        NpgsqlConnection connection, string databaseName, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT pg_get_userbyid(database.datdba),
                   COALESCE(shobj_description(database.oid, 'pg_database'), '')
            FROM pg_database database
            WHERE database.datname = @database;
            """, connection);
        command.Parameters.AddWithValue("database", NpgsqlDbType.Text, databaseName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new DatabaseState(reader.GetString(0), reader.GetString(1))
            : null;
    }

    private async Task ConfigureDatabaseAccessAsync(
        NpgsqlConnection connection, string databaseName, string appRole, CancellationToken cancellationToken)
    {
        var sql = await FormatSqlAsync(connection,
            """
            SELECT format(
                'REVOKE ALL ON DATABASE %I FROM PUBLIC; GRANT CONNECT ON DATABASE %I TO %I; GRANT CONNECT ON DATABASE %I TO %I;',
                @database, @database, @app_role, @database, @control_role);
            """, cancellationToken, ("database", databaseName), ("app_role", appRole), ("control_role", controlRoleName));
        await ExecuteDdlAsync(connection, sql, cancellationToken);
    }

    private async Task SetTenantMigrationContextAsync(
        NpgsqlConnection connection, Guid tenantId, string checksum, CancellationToken cancellationToken)
    {
        await using var setRole = new NpgsqlCommand($"SET ROLE {QuoteIdentifier(OwnerRoleName(tenantId))};", connection);
        await setRole.ExecuteNonQueryAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT set_config('mg.tenant_id', @tenant_id, false), set_config('mg.migration_checksum', @checksum, false);",
            connection);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId.ToString("D"));
        command.Parameters.AddWithValue("checksum", NpgsqlDbType.Text, checksum);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> SchemaMigrationLedgerExistsAsync(
        NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT to_regclass('mg.schema_migrations') IS NOT NULL;", connection);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task VerifyExistingBaselineAsync(
        NpgsqlConnection connection,
        Guid tenantId,
        TenantSchemaMigrationArtifact migration,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT identity.tenant_id, ledger.version, ledger.checksum_sha256
            FROM mg.tenant_identity identity
            CROSS JOIN mg.schema_migrations ledger
            WHERE identity.singleton = TRUE AND ledger.version = @version;
            """, connection);
        command.Parameters.AddWithValue("version", NpgsqlDbType.Integer, migration.Version);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)
            || reader.GetGuid(0) != tenantId
            || reader.GetInt32(1) != migration.Version
            || !string.Equals(reader.GetString(2), migration.Sha256, StringComparison.OrdinalIgnoreCase)
            || await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Tenant database identity or baseline migration checksum does not match the reviewed artifact.");
    }

    private async Task ApplyRuntimeGrantsAsync(
        NpgsqlConnection connection, string appRole, CancellationToken cancellationToken)
    {
        var grantSql = await FormatSqlAsync(connection,
            """
            SELECT format(
                'REVOKE ALL ON SCHEMA mg FROM PUBLIC;
                 REVOKE ALL ON ALL TABLES IN SCHEMA mg FROM PUBLIC;
                 GRANT USAGE ON SCHEMA mg TO %I;
                 GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE mg.business_settings, mg.users, mg.customers, mg.user_roles, mg.role_permissions, mg.outbox_messages TO %I;
                 GRANT SELECT ON TABLE mg.roles, mg.permissions, mg.tenant_identity, mg.schema_migrations, mg.audit_events TO %I;',
                @app_role, @app_role, @app_role, @app_role);
            """, cancellationToken, ("app_role", appRole));
        await ExecuteDdlAsync(connection, grantSql, cancellationToken);
    }

    private static async Task<string> FormatSqlAsync(
        NpgsqlConnection connection,
        string formatCommand,
        CancellationToken cancellationToken,
        params (string Name, string Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(formatCommand, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, NpgsqlDbType.Text, value);

        var sql = await command.ExecuteScalarAsync(cancellationToken) as string;
        return sql ?? throw new InvalidOperationException("Unable to format a safe PostgreSQL provisioning command.");
    }

    private static async Task ExecuteDdlAsync(
        NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string QuoteIdentifier(string value) =>
        "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string OwnerRoleName(Guid tenantId) => $"mg_tenant_{tenantId:N}_owner";
    private static string ApplicationRoleName(Guid tenantId) => $"mg_tenant_{tenantId:N}_app";
    private static string RoleMarker(Guid tenantId, string kind) => $"mg-tenant:{tenantId:N}:{kind}:v1";
    private static string DatabaseMarker(Guid tenantId) => $"mg-tenant:{tenantId:N}:database:v1";

    private static void ValidateTenant(Guid tenantId, Guid idempotencyKey, string databaseName)
    {
        if (tenantId == Guid.Empty || idempotencyKey == Guid.Empty
            || !string.Equals(databaseName, $"mg_tenant_{tenantId:N}", StringComparison.Ordinal))
            throw new ArgumentException("Tenant provisioning identity is invalid.");
    }

    private sealed record RoleState(
        bool CanLogin, bool IsSuperuser, bool CanCreateDatabase, bool CanCreateRole,
        bool CanReplicate, bool CanBypassRls, bool Inherit, string Comment,
        bool MemberOfAnyRole, bool HasAnyMembers, bool MemberOfUnexpectedRole);

    private sealed record DatabaseState(string Owner, string Comment);
}
