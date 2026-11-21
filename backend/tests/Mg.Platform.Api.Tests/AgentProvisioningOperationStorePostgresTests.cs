using System.Security.Cryptography;
using Mg.Provisioning.Agent.Core;
using Npgsql;
using Xunit;

namespace Mg.Platform.Api.Tests;

[CollectionDefinition("AgentProvisioningPostgres", DisableParallelization = true)]
public sealed class AgentProvisioningPostgresCollection { }

[Collection("AgentProvisioningPostgres")]
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
    public async Task CoordinatorReleasesTenantLockAndResumesAfterCancellation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenantId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();
        var enteredProvisioning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var interruptedProvisioner = new FakeResourceProvisioner
        {
            OnEnsure = async token =>
            {
                enteredProvisioning.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
            }
        };
        var interruptedCoordinator = new AgentProvisioningCoordinator(
            fixture.Store, interruptedProvisioner, LoadMigration());
        ValueTask<AgentProvisioningCredentials> CredentialFactory(CancellationToken _) =>
            ValueTask.FromResult(Fixture.Credentials($"mg_tenant_{tenantId:N}", "cancel-resume-secret"));

        using var cancellation = new CancellationTokenSource();
        var interrupted = interruptedCoordinator.ProvisionAsync(
            new AgentProvisioningRequest(tenantId, idempotencyKey), CredentialFactory, cancellation.Token);
        await enteredProvisioning.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => interrupted);
        Assert.Equal("Prepared", await fixture.Store.GetPhaseAsync(tenantId, idempotencyKey));

        var recoveryProvisioner = new FakeResourceProvisioner();
        var recoveryCoordinator = new AgentProvisioningCoordinator(
            fixture.Store, recoveryProvisioner, LoadMigration());
        var recovered = await recoveryCoordinator.ProvisionAsync(
            new AgentProvisioningRequest(tenantId, idempotencyKey), CredentialFactory);

        Assert.Contains("cancel-resume-secret", recovered, StringComparison.Ordinal);
        Assert.Equal(1, interruptedProvisioner.EnsureCalls);
        Assert.Equal(1, recoveryProvisioner.EnsureCalls);
        Assert.Equal(1, recoveryProvisioner.MigrationCalls);
        Assert.Equal(1, recoveryProvisioner.VerifyCalls);
        Assert.Equal("Verified", await fixture.Store.GetPhaseAsync(tenantId, idempotencyKey));
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
    public async Task RewrapsStoredCredentialsToCurrentKeyAndAllowsHistoricalKeyRemoval()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenantId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();
        var databaseName = $"mg_tenant_{tenantId:N}";
        var original = Fixture.Credentials(databaseName, "stable-rotation-secret");
        var stored = await fixture.Store.GetOrCreatePreparedCredentialsAsync(
            tenantId, idempotencyKey, databaseName,
            _ => ValueTask.FromResult(original));

        var newKeyBytes = RandomNumberGenerator.GetBytes(32);
        var newKey = Convert.ToBase64String(newKeyBytes);
        CryptographicOperations.ZeroMemory(newKeyBytes);
        using var rotatedProtector = new AgentCredentialProtector(
            "test-key-v2",
            new Dictionary<string, string>
            {
                ["test-key-v1"] = fixture.OriginalKeyMaterial,
                ["test-key-v2"] = newKey
            });
        var rotatedStore = fixture.CreateStore(rotatedProtector);

        var totalRewrapped = 0;
        int batchRewrapped;
        do
        {
            batchRewrapped = await rotatedStore.RewrapStoredCredentialsAsync(10);
            totalRewrapped += batchRewrapped;
        }
        while (batchRewrapped == 10);

        Assert.True(totalRewrapped >= 1);
        Assert.Equal(0, await rotatedStore.RewrapStoredCredentialsAsync(10));

        using var currentOnlyProtector = new AgentCredentialProtector(
            "test-key-v2",
            new Dictionary<string, string> { ["test-key-v2"] = newKey });
        var currentOnlyStore = fixture.CreateStore(currentOnlyProtector);
        var recovered = await currentOnlyStore.GetOrCreatePreparedCredentialsAsync(
            tenantId, idempotencyKey, databaseName,
            _ => ValueTask.FromException<AgentProvisioningCredentials>(
                new InvalidOperationException("Credential factory must not run for an existing operation.")));

        Assert.Equal(stored, recovered);
    }

    [Fact]
    public async Task CredentialRewrapWaitsForControlPlaneMaintenanceLock()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenantId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();
        var databaseName = $"mg_tenant_{tenantId:N}";
        await fixture.Store.GetOrCreatePreparedCredentialsAsync(
            tenantId, idempotencyKey, databaseName,
            _ => ValueTask.FromResult(Fixture.Credentials(databaseName, "maintenance-lock-secret")));

        var newKeyBytes = RandomNumberGenerator.GetBytes(32);
        var newKey = Convert.ToBase64String(newKeyBytes);
        CryptographicOperations.ZeroMemory(newKeyBytes);
        using var rotatedProtector = new AgentCredentialProtector(
            "test-key-v2",
            new Dictionary<string, string>
            {
                ["test-key-v1"] = fixture.OriginalKeyMaterial,
                ["test-key-v2"] = newKey
            });
        var rotatedStore = fixture.CreateStore(rotatedProtector);

        await using var maintenanceConnection = await fixture.AcquireControlPlaneMaintenanceLockAsync();
        var rewrap = rotatedStore.RewrapStoredCredentialsAsync(10);
        await Task.Delay(TimeSpan.FromMilliseconds(150));
        Assert.False(rewrap.IsCompleted);

        await maintenanceConnection.DisposeAsync();
        Assert.Equal(1, await rewrap.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, await rotatedStore.RewrapStoredCredentialsAsync(10));
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

        await Assert.ThrowsAsync<AgentProvisioningConflictException>(() =>
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

        await Assert.ThrowsAsync<AgentProvisioningConflictException>(() =>
            fixture.Store.GetOrCreatePreparedCredentialsAsync(
                tenantB, idempotencyKey, databaseB,
                _ => ValueTask.FromResult(Fixture.Credentials(databaseB, "secret-two"))));
    }

    [Fact]
    public async Task TenantProvisioningWaitsForControlPlaneMaintenanceLock()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var maintenanceConnection = await fixture.AcquireControlPlaneMaintenanceLockAsync();
        var actionStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var execution = fixture.Store.TryExecuteTenantExclusiveAsync(
            Guid.NewGuid(),
            _ =>
            {
                actionStarted.TrySetResult(true);
                return Task.FromResult("provisioned");
            });

        await Task.Delay(TimeSpan.FromMilliseconds(150));
        Assert.False(actionStarted.Task.IsCompleted);

        await maintenanceConnection.DisposeAsync();
        var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Acquired);
        Assert.Equal("provisioned", result.Value);
        Assert.True(await actionStarted.Task.WaitAsync(TimeSpan.FromSeconds(1)));
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
                _ => ValueTask.FromResult(Fixture.Credentials("another_db", "secret"))));

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

    [Fact]
    public async Task RevalidatesPublicLedgerAclAfterCredentialFactoryBeforePreparedInsert()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenantId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();
        var databaseName = $"mg_tenant_{tenantId:N}";

        try
        {
            var insertFailure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Store.GetOrCreatePreparedCredentialsAsync(
                    tenantId, idempotencyKey, databaseName,
                    async _ =>
                    {
                        await fixture.SetPublicLedgerSelectAsync(grant: true);
                        return Fixture.Credentials(databaseName, "acl-drift-secret");
                    }));
            Assert.Contains("ACL boundary changed", insertFailure.Message, StringComparison.Ordinal);

            var readFailure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Store.GetPhaseAsync(tenantId, idempotencyKey));
            Assert.Contains("ACL boundary changed", readFailure.Message, StringComparison.Ordinal);
        }
        finally
        {
            await fixture.SetPublicLedgerSelectAsync(grant: false);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Store.GetPhaseAsync(tenantId, idempotencyKey));
    }

    [Fact]
    public async Task RejectsPublicDatabaseAndSchemaAclDriftBeforeLedgerReadsAndWrites()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenantId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();
        var databaseName = $"mg_tenant_{tenantId:N}";

        foreach (var drift in new[]
        {
            (Scope: "database", Privilege: "CONNECT"),
            (Scope: "database", Privilege: "TEMPORARY"),
            (Scope: "database", Privilege: "CREATE"),
            (Scope: "schema", Privilege: "USAGE"),
            (Scope: "schema", Privilege: "CREATE"),
            (Scope: "table", Privilege: "SELECT"),
            (Scope: "table", Privilege: "INSERT"),
            (Scope: "table", Privilege: "UPDATE"),
            (Scope: "table", Privilege: "DELETE"),
            (Scope: "table", Privilege: "TRUNCATE"),
            (Scope: "table", Privilege: "REFERENCES"),
            (Scope: "table", Privilege: "TRIGGER")
        })
        {
            try
            {
                await fixture.SetPublicAclAsync(drift.Scope, drift.Privilege, grant: true);
                Assert.True(
                    await fixture.HasPublicAclAsync(drift.Scope, drift.Privilege),
                    $"Test setup did not grant PUBLIC {drift.Privilege} on {drift.Scope}.");

                var writeFailure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    fixture.Store.GetOrCreatePreparedCredentialsAsync(
                        tenantId, idempotencyKey, databaseName,
                        _ => ValueTask.FromResult(Fixture.Credentials(databaseName, "acl-drift-secret"))));
                Assert.Contains("ACL boundary changed", writeFailure.Message, StringComparison.Ordinal);

                var readFailure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    fixture.Store.GetPhaseAsync(tenantId, idempotencyKey));
                Assert.Contains("ACL boundary changed", readFailure.Message, StringComparison.Ordinal);
            }
            finally
            {
                await fixture.SetPublicAclAsync(drift.Scope, drift.Privilege, grant: false);
            }
        }

        var recovered = await fixture.Store.GetOrCreatePreparedCredentialsAsync(
            tenantId, idempotencyKey, databaseName,
            _ => ValueTask.FromResult(Fixture.Credentials(databaseName, "recovered-secret")));
        Assert.Contains("recovered-secret", recovered.ApplicationConnectionString, StringComparison.Ordinal);
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

        private Fixture(
            NpgsqlDataSource dataSource,
            AgentCredentialProtector protector,
            AgentProvisioningOperationStore store,
            string originalKeyMaterial)
        {
            this.dataSource = dataSource;
            this.protector = protector;
            Store = store;
            OriginalKeyMaterial = originalKeyMaterial;
        }

        public AgentProvisioningOperationStore Store { get; }
        public string OriginalKeyMaterial { get; }
        public AgentProvisioningOperationStore CreateStore(AgentCredentialProtector activeProtector) =>
            new(dataSource, activeProtector);

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

                // PostgreSQL grants CONNECT and TEMP to PUBLIC by default when the
                // database ACL is NULL. The production agent database explicitly
                // revokes these defaults, so mirror that security baseline here.
                await using (var revokePublicDatabaseAccess = new NpgsqlCommand(
                    """
                    SELECT format(
                        'REVOKE CONNECT, CREATE, TEMP ON DATABASE %I FROM PUBLIC;',
                        current_database());
                    """, connection))
                {
                    var revokeSql = (string?)await revokePublicDatabaseAccess.ExecuteScalarAsync()
                        ?? throw new InvalidOperationException("Could not construct the PUBLIC database ACL baseline.");
                    await using var applyRevoke = new NpgsqlCommand(revokeSql, connection);
                    await applyRevoke.ExecuteNonQueryAsync();
                }

                var migrationPath = Path.Combine(
                    AppContext.BaseDirectory, "agent-database", "migrations", "001_agent_provisioning_ledger.sql");
                var migrationSql = await File.ReadAllTextAsync(migrationPath);
                await using var command = new NpgsqlCommand(migrationSql, connection);
                await command.ExecuteNonQueryAsync();
            }

            // All tests share the same stable key ID/material because the disposable
            // integration database retains operation rows between test cases.
            const string keyMaterial = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";
            var protector = new AgentCredentialProtector(
                "test-key-v1",
                new Dictionary<string, string> { ["test-key-v1"] = keyMaterial });
            var dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
            return new Fixture(dataSource, protector,
                new AgentProvisioningOperationStore(dataSource, protector), keyMaterial);
        }

        public async Task SetPublicLedgerSelectAsync(bool grant)
        {
            await using var connection = await dataSource.OpenConnectionAsync();
            await using var command = new NpgsqlCommand(
                grant
                    ? "GRANT SELECT ON TABLE mg_agent.provisioning_operations TO PUBLIC;"
                    : "REVOKE SELECT ON TABLE mg_agent.provisioning_operations FROM PUBLIC;",
                connection);
            await command.ExecuteNonQueryAsync();
        }

        public async Task SetPublicAclAsync(string scope, string privilege, bool grant)
        {
            var template = (scope, privilege, grant) switch
            {
                ("database", "CONNECT", true) => "GRANT CONNECT ON DATABASE %I TO PUBLIC;",
                ("database", "CONNECT", false) => "REVOKE CONNECT ON DATABASE %I FROM PUBLIC;",
                ("database", "TEMPORARY", true) => "GRANT TEMPORARY ON DATABASE %I TO PUBLIC;",
                ("database", "TEMPORARY", false) => "REVOKE TEMPORARY ON DATABASE %I FROM PUBLIC;",
                ("database", "CREATE", true) => "GRANT CREATE ON DATABASE %I TO PUBLIC;",
                ("database", "CREATE", false) => "REVOKE CREATE ON DATABASE %I FROM PUBLIC;",
                ("schema", "USAGE", true) => "GRANT USAGE ON SCHEMA mg_agent TO PUBLIC;",
                ("schema", "USAGE", false) => "REVOKE USAGE ON SCHEMA mg_agent FROM PUBLIC;",
                ("schema", "CREATE", true) => "GRANT CREATE ON SCHEMA mg_agent TO PUBLIC;",
                ("schema", "CREATE", false) => "REVOKE CREATE ON SCHEMA mg_agent FROM PUBLIC;",
                ("table", "SELECT", true) => "GRANT SELECT ON TABLE mg_agent.provisioning_operations TO PUBLIC;",
                ("table", "SELECT", false) => "REVOKE SELECT ON TABLE mg_agent.provisioning_operations FROM PUBLIC;",
                ("table", "INSERT", true) => "GRANT INSERT ON TABLE mg_agent.provisioning_operations TO PUBLIC;",
                ("table", "INSERT", false) => "REVOKE INSERT ON TABLE mg_agent.provisioning_operations FROM PUBLIC;",
                ("table", "UPDATE", true) => "GRANT UPDATE ON TABLE mg_agent.provisioning_operations TO PUBLIC;",
                ("table", "UPDATE", false) => "REVOKE UPDATE ON TABLE mg_agent.provisioning_operations FROM PUBLIC;",
                ("table", "DELETE", true) => "GRANT DELETE ON TABLE mg_agent.provisioning_operations TO PUBLIC;",
                ("table", "DELETE", false) => "REVOKE DELETE ON TABLE mg_agent.provisioning_operations FROM PUBLIC;",
                ("table", "TRUNCATE", true) => "GRANT TRUNCATE ON TABLE mg_agent.provisioning_operations TO PUBLIC;",
                ("table", "TRUNCATE", false) => "REVOKE TRUNCATE ON TABLE mg_agent.provisioning_operations FROM PUBLIC;",
                ("table", "REFERENCES", true) => "GRANT REFERENCES ON TABLE mg_agent.provisioning_operations TO PUBLIC;",
                ("table", "REFERENCES", false) => "REVOKE REFERENCES ON TABLE mg_agent.provisioning_operations FROM PUBLIC;",
                ("table", "TRIGGER", true) => "GRANT TRIGGER ON TABLE mg_agent.provisioning_operations TO PUBLIC;",
                ("table", "TRIGGER", false) => "REVOKE TRIGGER ON TABLE mg_agent.provisioning_operations FROM PUBLIC;",
                _ => throw new ArgumentOutOfRangeException(nameof(privilege), "Unsupported PUBLIC ACL test privilege.")
            };

            await using var connection = await dataSource.OpenConnectionAsync();
            var sql = template;
            if (scope == "database")
            {
                await using var resolve = new NpgsqlCommand("SELECT format(@template, current_database());", connection);
                resolve.Parameters.AddWithValue("template", template);
                sql = (string?)await resolve.ExecuteScalarAsync()
                    ?? throw new InvalidOperationException("Could not resolve the current database for ACL test.");
            }
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<NpgsqlConnection> AcquireControlPlaneMaintenanceLockAsync()
        {
            var connection = await dataSource.OpenConnectionAsync();
            await using var command = new NpgsqlCommand(
                "SELECT pg_advisory_lock(19783, 20562);", connection);
            await command.ExecuteNonQueryAsync();
            return connection;
        }

        public async Task<bool> HasPublicAclAsync(string scope, string privilege)
        {
            var sql = (scope, privilege) switch
            {
                ("database", "CONNECT") => "SELECT EXISTS (SELECT 1 FROM pg_database d, LATERAL aclexplode(COALESCE(d.datacl, acldefault('d', d.datdba))) a WHERE d.datname = current_database() AND a.grantee = 0 AND a.privilege_type = 'CONNECT');",
                ("database", "TEMPORARY") => "SELECT EXISTS (SELECT 1 FROM pg_database d, LATERAL aclexplode(COALESCE(d.datacl, acldefault('d', d.datdba))) a WHERE d.datname = current_database() AND a.grantee = 0 AND a.privilege_type = 'TEMPORARY');",
                ("database", "CREATE") => "SELECT EXISTS (SELECT 1 FROM pg_database d, LATERAL aclexplode(COALESCE(d.datacl, acldefault('d', d.datdba))) a WHERE d.datname = current_database() AND a.grantee = 0 AND a.privilege_type = 'CREATE');",
                ("schema", "USAGE") => "SELECT EXISTS (SELECT 1 FROM pg_namespace n, LATERAL aclexplode(COALESCE(n.nspacl, acldefault('n', n.nspowner))) a WHERE n.nspname = 'mg_agent' AND a.grantee = 0 AND a.privilege_type = 'USAGE');",
                ("schema", "CREATE") => "SELECT EXISTS (SELECT 1 FROM pg_namespace n, LATERAL aclexplode(COALESCE(n.nspacl, acldefault('n', n.nspowner))) a WHERE n.nspname = 'mg_agent' AND a.grantee = 0 AND a.privilege_type = 'CREATE');",
                ("table", _) => "SELECT EXISTS (SELECT 1 FROM pg_class t JOIN pg_namespace n ON n.oid = t.relnamespace, LATERAL aclexplode(COALESCE(t.relacl, acldefault('r', t.relowner))) a WHERE n.nspname = 'mg_agent' AND t.relname = 'provisioning_operations' AND a.grantee = 0 AND a.privilege_type = @privilege);",
                _ => throw new ArgumentOutOfRangeException(nameof(privilege))
            };
            await using var connection = await dataSource.OpenConnectionAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            if (scope == "table")
                command.Parameters.AddWithValue("privilege", privilege);
            return (bool)(await command.ExecuteScalarAsync() ?? false);
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
