using System.Security.Cryptography;
using Mg.Provisioning.Agent.Core;
using Npgsql;
using Xunit;

namespace Mg.Platform.Api.Tests;

public sealed class AgentProvisioningOperationStorePostgresTests
{
    [Fact]
    public async Task SameRequestReplaysStableCredentialAndAllowsOnlyForwardPhaseTransitions()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenantId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();
        var databaseName = $"mg_tenant_{tenantId:N}";
        var generated = 0;

        var first = await fixture.Store.GetOrCreatePreparedCredentialAsync(
            tenantId, idempotencyKey, databaseName,
            _ =>
            {
                Interlocked.Increment(ref generated);
                return ValueTask.FromResult(Fixture.ConnectionString(databaseName, "stable-secret"));
            });

        var replay = await fixture.Store.GetOrCreatePreparedCredentialAsync(
            tenantId, idempotencyKey, databaseName,
            _ =>
            {
                Interlocked.Increment(ref generated);
                return ValueTask.FromResult(Fixture.ConnectionString(databaseName, "should-not-be-used"));
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

        async Task<string> CallAsync() =>
            await fixture.Store.GetOrCreatePreparedCredentialAsync(
                tenantId, idempotencyKey, databaseName,
                _ =>
                {
                    Interlocked.Increment(ref generated);
                    return ValueTask.FromResult(Fixture.ConnectionString(databaseName, "one-time-secret"));
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

        await fixture.Store.GetOrCreatePreparedCredentialAsync(
            tenantId, Guid.NewGuid(), databaseName,
            _ => ValueTask.FromResult(Fixture.ConnectionString(databaseName, "secret-one")));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Store.GetOrCreatePreparedCredentialAsync(
                tenantId, Guid.NewGuid(), databaseName,
                _ => ValueTask.FromResult(Fixture.ConnectionString(databaseName, "secret-two"))));
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

        await fixture.Store.GetOrCreatePreparedCredentialAsync(
            tenantA, idempotencyKey, databaseA,
            _ => ValueTask.FromResult(Fixture.ConnectionString(databaseA, "secret-one")));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Store.GetOrCreatePreparedCredentialAsync(
                tenantB, idempotencyKey, databaseB,
                _ => ValueTask.FromResult(Fixture.ConnectionString(databaseB, "secret-two"))));
    }

    [Fact]
    public async Task RejectsCredentialFactoryFailureWithoutCreatingOperation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenantId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();
        var databaseName = $"mg_tenant_{tenantId:N}";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Store.GetOrCreatePreparedCredentialAsync(
                tenantId, idempotencyKey, databaseName,
                _ => ValueTask.FromException<string>(new InvalidOperationException("safe test failure"))));

        var created = await fixture.Store.GetOrCreatePreparedCredentialAsync(
            tenantId, idempotencyKey, databaseName,
            _ => ValueTask.FromResult(Fixture.ConnectionString(databaseName, "retry-secret")));
        Assert.Contains("retry-secret", created, StringComparison.Ordinal);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly NpgsqlDataSource dataSource;
        private readonly string connectionString;
        private readonly Guid[] operationIds = [];

        private Fixture(NpgsqlDataSource dataSource, string connectionString, AgentProvisioningOperationStore store)
        {
            this.dataSource = dataSource;
            this.connectionString = connectionString;
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
                await using var command = new NpgsqlCommand("""
                    CREATE SCHEMA IF NOT EXISTS mg_agent;
                    CREATE TABLE IF NOT EXISTS mg_agent.provisioning_operations
                    (
                        tenant_id UUID PRIMARY KEY,
                        idempotency_key UUID NOT NULL UNIQUE,
                        database_name TEXT NOT NULL UNIQUE,
                        request_hash BYTEA NOT NULL CHECK (octet_length(request_hash) = 32),
                        phase TEXT NOT NULL CHECK (phase IN
                            ('Prepared', 'DatabaseCreated', 'SchemaApplied', 'Verified')),
                        credential_key_id TEXT NOT NULL,
                        credential_nonce BYTEA NOT NULL CHECK (octet_length(credential_nonce) = 12),
                        credential_ciphertext BYTEA NOT NULL CHECK (octet_length(credential_ciphertext) BETWEEN 1 AND 16384),
                        credential_tag BYTEA NOT NULL CHECK (octet_length(credential_tag) = 16),
                        created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                        updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                        CHECK (database_name = 'mg_tenant_' || replace(tenant_id::text, '-', ''))
                    );
                    """, connection);
                await command.ExecuteNonQueryAsync();
            }

            var key = RandomNumberGenerator.GetBytes(32);
            var protector = new AgentCredentialProtector(
                "test-key-v1",
                new Dictionary<string, string> { ["test-key-v1"] = Convert.ToBase64String(key) });
            CryptographicOperations.ZeroMemory(key);
            var dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
            return new Fixture(dataSource, builder.ConnectionString,
                new AgentProvisioningOperationStore(dataSource, protector));
        }

        public static string ConnectionString(string databaseName, string password) =>
            $"Host=localhost;Database={databaseName};Username=tenant_app;Password={password};SSL Mode=VerifyFull";

        public async ValueTask DisposeAsync()
        {
            // The test database is disposable; use globally unique tenant/idempotency IDs.
            await dataSource.DisposeAsync();
        }
    }
}
