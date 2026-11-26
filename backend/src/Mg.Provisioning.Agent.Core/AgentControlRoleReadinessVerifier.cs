using Npgsql;

namespace Mg.Provisioning.Agent.Core;

/// <summary>
/// Shared live verifier for the isolated agent control identity. Invoke immediately
/// before privileged tenant-resource changes and durable ledger mutations.
/// </summary>
public static class AgentControlRoleReadinessVerifier
{
    public static async Task<bool> IsReadyAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken,
        NpgsqlTransaction? transaction = null)
    {
        try
        {
            await EnsureReadyAsync(connection, cancellationToken, transaction);
            return true;
        }
        catch (NpgsqlException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static async Task EnsureReadyAsync(
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
                         AND database_acl.privilege_type IN ('CONNECT', 'CREATE', 'TEMPORARY')
                   ),
                   NOT EXISTS (
                       SELECT 1
                       FROM aclexplode(COALESCE(schema.nspacl, acldefault('n', schema.nspowner))) AS schema_acl
                       WHERE schema_acl.grantee = 0
                         AND schema_acl.privilege_type IN ('USAGE', 'CREATE')
                   ),
                   NOT EXISTS (
                       SELECT 1
                       FROM aclexplode(COALESCE(ledger.relacl, acldefault('r', ledger.relowner))) AS ledger_acl
                       WHERE ledger_acl.grantee = 0
                         AND ledger_acl.privilege_type IN
                             ('SELECT', 'INSERT', 'UPDATE', 'DELETE', 'TRUNCATE', 'REFERENCES', 'TRIGGER')
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
            || !reader.GetBoolean(25)
            || !reader.GetBoolean(26))
            throw new InvalidOperationException("Agent control-role membership, ACL, ownership or privilege boundary is not ready.");
    }
}
