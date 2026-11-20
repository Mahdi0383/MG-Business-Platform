using Npgsql;

namespace Mg.Provisioning.Agent.Core;

/// <summary>
/// Re-evaluates the complete live control-role boundary on the caller's PostgreSQL
/// connection. Call immediately before each privileged provisioning or ledger write.
/// </summary>
public static class AgentControlRoleReadinessVerifier
{
    private const string ReadinessSql = """
                SELECT role.rolcanlogin,
                       role.rolsuper,
                       role.rolcreatedb,
                       role.rolcreaterole,
                       role.rolreplication,
                       role.rolbypassrls,
                       role.rolinherit,
                       to_regclass('mg_agent.provisioning_operations') IS NOT NULL,
                       has_schema_privilege(current_user, 'mg_agent', 'USAGE'),
                       has_schema_privilege(current_user, 'mg_agent', 'CREATE'),
                       has_table_privilege(current_user, 'mg_agent.provisioning_operations', 'SELECT'),
                       has_table_privilege(current_user, 'mg_agent.provisioning_operations', 'INSERT'),
                       has_table_privilege(current_user, 'mg_agent.provisioning_operations', 'UPDATE'),
                       has_table_privilege(current_user, 'mg_agent.provisioning_operations', 'DELETE'),
                       has_table_privilege(current_user, 'mg_agent.provisioning_operations', 'TRUNCATE'),
                       has_table_privilege(current_user, 'mg_agent.provisioning_operations', 'REFERENCES'),
                       has_table_privilege(current_user, 'mg_agent.provisioning_operations', 'TRIGGER'),
                       has_database_privilege(current_user, current_database(), 'CREATE'),
                       pg_get_userbyid(database.datdba),
                       pg_get_userbyid(schema.nspowner),
                       pg_get_userbyid(ledger.relowner),
                       NOT EXISTS (
                           SELECT 1
                           FROM pg_auth_members membership
                           WHERE membership.roleid = role.oid
                              OR (membership.member = role.oid
                                  AND NOT (
                                      EXISTS (
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
                                      AND membership.admin_option
                                      AND NOT membership.inherit_option
                                      AND NOT membership.set_option
                                  )
                                  AND NOT (
                                      EXISTS (
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
                                      AND membership.set_option
                                      AND NOT membership.inherit_option
                                      AND NOT membership.admin_option
                                  ))
                       ),
                       current_user,
                       has_database_privilege(current_user, current_database(), 'TEMPORARY'),
                       NOT EXISTS (
                           SELECT 1
                           FROM aclexplode(COALESCE(database.datacl, acldefault('d', database.datdba))) AS database_acl
                           WHERE database_acl.grantee = 0
                             AND database_acl.privilege_type IN ('CONNECT', 'CREATE', 'TEMP')
                       )
                FROM pg_roles role
                JOIN pg_database database ON database.datname = current_database()
                JOIN pg_namespace schema ON schema.nspname = 'mg_agent'
                JOIN pg_class ledger ON ledger.oid = to_regclass('mg_agent.provisioning_operations')
                WHERE role.rolname = current_user;
                """;

    public static async Task<bool> IsReadyAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken,
        NpgsqlTransaction? transaction = null)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = new NpgsqlCommand(ReadinessSql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return false;

        return reader.GetBoolean(0)
                        && !reader.GetBoolean(1)
                        && reader.GetBoolean(2)
                        && reader.GetBoolean(3)
                        && !reader.GetBoolean(4)
                        && !reader.GetBoolean(5)
                        && !reader.GetBoolean(6)
                        && reader.GetBoolean(7)
                        && reader.GetBoolean(8)
                        && !reader.GetBoolean(9)
                        && reader.GetBoolean(10)
                        && reader.GetBoolean(11)
                        && reader.GetBoolean(12)
                        && !reader.GetBoolean(13)
                        && !reader.GetBoolean(14)
                        && !reader.GetBoolean(15)
                        && !reader.GetBoolean(16)
                        && !reader.GetBoolean(17)
                        && !string.Equals(reader.GetString(18), reader.GetString(22), StringComparison.Ordinal)
                        && !string.Equals(reader.GetString(19), reader.GetString(22), StringComparison.Ordinal)
                        && !string.Equals(reader.GetString(20), reader.GetString(22), StringComparison.Ordinal)
                        && reader.GetBoolean(21)
                        && !reader.GetBoolean(23)
                        && reader.GetBoolean(24);
    }

    public static async Task EnsureReadyAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken,
        NpgsqlTransaction? transaction = null)
    {
        if (!await IsReadyAsync(connection, cancellationToken, transaction))
            throw new InvalidOperationException(
                "Agent control-role readiness verification failed; privileged mutation was refused.");
    }
}
