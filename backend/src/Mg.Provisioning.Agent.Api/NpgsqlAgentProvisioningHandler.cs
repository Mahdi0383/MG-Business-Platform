using System.Security.Cryptography;
using Mg.Provisioning.Agent.Core;
using Npgsql;
using NpgsqlTypes;

namespace Mg.Provisioning.Agent.Api;

/// <summary>
/// Connects the mTLS HTTP boundary to the isolated agent core. The agent ledger migration
/// must already be applied and the control role must have only the reviewed privileges.
/// </summary>
public sealed class NpgsqlAgentProvisioningHandler : IAgentProvisioningHandler, IDisposable
{
    private readonly string controlConnectionString;
    private readonly NpgsqlDataSource dataSource;
    private readonly AgentCredentialProtector credentialProtector;
    private readonly AgentProvisioningCoordinator coordinator;
    private readonly AgentProvisioningOperationStore operationStore;
    private bool disposed;

    private NpgsqlAgentProvisioningHandler(
        string controlConnectionString,
        NpgsqlDataSource dataSource,
        AgentCredentialProtector credentialProtector,
        AgentProvisioningCoordinator coordinator,
        AgentProvisioningOperationStore operationStore)
    {
        this.controlConnectionString = controlConnectionString;
        this.dataSource = dataSource;
        this.credentialProtector = credentialProtector;
        this.coordinator = coordinator;
        this.operationStore = operationStore;
    }

    public static NpgsqlAgentProvisioningHandler? TryCreate(
        IConfiguration configuration,
        string applicationBaseDirectory)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var controlConnectionString = configuration["Agent:ControlDatabase:ConnectionString"];
        var currentKeyId = configuration["Agent:CredentialEncryption:CurrentKeyId"];
        var keys = configuration.GetSection("Agent:CredentialEncryption:Keys")
            .GetChildren()
            .Where(child => !string.IsNullOrWhiteSpace(child.Value))
            .ToDictionary(child => child.Key, child => child.Value!, StringComparer.Ordinal);

        var anyConfigured = !string.IsNullOrWhiteSpace(controlConnectionString)
            || !string.IsNullOrWhiteSpace(currentKeyId)
            || keys.Count > 0;
        if (!anyConfigured)
            return null;

        if (string.IsNullOrWhiteSpace(controlConnectionString)
            || string.IsNullOrWhiteSpace(currentKeyId)
            || keys.Count == 0)
            throw new InvalidOperationException(
                "Agent provisioning requires a control database connection and a complete credential encryption key ring.");

        var parsed = new NpgsqlConnectionStringBuilder(controlConnectionString);
        if (string.IsNullOrWhiteSpace(parsed.Host)
            || string.IsNullOrWhiteSpace(parsed.Database)
            || string.IsNullOrWhiteSpace(parsed.Username)
            || string.IsNullOrWhiteSpace(parsed.Password)
            || parsed.SslMode is not (SslMode.VerifyCA or SslMode.VerifyFull))
            throw new InvalidOperationException(
                "Agent control database connection must use strict TLS and complete credentials.");

        parsed.Pooling = false;
        var normalizedControlConnectionString = parsed.ConnectionString;
        var migration = TenantSchemaMigrationArtifact.LoadTenantCoreBaseline(applicationBaseDirectory);
        var protector = new AgentCredentialProtector(currentKeyId, keys);
        NpgsqlDataSource? dataSource = null;

        try
        {
            dataSource = NpgsqlDataSource.Create(normalizedControlConnectionString);
            var store = new AgentProvisioningOperationStore(dataSource, protector);
            var provisioner = new NpgsqlTenantResourceProvisioner(normalizedControlConnectionString);
            var coordinator = new AgentProvisioningCoordinator(store, provisioner, migration);
            return new NpgsqlAgentProvisioningHandler(
                normalizedControlConnectionString, dataSource, protector, coordinator, store);
        }
        catch
        {
            dataSource?.Dispose();
            protector.Dispose();
            throw;
        }
    }

    public async Task<string> ProvisionAsync(
        AgentProvisioningRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        // Revalidate the live control-role boundary immediately before resource changes.
        if (!await IsReadyAsync(cancellationToken))
            throw new InvalidOperationException("Agent provisioning dependencies are not ready.");

        return await coordinator.ProvisionAsync(
            request,
            token =>
            {
                token.ThrowIfCancellationRequested();
                var databaseName = $"mg_tenant_{request.TenantId:N}";
                var appBuilder = new NpgsqlConnectionStringBuilder(controlConnectionString)
                {
                    Database = databaseName,
                    Username = $"{databaseName}_app",
                    Password = GenerateCredentialPassword(),
                    Pooling = true
                };
                var ownerBuilder = new NpgsqlConnectionStringBuilder(controlConnectionString)
                {
                    Database = databaseName,
                    Username = $"{databaseName}_owner",
                    Password = GenerateCredentialPassword(),
                    Pooling = false
                };
                return ValueTask.FromResult(new AgentProvisioningCredentials(
                    appBuilder.ConnectionString, ownerBuilder.ConnectionString));
            },
            cancellationToken);
    }

    public async Task<int> RewrapStoredCredentialsAsync(
        int batchSize, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        // Rewrapping mutates the protected ledger and must honor the same live gate.
        if (!await IsReadyAsync(cancellationToken))
            throw new InvalidOperationException("Agent provisioning dependencies are not ready.");

        return await operationStore.RewrapStoredCredentialsAsync(batchSize, cancellationToken);
    }

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                """
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
                       current_user
                FROM pg_roles role
                JOIN pg_database database ON database.datname = current_database()
                JOIN pg_namespace schema ON schema.nspname = 'mg_agent'
                JOIN pg_class ledger ON ledger.oid = to_regclass('mg_agent.provisioning_operations')
                WHERE role.rolname = current_user;
                """, connection);
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
                && reader.GetBoolean(21);
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

    public void Dispose()
    {
        if (disposed) return;
        dataSource.Dispose();
        credentialProtector.Dispose();
        disposed = true;
    }

    private static string GenerateCredentialPassword() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
