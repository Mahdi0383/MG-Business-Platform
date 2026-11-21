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
    private readonly string controlHost;
    private readonly int controlPort;
    private readonly Func<CancellationToken, Task>? afterOwnerRoleCreated;
    private readonly Func<CancellationToken, Task>? afterApplicationRoleCreated;
    private readonly Func<CancellationToken, Task>? afterOwnerMembershipEnsured;

    public NpgsqlTenantResourceProvisioner(string controlConnectionString)
        : this(controlConnectionString, afterOwnerRoleCreated: null)
    {
    }

    internal NpgsqlTenantResourceProvisioner(
        string controlConnectionString,
        Func<CancellationToken, Task>? afterOwnerRoleCreated,
        Func<CancellationToken, Task>? afterApplicationRoleCreated = null,
        Func<CancellationToken, Task>? afterOwnerMembershipEnsured = null)
    {
        this.afterOwnerRoleCreated = afterOwnerRoleCreated;
        this.afterApplicationRoleCreated = afterApplicationRoleCreated;
        this.afterOwnerMembershipEnsured = afterOwnerMembershipEnsured;
        ArgumentException.ThrowIfNullOrWhiteSpace(controlConnectionString);
        var parsed = new NpgsqlConnectionStringBuilder(controlConnectionString);
        if (string.IsNullOrWhiteSpace(parsed.Host)
            || string.IsNullOrWhiteSpace(parsed.Database)
            || string.IsNullOrWhiteSpace(parsed.Username)
            || string.IsNullOrWhiteSpace(parsed.Password)
            || parsed.SslMode is not (SslMode.VerifyCA or SslMode.VerifyFull))
            throw new ArgumentException("Agent PostgreSQL control connection must be complete and use strict TLS certificate validation.", nameof(controlConnectionString));

        parsed.Pooling = false;
        this.controlConnectionString = parsed.ConnectionString;
        controlRoleName = parsed.Username;
        controlHost = parsed.Host;
        controlPort = parsed.Port;
    }

    public async Task EnsureDatabaseAndRolesAsync(
        Guid tenantId,
        Guid idempotencyKey,
        string databaseName,
        AgentProvisioningCredentials credentials,
        CancellationToken cancellationToken)
    {
        ValidateTenant(tenantId, idempotencyKey, databaseName);
        ArgumentNullException.ThrowIfNull(credentials);
        var app = ValidateApplicationConnection(credentials.ApplicationConnectionString, tenantId, databaseName);
        var owner = ValidateOwnerConnection(credentials.OwnerConnectionString, tenantId, databaseName);
        await using var connection = new NpgsqlConnection(controlConnectionString);
        await connection.OpenAsync(cancellationToken);
        await VerifyControlRoleAsync(connection, cancellationToken);

        var ownerRole = OwnerRoleName(tenantId);
        var appRole = ApplicationRoleName(tenantId);
        await using (var roleTransaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            await EnsureRoleAsync(connection, roleTransaction, ownerRole, RoleMarker(tenantId, "owner"),
                login: true, password: owner.Password, cancellationToken);
            if (afterOwnerRoleCreated is not null)
                await afterOwnerRoleCreated(cancellationToken);
            await EnsureRoleAsync(connection, roleTransaction, appRole, RoleMarker(tenantId, "application"),
                login: true, password: app.Password, cancellationToken);
            if (afterApplicationRoleCreated is not null)
                await afterApplicationRoleCreated(cancellationToken);
            await EnsureOwnerRoleMembershipAsync(connection, roleTransaction, ownerRole, cancellationToken);
            if (afterOwnerMembershipEnsured is not null)
                await afterOwnerMembershipEnsured(cancellationToken);
            await VerifyControlRoleAsync(connection, cancellationToken, roleTransaction);
            await roleTransaction.CommitAsync(cancellationToken);
        }
        await EnsureDatabaseAsync(connection, tenantId, databaseName, ownerRole,
            credentials.OwnerConnectionString, appRole, cancellationToken);
    }

    public async Task ApplyTenantSchemaAsync(
        Guid tenantId,
        AgentProvisioningCredentials credentials,
        TenantSchemaMigrationArtifact migration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(migration);
        if (migration.Version != 1 || migration.Name != "tenant_core_baseline")
            throw new InvalidOperationException("Only the reviewed tenant core baseline migration is supported.");

        var databaseName = $"mg_tenant_{tenantId:N}";
        _ = ValidateApplicationConnection(credentials.ApplicationConnectionString, tenantId, databaseName);
        var owner = ValidateOwnerConnection(credentials.OwnerConnectionString, tenantId, databaseName);
        var appRole = ApplicationRoleName(tenantId);

        owner.Pooling = false;
        await using var connection = new NpgsqlConnection(owner.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await VerifyTenantOwnerConnectionAsync(connection, tenantId, databaseName, cancellationToken);
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
                               WHERE membership.member = role.oid
                                  OR (membership.roleid = role.oid
                                      AND (pg_get_userbyid(membership.member) <> @control_role
                                           OR NOT EXISTS (
                                               SELECT 1 FROM pg_roles grantor_role
                                               WHERE grantor_role.oid = membership.grantor
                                                 AND grantor_role.rolsuper
                                           )
                                           OR membership.set_option
                                           OR membership.inherit_option
                                           OR NOT membership.admin_option))),
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
                       has_table_privilege(current_user, 'mg.customers', 'DELETE'),
                       EXISTS (SELECT 1 FROM pg_class relation
                               JOIN pg_namespace namespace ON namespace.oid = relation.relnamespace
                               WHERE namespace.nspname = 'mg' AND relation.relowner = role.oid),
                       EXISTS (SELECT 1 FROM pg_proc routine
                               JOIN pg_namespace namespace ON namespace.oid = routine.pronamespace
                               WHERE namespace.nspname = 'mg' AND routine.proowner = role.oid),
                       has_database_privilege(current_user, current_database(), 'TEMPORARY'),
                       NOT EXISTS (
                           SELECT 1
                           FROM aclexplode(COALESCE(database.datacl, acldefault('d', database.datdba))) AS database_acl
                           WHERE database_acl.grantee = 0
                             AND database_acl.privilege_type IN ('CONNECT', 'CREATE', 'TEMP')
                       ),
                       NOT EXISTS (
                           SELECT 1
                           FROM pg_proc routine
                           JOIN pg_namespace namespace ON namespace.oid = routine.pronamespace
                           CROSS JOIN LATERAL aclexplode(
                               COALESCE(routine.proacl, acldefault('f', routine.proowner))) AS function_acl
                           WHERE namespace.nspname = 'mg'
                             AND function_acl.grantee = 0
                             AND function_acl.privilege_type = 'EXECUTE'
                       )
                FROM mg.tenant_identity identity
                JOIN mg.schema_migrations migration ON migration.version = @version
                JOIN pg_roles role ON role.rolname = current_user
                JOIN pg_database database ON database.datname = current_database()
                JOIN pg_namespace tenant_schema ON tenant_schema.nspname = 'mg'
                WHERE identity.singleton = TRUE;
                """, connection);
            command.Parameters.AddWithValue("version", NpgsqlDbType.Integer, expectedMigrationVersion);
            command.Parameters.AddWithValue("control_role", NpgsqlDbType.Text, controlRoleName);

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
                && reader.GetBoolean(40)
                && !reader.GetBoolean(41)
                && !reader.GetBoolean(42)
                && !reader.GetBoolean(43)
                && reader.GetBoolean(44)
                && reader.GetBoolean(45);
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

    private NpgsqlConnectionStringBuilder ValidateApplicationConnection(
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
            || !string.Equals(app.Host, controlHost, StringComparison.OrdinalIgnoreCase)
            || app.Port != controlPort
            || string.IsNullOrWhiteSpace(app.Password)
            || app.SslMode is not (SslMode.VerifyCA or SslMode.VerifyFull))
            throw new InvalidOperationException("Application database credential does not match the tenant identity or strict TLS policy.");

        return app;
    }

    private NpgsqlConnectionStringBuilder ValidateOwnerConnection(
        string ownerConnectionString, Guid tenantId, string expectedDatabase)
    {
        NpgsqlConnectionStringBuilder owner;
        try
        {
            owner = new NpgsqlConnectionStringBuilder(ownerConnectionString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("Tenant owner credential is malformed.");
        }

        if (!string.Equals(owner.Database, expectedDatabase, StringComparison.Ordinal)
            || !string.Equals(owner.Username, $"{expectedDatabase}_owner", StringComparison.Ordinal)
            || !string.Equals(owner.Host, controlHost, StringComparison.OrdinalIgnoreCase)
            || owner.Port != controlPort
            || string.IsNullOrWhiteSpace(owner.Password)
            || owner.SslMode is not (SslMode.VerifyCA or SslMode.VerifyFull))
            throw new InvalidOperationException("Tenant owner credential does not match its immutable identity or strict TLS policy.");

        return owner;
    }

    private async Task VerifyTenantOwnerConnectionAsync(
        NpgsqlConnection connection, Guid tenantId, string databaseName, CancellationToken cancellationToken)
    {
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
                   EXISTS (SELECT 1 FROM pg_auth_members membership
                           WHERE membership.member = role.oid),
                   EXISTS (SELECT 1 FROM pg_auth_members membership
                           WHERE membership.roleid = role.oid
                             AND pg_get_userbyid(membership.member) <> @control_role),
                   EXISTS (SELECT 1 FROM pg_auth_members membership
                           JOIN pg_roles control_role ON control_role.oid = membership.member
                           JOIN pg_roles grantor_role ON grantor_role.oid = membership.grantor
                           WHERE membership.roleid = role.oid
                             AND control_role.rolname = @control_role
                             AND grantor_role.rolname = @control_role
                             AND membership.set_option
                             AND NOT membership.inherit_option
                             AND NOT membership.admin_option),
                   EXISTS (SELECT 1 FROM pg_auth_members membership
                           JOIN pg_roles control_role ON control_role.oid = membership.member
                           JOIN pg_roles grantor_role ON grantor_role.oid = membership.grantor
                           WHERE membership.roleid = role.oid
                             AND control_role.rolname = @control_role
                             AND NOT (
                                 (grantor_role.rolsuper AND membership.admin_option
                                  AND NOT membership.inherit_option AND NOT membership.set_option)
                                 OR (grantor_role.rolname = @control_role AND membership.set_option
                                     AND NOT membership.inherit_option AND NOT membership.admin_option)
                             )),
                   COALESCE(shobj_description(role.oid, 'pg_authid'), ''),
                   COALESCE(shobj_description(database.oid, 'pg_database'), ''),
                   NOT EXISTS (
                       SELECT 1
                       FROM aclexplode(COALESCE(database.datacl, acldefault('d', database.datdba))) AS database_acl
                       WHERE database_acl.grantee = 0
                         AND database_acl.privilege_type IN ('CONNECT', 'CREATE', 'TEMP')
                   ),
                   NOT EXISTS (
                       SELECT 1
                       FROM pg_namespace tenant_schema
                       CROSS JOIN LATERAL aclexplode(
                           COALESCE(tenant_schema.nspacl, acldefault('n', tenant_schema.nspowner))) AS schema_acl
                       WHERE tenant_schema.nspname = 'mg'
                         AND schema_acl.grantee = 0
                         AND schema_acl.privilege_type IN ('USAGE', 'CREATE')
                   )
            FROM pg_roles role
            JOIN pg_database database ON database.datname = current_database()
            WHERE role.rolname = current_user;
            """, connection);
        command.Parameters.AddWithValue("control_role", NpgsqlDbType.Text, controlRoleName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)
            || !string.Equals(reader.GetString(0), OwnerRoleName(tenantId), StringComparison.Ordinal)
            || !string.Equals(reader.GetString(1), OwnerRoleName(tenantId), StringComparison.Ordinal)
            || !reader.GetBoolean(2)
            || reader.GetBoolean(3)
            || reader.GetBoolean(4)
            || reader.GetBoolean(5)
            || reader.GetBoolean(6)
            || reader.GetBoolean(7)
            || reader.GetBoolean(8)
            || reader.GetBoolean(9)
            || reader.GetBoolean(10)
            || !reader.GetBoolean(11)
            || reader.GetBoolean(12)
            || !string.Equals(reader.GetString(13), RoleMarker(tenantId, "owner"), StringComparison.Ordinal)
            || !string.Equals(reader.GetString(14), DatabaseMarker(tenantId), StringComparison.Ordinal)
            || !reader.GetBoolean(15)
            || !reader.GetBoolean(16)
            || await reader.ReadAsync(cancellationToken))
            throw new AgentProvisioningConflictException("Tenant owner database identity or restricted role verification failed.");
    }

    private async Task VerifyControlRoleAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken,
        NpgsqlTransaction? transaction = null)
    {
        // Re-read the full control-plane boundary immediately before each mutation.
        await using var command = new NpgsqlCommand(
            """
            SELECT role.rolcanlogin,
                   role.rolsuper,
                   role.rolcreatedb,
                   role.rolcreaterole,
                   role.rolreplication,
                   role.rolbypassrls,
                   role.rolinherit,
                   ledger.oid IS NOT NULL,
                   has_schema_privilege(current_user, schema.oid, 'USAGE'),
                   has_schema_privilege(current_user, schema.oid, 'CREATE'),
                   has_table_privilege(current_user, ledger.oid, 'SELECT'),
                   has_table_privilege(current_user, ledger.oid, 'INSERT'),
                   has_table_privilege(current_user, ledger.oid, 'UPDATE'),
                   has_table_privilege(current_user, ledger.oid, 'DELETE'),
                   has_table_privilege(current_user, ledger.oid, 'TRUNCATE'),
                   has_table_privilege(current_user, ledger.oid, 'REFERENCES'),
                   has_table_privilege(current_user, ledger.oid, 'TRIGGER'),
                   has_database_privilege(current_user, current_database(), 'CREATE'),
                   has_database_privilege(current_user, current_database(), 'TEMPORARY'),
                   pg_get_userbyid(database.datdba),
                   pg_get_userbyid(schema.nspowner),
                   pg_get_userbyid(ledger.relowner),
                   current_user,
                   NOT EXISTS (
                       SELECT 1
                       FROM pg_auth_members membership
                       WHERE membership.roleid = role.oid
                          OR (
                              membership.member = role.oid
                              AND NOT (
                                  (
                                      membership.admin_option
                                      AND NOT membership.inherit_option
                                      AND NOT membership.set_option
                                      AND EXISTS (
                                          SELECT 1
                                          FROM pg_roles grantor_role
                                          JOIN pg_roles target_role ON target_role.oid = membership.roleid
                                          WHERE grantor_role.oid = membership.grantor
                                            AND grantor_role.rolsuper
                                            AND target_role.rolname ~ '^mg_tenant_[0-9a-f]{32}_(owner|app)$'
                                            AND target_role.rolcanlogin
                                            AND NOT target_role.rolsuper
                                            AND NOT target_role.rolcreatedb
                                            AND NOT target_role.rolcreaterole
                                            AND NOT target_role.rolreplication
                                            AND NOT target_role.rolbypassrls
                                            AND NOT target_role.rolinherit
                                            AND shobj_description(target_role.oid, 'pg_authid') =
                                                'mg-tenant:' ||
                                                substring(target_role.rolname from '^mg_tenant_([0-9a-f]{32})_(owner|app)$') ||
                                                CASE WHEN target_role.rolname ~ '_owner$'
                                                     THEN ':owner:v1' ELSE ':application:v1' END
                                      )
                                  )
                                  OR
                                  (
                                      membership.set_option
                                      AND NOT membership.inherit_option
                                      AND NOT membership.admin_option
                                      AND EXISTS (
                                          SELECT 1
                                          FROM pg_roles grantor_role
                                          JOIN pg_roles owner_role ON owner_role.oid = membership.roleid
                                          WHERE grantor_role.oid = membership.grantor
                                            AND grantor_role.oid = role.oid
                                            AND owner_role.rolname ~ '^mg_tenant_[0-9a-f]{32}_owner$'
                                            AND owner_role.rolcanlogin
                                            AND NOT owner_role.rolsuper
                                            AND NOT owner_role.rolcreatedb
                                            AND NOT owner_role.rolcreaterole
                                            AND NOT owner_role.rolreplication
                                            AND NOT owner_role.rolbypassrls
                                            AND NOT owner_role.rolinherit
                                            AND shobj_description(owner_role.oid, 'pg_authid') =
                                                'mg-tenant:' ||
                                                substring(owner_role.rolname from '^mg_tenant_([0-9a-f]{32})_owner$') ||
                                                ':owner:v1'
                                      )
                                  )
                              )
                          )
                   ),
                   NOT EXISTS (
                       SELECT 1
                       FROM aclexplode(COALESCE(database.datacl, acldefault('d', database.datdba))) AS database_acl
                       WHERE database_acl.grantee = 0
                         AND database_acl.privilege_type IN ('CONNECT', 'CREATE', 'TEMP')
                   ),
                   NOT EXISTS (
                       SELECT 1
                       FROM aclexplode(COALESCE(schema.nspacl, acldefault('n', schema.nspowner))) AS schema_acl
                       WHERE schema_acl.grantee = 0
                         AND schema_acl.privilege_type IN ('USAGE', 'CREATE')
                   )
            FROM pg_roles role
            JOIN pg_database database ON database.datname = current_database()
            JOIN pg_namespace schema ON schema.nspname = 'mg_agent'
            JOIN pg_class ledger ON ledger.relnamespace = schema.oid AND ledger.relname = 'provisioning_operations'
            WHERE role.rolname = current_user;
            """, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)
            || !reader.GetBoolean(0)
            || reader.GetBoolean(1)
            || !reader.GetBoolean(2)
            || !reader.GetBoolean(3)
            || reader.GetBoolean(4)
            || reader.GetBoolean(5)
            || reader.GetBoolean(6)
            || !reader.GetBoolean(7)
            || !reader.GetBoolean(8)
            || reader.GetBoolean(9)
            || !reader.GetBoolean(10)
            || !reader.GetBoolean(11)
            || !reader.GetBoolean(12)
            || reader.GetBoolean(13)
            || reader.GetBoolean(14)
            || reader.GetBoolean(15)
            || reader.GetBoolean(16)
            || reader.GetBoolean(17)
            || reader.GetBoolean(18)
            || string.Equals(reader.GetString(19), reader.GetString(22), StringComparison.Ordinal)
            || string.Equals(reader.GetString(20), reader.GetString(22), StringComparison.Ordinal)
            || string.Equals(reader.GetString(21), reader.GetString(22), StringComparison.Ordinal)
            || !reader.GetBoolean(23)
            || !reader.GetBoolean(24)
            || !reader.GetBoolean(25))
            throw new InvalidOperationException("Agent control-role membership, ACL, ownership or privilege boundary is not ready.");
    }

    private async Task EnsureRoleAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string roleName,
        string marker,
        bool login,
        string? password,
        CancellationToken cancellationToken)
    {
        var state = await GetRoleStateAsync(connection, roleName, cancellationToken, transaction);
        if (state is null)
        {
            await VerifyControlRoleAsync(connection, cancellationToken, transaction);
            var sql = login
                ? await FormatSqlAsync(connection, transaction,
                    "SELECT format('CREATE ROLE %I WITH LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', @role, @password);",
                    cancellationToken, ("role", roleName), ("password", password!))
                : await FormatSqlAsync(connection, transaction,
                    "SELECT format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', @role);",
                    cancellationToken, ("role", roleName));
            await ExecuteDdlAsync(connection, sql, cancellationToken, transaction);
            var commentSql = await FormatSqlAsync(connection, transaction,
                "SELECT format('COMMENT ON ROLE %I IS %L', @role, @comment);",
                cancellationToken, ("role", roleName), ("comment", marker));
            await ExecuteDdlAsync(connection, commentSql, cancellationToken, transaction);
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
            || (login && (state.MemberOfAnyRole || state.MemberOfUnexpectedRole))
            || (!login && state.MemberOfUnexpectedRole))
            throw new AgentProvisioningConflictException("Existing tenant role has an unexpected identity, privilege or membership state.");

        if (login)
        {
            await VerifyControlRoleAsync(connection, cancellationToken, transaction);
            var alterSql = await FormatSqlAsync(connection, transaction,
                "SELECT format('ALTER ROLE %I WITH PASSWORD %L', @role, @password);",
                cancellationToken, ("role", roleName), ("password", password!));
            await ExecuteDdlAsync(connection, alterSql, cancellationToken, transaction);
        }
    }

    private async Task EnsureOwnerRoleMembershipAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string ownerRole,
        CancellationToken cancellationToken)
    {
        var state = await GetRoleStateAsync(connection, ownerRole, cancellationToken, transaction)
            ?? throw new InvalidOperationException("Tenant owner role was not created.");
        if (state.MemberOfAnyRole || state.MemberOfUnexpectedRole)
            throw new AgentProvisioningConflictException("Tenant owner role has an unexpected role membership.");

        // PostgreSQL 17 creates an immutable bootstrap-superuser ADMIN membership for
        // the CREATEROLE identity that creates a role (INHERIT FALSE, SET FALSE).
        // Add a separate grantor-scoped SET-only path; never inherit owner privileges.
        await using var check = new NpgsqlCommand(
            """
            SELECT EXISTS (
                       SELECT 1
                       FROM pg_auth_members membership
                       JOIN pg_roles granted_role ON granted_role.oid = membership.roleid
                       JOIN pg_roles member_role ON member_role.oid = membership.member
                       JOIN pg_roles grantor_role ON grantor_role.oid = membership.grantor
                       WHERE granted_role.rolname = @owner_role
                         AND member_role.rolname = @control_role
                         AND grantor_role.rolname = @control_role
                         AND membership.set_option
                         AND NOT membership.inherit_option
                         AND NOT membership.admin_option
                   ),
                   EXISTS (
                       SELECT 1
                       FROM pg_auth_members membership
                       JOIN pg_roles granted_role ON granted_role.oid = membership.roleid
                       JOIN pg_roles member_role ON member_role.oid = membership.member
                       JOIN pg_roles grantor_role ON grantor_role.oid = membership.grantor
                       WHERE granted_role.rolname = @owner_role
                         AND member_role.rolname = @control_role
                         AND NOT (
                             (grantor_role.rolsuper AND membership.admin_option
                              AND NOT membership.inherit_option AND NOT membership.set_option)
                             OR (grantor_role.rolname = @control_role AND membership.set_option
                                 AND NOT membership.inherit_option AND NOT membership.admin_option)
                         )
                   );
            """, connection, transaction);
        check.Parameters.AddWithValue("owner_role", NpgsqlDbType.Text, ownerRole);
        check.Parameters.AddWithValue("control_role", NpgsqlDbType.Text, controlRoleName);
        await using var checkReader = await check.ExecuteReaderAsync(cancellationToken);
        await checkReader.ReadAsync(cancellationToken);
        var hasSetOnlyMembership = checkReader.GetBoolean(0);
        var hasUnsafeMembership = checkReader.GetBoolean(1);
        await checkReader.CloseAsync();
        if (hasUnsafeMembership)
            throw new AgentProvisioningConflictException("Tenant owner role control membership has unsafe INHERIT, SET, ADMIN or grantor options.");
        if (hasSetOnlyMembership)
            return;

        await VerifyControlRoleAsync(connection, cancellationToken, transaction);
        var grantSql = await FormatSqlAsync(connection, transaction,
            "SELECT format('GRANT %I TO %I WITH INHERIT FALSE, SET TRUE', @role, @member);",
            cancellationToken, ("role", ownerRole), ("member", controlRoleName));
        await ExecuteDdlAsync(connection, grantSql, cancellationToken, transaction);

        await using var verify = new NpgsqlCommand(
            """
            SELECT EXISTS (
                       SELECT 1
                       FROM pg_auth_members membership
                       JOIN pg_roles granted_role ON granted_role.oid = membership.roleid
                       JOIN pg_roles member_role ON member_role.oid = membership.member
                       JOIN pg_roles grantor_role ON grantor_role.oid = membership.grantor
                       WHERE granted_role.rolname = @owner_role
                         AND member_role.rolname = @control_role
                         AND grantor_role.rolname = @control_role
                         AND membership.set_option
                         AND NOT membership.inherit_option
                         AND NOT membership.admin_option
                   ),
                   EXISTS (
                       SELECT 1
                       FROM pg_auth_members membership
                       JOIN pg_roles granted_role ON granted_role.oid = membership.roleid
                       JOIN pg_roles member_role ON member_role.oid = membership.member
                       JOIN pg_roles grantor_role ON grantor_role.oid = membership.grantor
                       WHERE granted_role.rolname = @owner_role
                         AND member_role.rolname = @control_role
                         AND NOT (
                             (grantor_role.rolsuper AND membership.admin_option
                              AND NOT membership.inherit_option AND NOT membership.set_option)
                             OR (grantor_role.rolname = @control_role AND membership.set_option
                                 AND NOT membership.inherit_option AND NOT membership.admin_option)
                         )
                   );
            """, connection, transaction);
        verify.Parameters.AddWithValue("owner_role", NpgsqlDbType.Text, ownerRole);
        verify.Parameters.AddWithValue("control_role", NpgsqlDbType.Text, controlRoleName);
        await using var verifyReader = await verify.ExecuteReaderAsync(cancellationToken);
        await verifyReader.ReadAsync(cancellationToken);
        var verifiedSetOnlyMembership = verifyReader.GetBoolean(0);
        var verifiedUnsafeMembership = verifyReader.GetBoolean(1);
        await verifyReader.CloseAsync();
        if (!verifiedSetOnlyMembership || verifiedUnsafeMembership)
            throw new AgentProvisioningConflictException("Tenant owner role lacks the required isolated SET-only membership path.");
    }

    private async Task<RoleState?> GetRoleStateAsync(
        NpgsqlConnection connection, string roleName, CancellationToken cancellationToken,
        NpgsqlTransaction? transaction = null)
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
            """, connection, transaction);
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

    private async Task EnsureDatabaseAsync(
        NpgsqlConnection controlConnection,
        Guid tenantId,
        string databaseName,
        string ownerRole,
        string ownerConnectionString,
        string appRole,
        CancellationToken cancellationToken)
    {
        var state = await GetDatabaseStateAsync(controlConnection, databaseName, cancellationToken);
        if (state is null)
        {
            await VerifyControlRoleAsync(controlConnection, cancellationToken);
            var createSql = await FormatSqlAsync(controlConnection,
                "SELECT format('CREATE DATABASE %I OWNER %I', @database, @owner);",
                cancellationToken, ("database", databaseName), ("owner", ownerRole));
            await ExecuteDdlAsync(controlConnection, createSql, cancellationToken);
            await SetDatabaseMarkerAsync(ownerConnectionString, tenantId, databaseName, cancellationToken);
            state = await GetDatabaseStateAsync(controlConnection, databaseName, cancellationToken);
        }
        else
        {
            if (!string.Equals(state.Owner, ownerRole, StringComparison.Ordinal))
                throw new AgentProvisioningConflictException("Existing tenant database owner does not match the immutable tenant owner role.");

            if (string.IsNullOrEmpty(state.Comment))
            {
                // Recover only the crash window after CREATE DATABASE and before its marker.
                if (!await IsFreshDatabaseAsync(databaseName, cancellationToken))
                    throw new AgentProvisioningConflictException("Unmarked tenant database is not empty and cannot be safely adopted.");
                await SetDatabaseMarkerAsync(ownerConnectionString, tenantId, databaseName, cancellationToken);
                state = await GetDatabaseStateAsync(controlConnection, databaseName, cancellationToken);
            }
            else if (!string.Equals(state.Comment, DatabaseMarker(tenantId), StringComparison.Ordinal))
            {
                throw new AgentProvisioningConflictException("Existing tenant database immutable marker does not match.");
            }
        }

        if (state is null
            || !string.Equals(state.Owner, ownerRole, StringComparison.Ordinal)
            || !string.Equals(state.Comment, DatabaseMarker(tenantId), StringComparison.Ordinal))
            throw new AgentProvisioningConflictException("Tenant database owner or immutable database marker verification failed.");

        await ConfigureDatabaseAccessAsync(ownerConnectionString, databaseName, appRole, cancellationToken);
    }

    private async Task<bool> IsFreshDatabaseAsync(string databaseName, CancellationToken cancellationToken)
    {
        var builder = new NpgsqlConnectionStringBuilder(controlConnectionString)
        {
            Database = databaseName,
            Pooling = false
        };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT
                NOT EXISTS (
                    SELECT 1 FROM pg_namespace
                    WHERE nspname NOT IN ('pg_catalog', 'information_schema', 'public')
                      AND nspname NOT LIKE 'pg_toast%'
                )
                AND NOT EXISTS (
                    SELECT 1
                    FROM pg_class relation
                    JOIN pg_namespace namespace ON namespace.oid = relation.relnamespace
                    WHERE namespace.nspname = 'public'
                )
                AND NOT EXISTS (
                    SELECT 1
                    FROM pg_proc routine
                    JOIN pg_namespace namespace ON namespace.oid = routine.pronamespace
                    WHERE namespace.nspname = 'public'
                )
                AND NOT EXISTS (
                    SELECT 1
                    FROM pg_type type
                    JOIN pg_namespace namespace ON namespace.oid = type.typnamespace
                    WHERE namespace.nspname = 'public'
                )
                AND NOT EXISTS (
                    SELECT 1
                    FROM pg_extension extension
                    JOIN pg_namespace namespace ON namespace.oid = extension.extnamespace
                    WHERE namespace.nspname = 'public'
                )
                AND NOT EXISTS (SELECT 1 FROM pg_largeobject_metadata)
                AND NOT EXISTS (SELECT 1 FROM pg_default_acl);
            """, connection);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private async Task SetDatabaseMarkerAsync(
        string ownerConnectionString, Guid tenantId, string databaseName, CancellationToken cancellationToken)
    {
        var builder = new NpgsqlConnectionStringBuilder(ownerConnectionString) { Pooling = false };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("COMMENT ON DATABASE " + QuoteIdentifier(databaseName)
            + " IS '" + DatabaseMarker(tenantId) + "';", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
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
        string ownerConnectionString, string databaseName, string appRole, CancellationToken cancellationToken)
    {
        var builder = new NpgsqlConnectionStringBuilder(ownerConnectionString) { Pooling = false };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var sql = await FormatSqlAsync(connection,
            """
            SELECT format(
                'REVOKE ALL ON DATABASE %I FROM PUBLIC; GRANT CONNECT ON DATABASE %I TO %I; GRANT CONNECT ON DATABASE %I TO %I;',
                @database, @database, @app_role, @database, @control_role);
            """, cancellationToken, ("database", databaseName), ("app_role", appRole), ("control_role", controlRoleName));
        await ExecuteDdlAsync(connection, sql, cancellationToken);
    }

    private static async Task SetTenantMigrationContextAsync(
        NpgsqlConnection connection, Guid tenantId, string checksum, CancellationToken cancellationToken)
    {
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
            throw new AgentProvisioningConflictException("Tenant database identity or baseline migration checksum does not match the reviewed artifact.");
    }

    private async Task ApplyRuntimeGrantsAsync(
        NpgsqlConnection connection, string appRole, CancellationToken cancellationToken)
    {
        var grantSql = await FormatSqlAsync(connection,
            """
            SELECT format(
                'REVOKE ALL ON SCHEMA mg FROM PUBLIC;
                 REVOKE ALL ON ALL TABLES IN SCHEMA mg FROM PUBLIC;
                 REVOKE ALL ON ALL FUNCTIONS IN SCHEMA mg FROM PUBLIC;
                 ALTER DEFAULT PRIVILEGES REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC;
                 GRANT USAGE ON SCHEMA mg TO %I;
                 GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE mg.business_settings, mg.users, mg.customers, mg.user_roles, mg.role_permissions, mg.outbox_messages TO %I;
                 GRANT SELECT ON TABLE mg.roles, mg.permissions, mg.tenant_identity, mg.schema_migrations, mg.audit_events TO %I;',
                @app_role, @app_role, @app_role, @app_role);
            """, cancellationToken, ("app_role", appRole));
        await ExecuteDdlAsync(connection, grantSql, cancellationToken);
    }

    private static Task<string> FormatSqlAsync(
        NpgsqlConnection connection,
        string formatCommand,
        CancellationToken cancellationToken,
        params (string Name, string Value)[] parameters) =>
        FormatSqlAsync(connection, null, formatCommand, cancellationToken, parameters);

    private static async Task<string> FormatSqlAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string formatCommand,
        CancellationToken cancellationToken,
        params (string Name, string Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(formatCommand, connection, transaction);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, NpgsqlDbType.Text, value);

        var sql = await command.ExecuteScalarAsync(cancellationToken) as string;
        return sql ?? throw new InvalidOperationException("Unable to format a safe PostgreSQL provisioning command.");
    }

    private static Task ExecuteDdlAsync(
        NpgsqlConnection connection, string sql, CancellationToken cancellationToken) =>
        ExecuteDdlAsync(connection, sql, cancellationToken, null);

    private static async Task ExecuteDdlAsync(
        NpgsqlConnection connection, string sql, CancellationToken cancellationToken,
        NpgsqlTransaction? transaction)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
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