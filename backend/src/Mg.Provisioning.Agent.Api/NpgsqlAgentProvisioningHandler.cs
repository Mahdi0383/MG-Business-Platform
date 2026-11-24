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
            return await AgentControlRoleReadinessVerifier.IsReadyAsync(connection, cancellationToken);
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
