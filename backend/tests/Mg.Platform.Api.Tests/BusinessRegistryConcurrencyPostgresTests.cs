using Mg.Platform.Api.Contracts;
using Mg.Platform.Api.Data;
using Mg.Platform.Api.Endpoints;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Mg.Platform.Api.Tests;

public sealed class BusinessRegistryConcurrencyPostgresTests
{
    [Fact]
    public async Task ConcurrentSameSlugRequestsCreateExactlyOneBusinessAndOneOutboxJob()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();

        async Task<(bool Conflict, Guid? BusinessId)> CreateAsync(int index)
        {
            await using var context = fixture.CreateContext();
            var result = await new BusinessRegistryService(context).CreateAsync(
                new CreateBusinessRequest($"Cafe {index}", "same-cafe"),
                Guid.NewGuid(), "https://issuer.test", $"creator-{index}", CancellationToken.None);
            return (result.Conflict, result.Business?.Id);
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(CreateAsync));

        Assert.Single(results, result => !result.Conflict && result.BusinessId.HasValue);
        Assert.Equal(15, results.Count(result => result.Conflict));
        Assert.Equal(1, await fixture.CountBusinessesAsync());
        Assert.Equal(1, await fixture.CountJobsAsync());

        var stored = await fixture.ReadBusinessAsync();
        Assert.Equal("same-cafe", stored.Slug);
        Assert.Equal("PendingProvisioning", stored.Status);
        Assert.Equal($"tenants/{stored.Id:N}/postgres", stored.DatabaseSecretReference);
    }

    [Fact]
    public async Task DistinctConcurrentSlugsCreateIndependentBusinessesAndOutboxJobs()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();

        async Task<Guid> CreateAsync(int index)
        {
            await using var context = fixture.CreateContext();
            var result = await new BusinessRegistryService(context).CreateAsync(
                new CreateBusinessRequest($"Cafe {index}", $"cafe-{index}"),
                Guid.NewGuid(), "https://issuer.test", $"creator-{index}", CancellationToken.None);
            Assert.False(result.Conflict);
            return result.Business!.Id;
        }

        var ids = await Task.WhenAll(Enumerable.Range(0, 12).Select(CreateAsync));

        Assert.Equal(12, ids.Distinct().Count());
        Assert.Equal(12, await fixture.CountBusinessesAsync());
        Assert.Equal(12, await fixture.CountJobsAsync());
        Assert.Equal(12, await fixture.CountDistinctIdempotencyKeysAsync());
    }

    [Fact]
    public async Task SameIdempotencyKeyAndPayloadReplaysOriginalBusinessWithoutCreatingAnotherJob()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var key = Guid.NewGuid();
        var request = new CreateBusinessRequest("Replay Cafe", "replay-cafe");

        await using var firstContext = fixture.CreateContext();
        var first = await new BusinessRegistryService(firstContext).CreateAsync(
            request, key, "https://issuer.test", "creator-subject-1", CancellationToken.None);

        await using var retryContext = fixture.CreateContext();
        var retry = await new BusinessRegistryService(retryContext).CreateAsync(
            request with { Name = " Replay Cafe ", Slug = "REPLAY-CAFE" },
            key, "https://issuer.test", "creator-subject-1", CancellationToken.None);

        Assert.False(first.Conflict);
        Assert.False(first.Replayed);
        Assert.False(retry.Conflict);
        Assert.True(retry.Replayed);
        Assert.Equal(first.Business!.Id, retry.Business!.Id);
        Assert.Equal(1, await fixture.CountBusinessesAsync());
        Assert.Equal(1, await fixture.CountJobsAsync());
        Assert.Equal(1, await fixture.CountIdempotencyRecordsAsync());
    }

    [Fact]
    public async Task IdempotencyKeyCannotBeReusedForDifferentPayloadOrCreator()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var key = Guid.NewGuid();
        await using (var firstContext = fixture.CreateContext())
        {
            var first = await new BusinessRegistryService(firstContext).CreateAsync(
                new CreateBusinessRequest("Original Cafe", "original-cafe"),
                key, "https://issuer.test", "creator-one", CancellationToken.None);
            Assert.False(first.Conflict);
        }

        await using (var changedPayloadContext = fixture.CreateContext())
        {
            var changedPayload = await new BusinessRegistryService(changedPayloadContext).CreateAsync(
                new CreateBusinessRequest("Changed Cafe", "changed-cafe"),
                key, "https://issuer.test", "creator-one", CancellationToken.None);
            Assert.True(changedPayload.Conflict);
            Assert.Null(changedPayload.Business);
        }

        await using (var changedCreatorContext = fixture.CreateContext())
        {
            var changedCreator = await new BusinessRegistryService(changedCreatorContext).CreateAsync(
                new CreateBusinessRequest("Original Cafe", "original-cafe"),
                key, "https://issuer.test", "creator-two", CancellationToken.None);
            Assert.True(changedCreator.Conflict);
            Assert.Null(changedCreator.Business);
        }

        Assert.Equal(1, await fixture.CountBusinessesAsync());
        Assert.Equal(1, await fixture.CountJobsAsync());
        Assert.Equal(1, await fixture.CountIdempotencyRecordsAsync());
    }

    [Fact]
    public async Task ConcurrentSameIdempotencyKeyRequestsReplayOneCommittedBusiness()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var key = Guid.NewGuid();

        async Task<(Guid? BusinessId, bool Conflict, bool Replayed)> CreateAsync()
        {
            await using var context = fixture.CreateContext();
            var result = await new BusinessRegistryService(context).CreateAsync(
                new CreateBusinessRequest("Concurrent Replay Cafe", "concurrent-replay-cafe"),
                key, "https://issuer.test", "creator-subject", CancellationToken.None);
            return (result.Business?.Id, result.Conflict, result.Replayed);
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => CreateAsync()));

        Assert.All(results, result =>
        {
            Assert.False(result.Conflict);
            Assert.NotNull(result.BusinessId);
            Assert.Equal(results[0].BusinessId, result.BusinessId);
        });
        Assert.Single(results, result => !result.Replayed);
        Assert.Equal(15, results.Count(result => result.Replayed));
        Assert.Equal(1, await fixture.CountBusinessesAsync());
        Assert.Equal(1, await fixture.CountJobsAsync());
        Assert.Equal(1, await fixture.CountIdempotencyRecordsAsync());
    }

    [Fact]
    public async Task OutboxInsertFailureRollsBackBusinessRegistryInsert()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        await fixture.RejectOutboxInsertsAsync();

        await using var context = fixture.CreateContext();
        var registry = new BusinessRegistryService(context);

        await Assert.ThrowsAsync<DbUpdateException>(() => registry.CreateAsync(
            new CreateBusinessRequest("Atomic Cafe", "atomic-cafe"),
            Guid.NewGuid(), "https://issuer.test", "atomic-creator", CancellationToken.None));

        Assert.Equal(0, await fixture.CountBusinessesAsync());
        Assert.Equal(0, await fixture.CountJobsAsync());
        Assert.Equal(0, await fixture.CountIdempotencyRecordsAsync());
    }

    internal sealed class DatabaseFixture : IAsyncDisposable
    {
        private readonly string adminConnection;
        private readonly string schema;
        private readonly string testConnection;

        private DatabaseFixture(string adminConnection, string schema, string testConnection)
        {
            this.adminConnection = adminConnection;
            this.schema = schema;
            this.testConnection = testConnection;
        }

        public static async Task<DatabaseFixture> CreateAsync()
        {
            var configured = Environment.GetEnvironmentVariable("MG_TEST_POSTGRES");
            if (string.IsNullOrWhiteSpace(configured))
                throw new InvalidOperationException("MG_TEST_POSTGRES must point to a disposable PostgreSQL test database.");

            var builder = new NpgsqlConnectionStringBuilder(configured) { Pooling = false };
            var adminConnection = builder.ConnectionString;
            var schema = "mg_registry_test_" + Guid.NewGuid().ToString("N");
            await using (var connection = new NpgsqlConnection(adminConnection))
            {
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand($@"CREATE SCHEMA ""{schema}""", connection);
                await command.ExecuteNonQueryAsync();
            }

            builder.SearchPath = schema;
            var testConnection = builder.ConnectionString;
            try
            {
                await using var connection = new NpgsqlConnection(testConnection);
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand("""
                    CREATE TABLE business_tenants (
                        id uuid PRIMARY KEY,
                        name varchar(160) NOT NULL,
                        slug varchar(80) NOT NULL,
                        status varchar(24) NOT NULL,
                        database_secret_reference varchar(300) NOT NULL,
                        created_at_utc timestamptz NOT NULL,
                        activated_at_utc timestamptz NULL,
                        CONSTRAINT uq_business_tenants_slug UNIQUE (slug)
                    );
                    CREATE TABLE tenant_provisioning_jobs (
                        id uuid PRIMARY KEY,
                        business_id uuid NOT NULL REFERENCES business_tenants(id) ON DELETE RESTRICT,
                        status varchar(20) NOT NULL,
                        attempts integer NOT NULL,
                        idempotency_key uuid NOT NULL UNIQUE,
                        lease_owner varchar(120) NULL,
                        lease_token uuid NULL,
                        lease_expires_at_utc timestamptz NULL,
                        created_at_utc timestamptz NOT NULL,
                        next_attempt_at_utc timestamptz NOT NULL,
                        started_at_utc timestamptz NULL,
                        completed_at_utc timestamptz NULL,
                        last_error_code varchar(80) NULL
                    );
                    CREATE TABLE business_creation_requests (
                        idempotency_key uuid NOT NULL,
                        creator_identity_hash char(64) NOT NULL,
                        request_fingerprint char(64) NOT NULL,
                        business_id uuid NOT NULL REFERENCES business_tenants(id) ON DELETE RESTRICT,
                        created_at_utc timestamptz NOT NULL,
                        CONSTRAINT pk_business_creation_requests PRIMARY KEY (idempotency_key),
                        CONSTRAINT ck_business_creation_creator_hash
                            CHECK (creator_identity_hash ~ '^[0-9a-f]{64}$'),
                        CONSTRAINT ck_business_creation_request_hash
                            CHECK (request_fingerprint ~ '^[0-9a-f]{64}$')
                    );
                    """, connection);
                await command.ExecuteNonQueryAsync();
            }
            catch
            {
                await using var cleanup = new NpgsqlConnection(adminConnection);
                await cleanup.OpenAsync();
                await using var drop = new NpgsqlCommand($@"DROP SCHEMA IF EXISTS ""{schema}"" CASCADE", cleanup);
                await drop.ExecuteNonQueryAsync();
                throw;
            }

            return new DatabaseFixture(adminConnection, schema, testConnection);
        }

        public string TestConnection => testConnection;

        public PlatformDbContext CreateContext() =>
            new(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(testConnection).Options);

        public Task<int> CountBusinessesAsync() => ScalarAsync("SELECT count(*) FROM business_tenants");
        public Task<int> CountJobsAsync() => ScalarAsync("SELECT count(*) FROM tenant_provisioning_jobs");
        public Task<int> CountIdempotencyRecordsAsync() =>
            ScalarAsync("SELECT count(*) FROM business_creation_requests");
        public Task<int> CountDistinctIdempotencyKeysAsync() =>
            ScalarAsync("SELECT count(DISTINCT idempotency_key) FROM tenant_provisioning_jobs");

        public async Task<(Guid Id, string Slug, string Status, string DatabaseSecretReference)> ReadBusinessAsync()
        {
            await using var connection = new NpgsqlConnection(testConnection);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT id, slug, status, database_secret_reference
                FROM business_tenants
                """, connection);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return (reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
        }

        public async Task DropIdempotencyTableAsync()
        {
            await using var connection = new NpgsqlConnection(testConnection);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "DROP TABLE business_creation_requests CASCADE", connection);
            await command.ExecuteNonQueryAsync();
        }

        public async Task RejectOutboxInsertsAsync()
        {
            await using var connection = new NpgsqlConnection(testConnection);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                CREATE FUNCTION reject_test_outbox_insert() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'simulated outbox persistence failure';
                END;
                $$;
                CREATE TRIGGER reject_test_outbox_insert
                BEFORE INSERT ON tenant_provisioning_jobs
                FOR EACH ROW EXECUTE FUNCTION reject_test_outbox_insert();
                """, connection);
            await command.ExecuteNonQueryAsync();
        }

        private async Task<int> ScalarAsync(string sql)
        {
            await using var connection = new NpgsqlConnection(testConnection);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(adminConnection);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($@"DROP SCHEMA IF EXISTS ""{schema}"" CASCADE", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
