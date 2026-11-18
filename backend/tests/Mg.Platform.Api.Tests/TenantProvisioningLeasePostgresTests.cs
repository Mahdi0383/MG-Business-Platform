using Mg.Platform.Api.Data;
using Mg.Platform.Api.Endpoints;
using Mg.Platform.Api.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Mg.Platform.Api.Tests;

public sealed class TenantProvisioningLeasePostgresTests
{
    [Fact]
    public async Task ConcurrentWorkersClaimDifferentJobs()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var first = await fixture.AddJobAsync("first");
        var second = await fixture.AddJobAsync("second");

        async Task<TenantProvisioningJob?> Claim(string worker)
        {
            await using var context = fixture.CreateContext();
            return await new TenantProvisioningLeaseService(context)
                .ClaimNextAsync(worker, TimeSpan.FromMinutes(1), CancellationToken.None);
        }

        var claims = await Task.WhenAll(Claim("worker-a"), Claim("worker-b"));
        Assert.All(claims, Assert.NotNull);
        Assert.NotEqual(claims[0]!.Id, claims[1]!.Id);
        Assert.NotNull(claims[0]!.LeaseToken);
        Assert.NotNull(claims[1]!.LeaseToken);
        Assert.NotEqual(claims[0]!.LeaseToken, claims[1]!.LeaseToken);
        Assert.Equal(new[] { "worker-a", "worker-b" },
            claims.Select(job => job!.LeaseOwner).Order().ToArray());
        Assert.Contains(claims, job => job!.Id == first.Id);
        Assert.Contains(claims, job => job!.Id == second.Id);
    }

    [Fact]
    public async Task ExpiredLeaseCanBeReclaimedAndOldOwnerCannotRenew()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var job = await fixture.AddJobAsync("expired", TenantProvisioningStatus.Running,
            attempts: 1, leaseOwner: "old-worker",
            leaseExpiry: DateTimeOffset.UtcNow.AddMinutes(-1));

        await using var context = fixture.CreateContext();
        var service = new TenantProvisioningLeaseService(context);
        var claimed = await service.ClaimNextAsync(
            "new-worker", TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.NotNull(claimed);
        Assert.Equal(job.Id, claimed.Id);
        Assert.Equal(2, claimed.Attempts);
        Assert.Equal("new-worker", claimed.LeaseOwner);
        Assert.NotNull(claimed.LeaseToken);
        Assert.NotEqual(job.LeaseToken, claimed.LeaseToken);
        Assert.False(await service.RenewAsync(job.Id, "old-worker", job.LeaseToken!.Value,
            TimeSpan.FromMinutes(1), CancellationToken.None));
        Assert.True(await service.RenewAsync(job.Id, "new-worker", claimed.LeaseToken.Value,
            TimeSpan.FromMinutes(1), CancellationToken.None));
    }

    [Fact]
    public async Task FencingTokenRejectsStaleAttemptEvenWhenWorkerIdIsReused()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var job = await fixture.AddJobAsync("same-worker", TenantProvisioningStatus.Running,
            attempts: 1, leaseOwner: "reused-worker",
            leaseExpiry: DateTimeOffset.UtcNow.AddMinutes(-1));
        var staleToken = job.LeaseToken!.Value;

        await using var context = fixture.CreateContext();
        var service = new TenantProvisioningLeaseService(context);
        var reclaimed = await service.ClaimNextAsync(
            "reused-worker", TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.NotNull(reclaimed);
        Assert.Equal(job.Id, reclaimed.Id);
        Assert.NotNull(reclaimed.LeaseToken);
        Assert.NotEqual(staleToken, reclaimed.LeaseToken);

        Assert.False(await service.RenewAsync(job.Id, "reused-worker", staleToken,
            TimeSpan.FromMinutes(1), CancellationToken.None));
        Assert.False(await service.MarkSucceededAsync(job.Id, "reused-worker", staleToken,
            CancellationToken.None));
        Assert.False(await service.MarkFailedAsync(job.Id, "reused-worker", staleToken,
            "STALE_ATTEMPT", permanent: true, CancellationToken.None));

        Assert.True(await service.RenewAsync(job.Id, "reused-worker", reclaimed.LeaseToken.Value,
            TimeSpan.FromMinutes(1), CancellationToken.None));
    }

    [Fact]
    public async Task ExpiredLeaseAtAttemptLimitIsReconciledToDeadLetter()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var job = await fixture.AddJobAsync("exhausted",
            TenantProvisioningStatus.Running, attempts: 8,
            leaseOwner: "crashed-worker",
            leaseExpiry: DateTimeOffset.UtcNow.AddMinutes(-2));

        await using var context = fixture.CreateContext();
        var service = new TenantProvisioningLeaseService(context);
        Assert.Equal(1, await service.DeadLetterExhaustedAsync(CancellationToken.None));

        var stored = await context.TenantProvisioningJobs.AsNoTracking()
            .SingleAsync(item => item.Id == job.Id);
        Assert.Equal(TenantProvisioningStatus.DeadLetter, stored.Status);
        Assert.Equal("LEASE_EXHAUSTED", stored.LastErrorCode);
        Assert.Null(stored.LeaseOwner);
        Assert.Null(stored.LeaseToken);
        Assert.Null(stored.LeaseExpiresAtUtc);
        Assert.Equal(0, await service.DeadLetterExhaustedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task PermanentFailureMovesJobToDeadLetterAndReleasesLease()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var job = await fixture.AddJobAsync("failure");

        await using var context = fixture.CreateContext();
        var service = new TenantProvisioningLeaseService(context);
        var claimed = await service.ClaimNextAsync(
            "worker-failure", TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.NotNull(claimed);

        Assert.True(await service.MarkFailedAsync(job.Id, "worker-failure", claimed.LeaseToken!.Value,
            "PROVIDER_DENIED", permanent: true, CancellationToken.None));

        var stored = await context.TenantProvisioningJobs.AsNoTracking()
            .SingleAsync(item => item.Id == job.Id);
        Assert.Equal(TenantProvisioningStatus.DeadLetter, stored.Status);
        Assert.Null(stored.LeaseOwner);
        Assert.Null(stored.LeaseToken);
        Assert.Null(stored.LeaseExpiresAtUtc);
        Assert.Equal("PROVIDER_DENIED", stored.LastErrorCode);
        Assert.False(await service.MarkFailedAsync(job.Id, "worker-failure", claimed.LeaseToken.Value,
            "PROVIDER_DENIED", permanent: true, CancellationToken.None));
    }

    [Fact]
    public async Task SuccessfulCompletionActivatesBusinessAtomically()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var (business, job) = await fixture.AddBusinessAndJobAsync("activation");

        await using var context = fixture.CreateContext();
        var service = new TenantProvisioningLeaseService(context);
        var claimed = await service.ClaimNextAsync(
            "worker-success", TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.NotNull(claimed);

        Assert.True(await service.MarkSucceededAsync(
            job.Id, "worker-success", claimed.LeaseToken!.Value, CancellationToken.None));
        Assert.False(await service.MarkSucceededAsync(
            job.Id, "worker-success", claimed.LeaseToken.Value, CancellationToken.None));

        var storedJob = await context.TenantProvisioningJobs.AsNoTracking()
            .SingleAsync(item => item.Id == job.Id);
        var storedBusiness = await context.Businesses.AsNoTracking()
            .SingleAsync(item => item.Id == business.Id);
        Assert.Equal(TenantProvisioningStatus.Succeeded, storedJob.Status);
        Assert.Equal("Active", storedBusiness.Status);
        Assert.NotNull(storedBusiness.ActivatedAtUtc);
        Assert.Null(storedJob.LeaseOwner);
        Assert.Null(storedJob.LeaseToken);
        Assert.Null(storedJob.LeaseExpiresAtUtc);
    }

    private sealed class DatabaseFixture : IAsyncDisposable
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
                throw new InvalidOperationException(
                    "MG_TEST_POSTGRES must point to a disposable PostgreSQL test database.");

            var schema = "mg_lease_test_" + Guid.NewGuid().ToString("N");
            var builder = new NpgsqlConnectionStringBuilder(configured);
            var adminConnection = builder.ConnectionString;
            await using (var connection = new NpgsqlConnection(adminConnection))
            {
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand(
                    $@"CREATE SCHEMA ""{schema}""", connection);
                await command.ExecuteNonQueryAsync();
            }

            builder.SearchPath = schema;
            var testConnection = builder.ConnectionString;
            await using (var connection = new NpgsqlConnection(testConnection))
            {
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand("""
                    CREATE TABLE business_tenants (
                        id uuid PRIMARY KEY,
                        name varchar(160) NOT NULL,
                        slug varchar(80) NOT NULL UNIQUE,
                        status varchar(24) NOT NULL,
                        database_secret_reference varchar(300) NOT NULL,
                        created_at_utc timestamptz NOT NULL,
                        activated_at_utc timestamptz NULL
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
                        last_error_code varchar(80) NULL,
                        CONSTRAINT ck_test_lease_pair
                            CHECK ((lease_owner IS NULL) = (lease_expires_at_utc IS NULL)),
                        CONSTRAINT ck_test_lease_token_pair
                            CHECK ((lease_owner IS NULL) = (lease_token IS NULL))
                    );
                    """, connection);
                await command.ExecuteNonQueryAsync();
            }

            return new DatabaseFixture(adminConnection, schema, testConnection);
        }

        public PlatformDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<PlatformDbContext>()
                .UseNpgsql(testConnection)
                .Options;
            return new PlatformDbContext(options);
        }

        public async Task<TenantProvisioningJob> AddJobAsync(
            string slug,
            string status = TenantProvisioningStatus.Pending,
            int attempts = 0,
            string? leaseOwner = null,
            DateTimeOffset? leaseExpiry = null)
        {
            var (_, job) = await AddBusinessAndJobAsync(
                slug, status, attempts, leaseOwner, leaseExpiry);
            return job;
        }

        public async Task<(BusinessTenant Business, TenantProvisioningJob Job)> AddBusinessAndJobAsync(
            string slug,
            string status = TenantProvisioningStatus.Pending,
            int attempts = 0,
            string? leaseOwner = null,
            DateTimeOffset? leaseExpiry = null)
        {
            var businessId = Guid.NewGuid();
            var business = new BusinessTenant
            {
                Id = businessId,
                Name = slug,
                Slug = slug,
                Status = "PendingProvisioning",
                DatabaseSecretReference = $"tenants/{businessId:N}/postgres"
            };
            var job = new TenantProvisioningJob
            {
                BusinessId = business.Id,
                Status = status,
                Attempts = attempts,
                LeaseOwner = leaseOwner,
                LeaseToken = leaseOwner is null ? null : Guid.NewGuid(),
                LeaseExpiresAtUtc = leaseExpiry
            };
            await using var context = CreateContext();
            context.Businesses.Add(business);
            context.TenantProvisioningJobs.Add(job);
            await context.SaveChangesAsync();
            return (business, job);
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(adminConnection);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                $@"DROP SCHEMA IF EXISTS ""{schema}"" CASCADE", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
