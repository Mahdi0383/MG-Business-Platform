using System.Security.Cryptography;
using Mg.Provisioning.Agent.Core;
using Npgsql;
using Xunit;

namespace Mg.Platform.Api.Tests;

public sealed class AgentProvisioningOperationStorePostgresTests
{
    [Fact]
    public async Task CoordinatorExecutesStagesAndReplaysVerifiedCredentialWithoutRepeatingSideEffects()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenantId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();
        var provisioner = new FakeResourceProvisioner();
        var coordinator = new AgentProvisioningCoordinator(fixture.Store, provisioner, LoadMigration());
        var generated = 0;
        ValueTask<AgentProvisioningCredentials> CredentialFactory(CancellationToken _) 
        {
            Interlocked.Increment(ref generated);
            return ValueTask.FromResult(Fixture.Credentials($"mg_tenant_{tenantId:N}", "coordinator-secret"));
        }

        var first = await coordinator.ProvisionAsync(new AgentProvisioningRequest(tenantId, idempotencyKey), CredentialFactory);
        var replay = await coordinator.ProvisionAsync(new AgentProvisioningRequest(tenantId, idempotencyKey), CredentialFactory);

        Assert.Equal(first, replay);
        Assert.Equal(1, generated);
        Assert.Equal(1, provisioner.EnsureCalls);
        Assert.Equal(1, provisioner.MigrationCalls);
        Assert.Equal(1, provisioner.VerifyCalls);
        Assert.Equal("Verified", await fixture.Store.GetPhaseAsync(tenantId, idempotencyKey));
    }

    [Fact]
    public async Task CoordinatorResumesFromLastCommittedPhaseAfterExternalStageFailure()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenantId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();
        var provisioner = new FakeResourceProvisioner { FailMigrationOnce = true };
        var coordinator = new AgentProvisioningCoordinator(fixture.Store, provisioner, LoadMigration());
        ValueTask<AgentProvisioningCredentials> CredentialFactory(CancellationToken _) =>
            ValueTask.FromResult(Fixture.Credentials($"mg_tenant_{tenantId:N}", "replay-secret"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ProvisionAsync(new AgentProvisioningRequest(tenantId, idempotencyKey), CredentialFactory));
        Assert.Equal("DatabaseCreated", await fixture.Store.GetPhaseAsync(tenantId, idempotencyKey));

        var recovered = await coordinator.ProvisionAsync(
            new AgentProvisioningRequest(tenantId, idempotencyKey), CredentialFactory);

        Assert.Contains("replay-secret", recovered, StringComparison.Ordinal);
        Assert.Equal(1, provisioner.EnsureCalls);
        Assert.Equal(2, provisioner.MigrationCalls);
        Assert.Equal(1, provisioner.VerifyCalls);
        Assert.Equal("Verified", await fixture.Store.GetPhaseAsync(tenantId, idempotencyKey));
    }

    [Fact]
    public async Task CoordinatorRejectsConcurrentTenantOperationAndReleasesLockAfterCompletion()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenantId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();
        var enteredProvisioning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProvisioning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provisioner = new FakeResourceProvisioner
        {
            OnEnsure = async token =>
            {
                enteredProvisioning.TrySetResult();
                await releaseProvisioning.Task.WaitAsync(token);
            }
        };
        var coordinator = new AgentProvisioningCoordinator(fixture.Store, provisioner, LoadMigration());
        ValueTask<AgentProvisioningCredentials> CredentialFactory(CancellationToken _) =>
            ValueTask.FromResult(Fixture.Credentials($"mg_tenant_{tenantId:N}", "exclusive-secret"));

        var firstTask = coordinator.ProvisionAsync(
            new AgentProvisioningRequest(tenantId, idempotencyKey), CredentialFactory);
        await enteredProvisioning.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.ThrowsAsync<AgentProvisioningBusyException>(() =>
            coordinator.ProvisionAsync(new AgentProvisioningRequest(tenantId, idempotencyKey), CredentialFactory));

        releaseProvisioning.TrySetResult();
        await firstTask;

        var replay = await coordinator.ProvisionAsync(
            new AgentProvisioningRequest(tenantId, idempotencyKey), CredentialFactory);
        Assert.Contains("exclusive-secret", replay, StringComparison.Ordinal);
    }

    [Fact]
    public void MigrationLoaderRejectsMissingPackagedArtifact()
    {
        var missingDirectory = Path.Combine(Path.GetTempPath(), "mg-agent-missing-" + Guid.NewGuid().ToString("N"));
        Assert.Throws<FileNotFoundException>(() =>
            TenantSchemaMigrationArtifact.LoadTenantCoreBaseline(missingDirectory));
    }

    private static TenantSchemaMigrationArtifact LoadMigration() =>
        TenantSchemaMigrationArtifact.LoadTenantCoreBaseline(AppContext.BaseDirectory);

    [Fact]
    public void PackagedMigrationArtifactReturnsDefensiveSqlCopies()
    {
        var artifact = TenantSchemaMigrationArtifact.LoadTenantCoreBaseline(AppContext.BaseDirectory);
        var original = artifact.GetSqlBytes().ToArray();
        var expectedHash = Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant();
        original[0] ^= 0x20;

        Assert.Equal(expectedHash, artifact.Sha256);
        Assert.NotEqual(original[0], artifact.GetSqlBytes().Span[0]);
    }

    [Fact]
    public async Task ConcurrentCrossTenantIdempotencyReuseAllowsExactlyOneReservation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var idempotencyKey = Guid.NewGuid();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        async Task<bool> Reserve(Guid tenantId)
        {
            var databaseName = $"mg_tenant_{tenantId:N}";
            try
            {
                await fixture.Store.GetOrCreatePreparedCredentialsAsync(
                    tenantId, idempotencyKey, databaseName,
                    _ => ValueTask.FromResult(Fixture.Credentials(databaseName, "one-winner-secret")));
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(Reserve(tenantA), Reserve(tenantB));
        Assert.Single(results, value => value);
        Assert.Single(results, value => !value);
    }

    [Fact]
    public async Task SameRequestReplaysStableCredentialAndAllowsOnlyForwardPhaseTransitions()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenantId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();
        var databaseName = $"mg_tenant_{tenantId:N}";
        var generated = 0;

        var first = await fixture.Store.GetOrCreatePreparedCredentialsAsync(
            tenantId, idempotencyKey, databaseName,
            _ =>
            {
                Interlocked.Increment(ref generated);
                return ValueTask.FromResult(Fixture.Credentials(databaseName, "stable-secret"));
            });

        var replay = await fixture.Store.GetOrCreatePreparedCredentialsAsync(
            tenantId, idempotencyKey, databaseName,
            _ =>
            {
                Interlocked.Increment(ref generated);
                return ValueTask.FromResult(Fixture.Credentials(databaseName, "should-not-be-used"));
            });

        Assert.Equal(first, replay);
        Assert.Equal(1, generated);

        await fixture.Store.AdvancePhaseAsync(tenantId, idempotencyKey, "Prepared", "DatabaseCreated");
        await fixture.Store.AdvancePhaseAsync(tenantId, idempotencyKey, "DatabaseCreated", "SchemaApplied");
        await fixture.Store.AdvancePhaseAsync(tenantId, idempotencyKey, "SchemaApplied", "Verified");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Store.AdvancePhaseAsync(tenantId, idempotencyKey, "Verified", "Prepared"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Store.AdvancePhaseAsync(tenantId, idempotencyKey, "SchemaApplied", "Verified"));
    }

    [Fact]
    public async Task ConcurrentDuplicateRequestsGenerateAndPersistOnlyOneCredential()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenantId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();
        var databaseName = $"mg_tenant_{tenantId:N}";
        var generated = 0;

        async Task<AgentProvisioningCredentials> CallAsync() =>
            await fixture.Store.GetOrCreatePreparedCredentialsAsync(
                tenantId, idempotencyKey, databaseName,
                _ =>
                {
                    Interlocked.Increment(ref generated);
                    return ValueTask.FromResult(Fixture.Credentials(databaseName, "one-time-secret"));
                });

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => CallAsync()));

        Assert.All(results, value => Assert.Equal(results[0], value));
        Assert.Equal(1, generated);
    }

    [Fact]
    public async Task RejectsChangedIdempotencyKeyForExistingTenant()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenantId = Guid.NewGuid();
        var databaseName = $"mg_tenant_{tenantId:N}";

        await fixture.Store.GetOrCreatePreparedCredentialsAsync(
            tenantId, Guid.NewGuid(), databaseName,
            _ => ValueTask.FromResult(Fixture.Credentials(databaseName, "secret-one")));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Store.GetOrCreatePreparedCredentialsAsync(
                tenantId, Guid.NewGuid(), databaseName,
                _ => ValueTask.FromResult(Fixture.Credentials(databaseName, "secret-two"))));
    }

    [Fact]
    public async Task RejectsIdempotencyKeyReusedForDifferentTenant()
    {
        await using var fixture = await Fixture.CreateAsync();
        var idempotencyKey = Guid.NewGuid();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var databaseA = $"mg_tenant_{tenantA:N}";
        var databaseB = $"mg_tenant_{tenantB:N}";

        await fixture.Store.GetOrCreatePreparedCredentialsAsync(
            tenantA, idempotencyKey, databaseA,
            _ => ValueTask.FromResult(Fixture.Credentials(databaseA, "secret-one")));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Store.GetOrCreatePreparedCredentialsAsync(
                tenantB, idempotencyKey, databaseB,
                _ => ValueTask.FromResult(Fixture.Credentials(databaseB, "secret-two"))));
    }

    [Fact]
    public async Task RejectsWrongDatabaseAndEncryptionWithoutCertificateValidation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenantId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();
        var databaseName = $"mg_tenant_{tenantId:N}";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Store.GetOrCreatePreparedCredentialsAsync(
                tenantId, idempotencyKey, databaseName,
                _ => ValueTask.FromResult(Fixture.Credentials("another_db", "secret")));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Store.GetOrCreatePreparedCredentialsAsync(
                tenantId, idempotencyKey, databaseName,
                _ => ValueTask.FromResult(new AgentProvisioningCredentials(
                    $"Host=localhost;Database={databaseName};Username={databaseName}_app;Password=secret;SSL Mode=Require",
                    Fixture.Credentials(databaseName, "owner-test").OwnerConnectionString))));

        var valid = await fixture.Store.GetOrCreatePreparedCredentialsAsync(
            tenantId, idempotencyKey, databaseName,
            _ => ValueTask.FromResult(Fixture.Credentials(databaseName, "valid-secret")));
        Assert.Contains("valid-secret", valid.ApplicationConnectionString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsConnectionStringWithWrongApplicationRoleBeforePersistingCredential()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenantId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();
        var databaseName = $"mg_tenant_{tenantId:N}";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Store.GetOrCreatePreparedCredentialsAsync(
                tenantId, idempotencyKey, databaseName,
                _ => ValueTask.FromResult(new AgentProvisioningCredentials(
                    $"Host=localhost;Database={databaseName};Username=other_role;Password=secret;SSL Mode=VerifyFull",
                    Fixture.Credentials(databaseName, "owner-test").OwnerConnectionString))));

        var valid = await fixture.Store.GetOrCreatePreparedCredentialsAsync(
            tenantId, idempotencyKey, databaseName,
            _ => ValueTask.FromResult(Fixture.Credentials(databaseName, "valid-role-secret")));
        Assert.Contains($"{databaseName}_app", valid.ApplicationConnectionString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsCredentialFactoryFailureWithoutCreatingOperation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenantId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();
        var databaseName = $"mg_tenant_{tenantId:N}";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Store.GetOrCreatePreparedCredentialsAsync(
                tenantId, idempotencyKey, databaseName,
                _ => ValueTask.FromException<AgentProvisioningCredentials>(new InvalidOperationException("safe test failure"))));

        var created = await fixture.Store.GetOrCreatePreparedCredentialsAsync(
            tenantId, idempotencyKey, databaseName,
            _ => ValueTask.FromResult(Fixture.Credentials(databaseName, "retry-secret")));
        Assert.Contains("retry-secret", created.ApplicationConnectionString, StringComparison.Ordinal);
    }

    private sealed class FakeResourceProvisioner : ITenantResourceProvisioner
    {
        public int EnsureCalls { get; private set; }
        public int MigrationCalls { get; private set; }
        public int VerifyCalls { get; private set; }
        public bool FailMigrationOnce { get; init; }
        private bool migrationFailureConsumed;
        public Func<CancellationToken, Task>? OnEnsure { get; init; }

        public async Task EnsureDatabaseAndRolesAsync(
            Guid tenantId, Guid idempotencyKey, string databaseName,
            AgentProvisioningCredentials credentials, CancellationToken cancellationToken)
        {
            EnsureCalls++;
            Assert.Equal($"mg_tenant_{tenantId:N}", databaseName);
            Assert.NotEqual(Guid.Empty, idempotencyKey);
            Assert.Contains(databaseName, credentials.ApplicationConnectionString, StringComparison.Ordinal);
            Assert.Contains($"{databaseName}_owner", credentials.OwnerConnectionString, StringComparison.Ordinal);
            if (OnEnsure is not null)
                await OnEnsure(cancellationToken);
        }

        public Task ApplyTenantSchemaAsync(
            Guid tenantId, AgentProvisioningCredentials credentials, TenantSchemaMigrationArtifact migration,
            CancellationToken cancellationToken)
        {
            MigrationCalls++;
            Assert.Contains($"mg_tenant_{tenantId:N}_owner", credentials.OwnerConnectionString, StringComparison.Ordinal);
            Assert.Equal(1, migration.Version);
            Assert.Equal("tenant_core_baseline", migration.Name);
            Assert.Matches("^[a-f0-9]{64}$", migration.Sha256);
            Assert.Contains("CREATE TABLE mg.tenant_identity", System.Text.Encoding.UTF8.GetString(migration.GetSqlBytes().Span), StringComparison.Ordinal);
            Assert.Contains($"mg_tenant_{tenantId:N}_owner", credentials.OwnerConnectionString, StringComparison.Ordinal);
            if (FailMigrationOnce && !migrationFailureConsumed)
            {
                migrationFailureConsumed = true;
                throw new InvalidOperationException("simulated idempotent migration interruption");
            }
            return Task.CompletedTask;
        }

        public Task<bool> VerifyRuntimeAsync(
            Guid tenantId, string databaseName, string applicationConnectionString,
            int expectedMigrationVersion, string expectedMigrationSha256,
            CancellationToken cancellationToken)
        {
            VerifyCalls++;
            Assert.Equal($"mg_tenant_{tenantId:N}", databaseName);
            Assert.Equal(1, expectedMigrationVersion);
            Assert.Matches("^[a-f0-9]{64}$", expectedMigrationSha256);
            return Task.FromResult(true);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly NpgsqlDataSource dataSource;
        private readonly AgentCredentialProtector protector;

        private Fixture(NpgsqlDataSource dataSource, AgentCredentialProtector protector, AgentProvisioningOperationStore store)
        {
            this.dataSource = dataSource;
            this.protector = protector;
            Store = store;
        }

        public AgentProvisioningOperationStore Store { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var configured = Environment.GetEnvironmentVariable("MG_TEST_POSTGRES");
            if (string.IsNullOrWhiteSpace(configured))
                throw new InvalidOperationException("MG_TEST_POSTGRES must point to a disposable PostgreSQL test database.");

            var builder = new NpgsqlConnectionStringBuilder(configured);
            builder.Pooling = false;
            await using (var connection = new NpgsqlConnection(builder.ConnectionString))
            {
                await connection.OpenAsync();
                var migrationPath = Path.Combine(
                    AppContext.BaseDirectory, "agent-database", "migrations", "001_agent_provisioning_ledger.sql");
                var migrationSql = await File.ReadAllTextAsync(migrationPath);
                await using var command = new NpgsqlCommand(migrationSql, connection);
                await command.ExecuteNonQueryAsync();
            }

            var key = RandomNumberGenerator.GetBytes(32);
            var protector = new AgentCredentialProtector(
                "test-key-v1",
                new Dictionary<string, string> { ["test-key-v1"] = Convert.ToBase64String(key) });
            CryptographicOperations.ZeroMemory(key);
            var dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
            return new Fixture(dataSource, protector,
                new AgentProvisioningOperationStore(dataSource, protector));
        }

        public static AgentProvisioningCredentials Credentials(string databaseName, string password) =>
            new(
                $"Host=localhost;Database={databaseName};Username={databaseName}_app;Password={password};SSL Mode=VerifyFull",
                $"Host=localhost;Database={databaseName};Username={databaseName}_owner;Password={password}-owner;SSL Mode=VerifyFull");

        public async ValueTask DisposeAsync()
        {
            // The test database is disposable; use globally unique tenant/idempotency IDs.
            await dataSource.DisposeAsync();
            protector.Dispose();
        }
    }
}
