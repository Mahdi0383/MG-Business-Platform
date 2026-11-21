using Mg.Platform.Api.Data;
using Mg.Platform.Api.Endpoints;
using Mg.Platform.Api.Models;
using Mg.Platform.Api.Provisioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Mg.Platform.Api.Tests;

public sealed class TenantProvisioningWorkerPostgresTests
{
    private static readonly TimeSpan TestLeaseDuration = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan TestRenewalInterval = TimeSpan.FromMilliseconds(40);

    [Fact]
    public async Task RenewsLeaseWhileHandlerRunsAndCompletesAtomicallyAfterHandlerReturns()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (business, job) = await fixture.AddBusinessAndJobAsync("heartbeat-success");
        var started = new TaskCompletionSource<TenantProvisioningJob>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new DelegateHandler(async (claimed, token) =>
        {
            started.TrySetResult(claimed);
            await release.Task.WaitAsync(token);
        });
        using var services = CreateServices(fixture, handler);
        var worker = CreateWorker(services);
        var processing = worker.ProcessOneAsync("heartbeat-worker", CancellationToken.None);
        var claimed = await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var originalExpiry = claimed.LeaseExpiresAtUtc!.Value;

        await Task.Delay(TestRenewalInterval + TestRenewalInterval);
        var renewed = await fixture.ReadJobAsync(job.Id);
        Assert.True(renewed.LeaseExpiresAtUtc > originalExpiry);

        release.TrySetResult();
        await processing.WaitAsync(TimeSpan.FromSeconds(3));

        var completed = await fixture.ReadJobAsync(job.Id);
        var activated = await fixture.ReadBusinessAsync(business.Id);
        Assert.Equal(TenantProvisioningStatus.Succeeded, completed.Status);
        Assert.Null(completed.LeaseOwner);
        Assert.Null(completed.LeaseToken);
        Assert.Null(completed.LeaseExpiresAtUtc);
        Assert.Equal("Active", activated.Status);
        Assert.NotNull(activated.ActivatedAtUtc);
    }

    [Fact]
    public async Task LostLeaseCancelsHandlerAndPreventsStaleAttemptFromFailingOrCompleting()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (business, job) = await fixture.AddBusinessAndJobAsync("heartbeat-lost");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new DelegateHandler(async (_, token) =>
        {
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException)
            {
                cancelled.TrySetResult();
                throw;
            }
        });
        using var services = CreateServices(fixture, handler);
        var worker = CreateWorker(services);

        var processing = worker.ProcessOneAsync("original-worker", CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await fixture.ExpireLeaseAsync(job.Id);
        var replacement = await fixture.ClaimAsync("replacement-worker");
        Assert.NotNull(replacement);
        Assert.Equal(job.Id, replacement.Id);

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await processing.WaitAsync(TimeSpan.FromSeconds(3));

        var stored = await fixture.ReadJobAsync(job.Id);
        var storedBusiness = await fixture.ReadBusinessAsync(business.Id);
        Assert.Equal(TenantProvisioningStatus.Running, stored.Status);
        Assert.Equal("replacement-worker", stored.LeaseOwner);
        Assert.Equal(replacement.LeaseToken, stored.LeaseToken);
        Assert.Equal("PendingProvisioning", storedBusiness.Status);
        Assert.Null(storedBusiness.ActivatedAtUtc);
    }

    [Fact]
    public async Task HandlerThatIgnoresLostLeaseCancellationCannotCompleteReclaimedJob()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (business, job) = await fixture.AddBusinessAndJobAsync("heartbeat-ignores-cancel");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedCancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new DelegateHandler(async (_, token) =>
        {
            using var registration = token.Register(() => observedCancellation.TrySetResult());
            started.TrySetResult();
            await release.Task; // Deliberately ignores cancellation to simulate an uncooperative provider.
        });
        using var services = CreateServices(fixture, handler);
        var worker = CreateWorker(services);

        var processing = worker.ProcessOneAsync("stale-worker", CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await fixture.ExpireLeaseAsync(job.Id);
        var replacement = await fixture.ClaimAsync("replacement-worker");
        Assert.NotNull(replacement);

        await observedCancellation.Task.WaitAsync(TimeSpan.FromSeconds(3));
        release.TrySetResult();
        await processing.WaitAsync(TimeSpan.FromSeconds(3));

        var stored = await fixture.ReadJobAsync(job.Id);
        var storedBusiness = await fixture.ReadBusinessAsync(business.Id);
        Assert.Equal(TenantProvisioningStatus.Running, stored.Status);
        Assert.Equal("replacement-worker", stored.LeaseOwner);
        Assert.Equal(replacement.LeaseToken, stored.LeaseToken);
        Assert.Equal("PendingProvisioning", storedBusiness.Status);
        Assert.Null(storedBusiness.ActivatedAtUtc);
    }

    [Fact]
    public async Task HostShutdownCancelsHandlerAndLeavesClaimForLeaseExpiryRecovery()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (business, job) = await fixture.AddBusinessAndJobAsync("heartbeat-shutdown");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new DelegateHandler(async (_, token) =>
        {
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException)
            {
                cancelled.TrySetResult();
                throw;
            }
        });
        using var services = CreateServices(fixture, handler);
        var worker = CreateWorker(services);
        using var shutdown = new CancellationTokenSource();

        var processing = worker.ProcessOneAsync("shutdown-worker", shutdown.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        shutdown.Cancel();

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await processing.WaitAsync(TimeSpan.FromSeconds(3));

        var stored = await fixture.ReadJobAsync(job.Id);
        var storedBusiness = await fixture.ReadBusinessAsync(business.Id);
        Assert.Equal(TenantProvisioningStatus.Running, stored.Status);
        Assert.Equal("shutdown-worker", stored.LeaseOwner);
        Assert.NotNull(stored.LeaseToken);
        Assert.Equal("PendingProvisioning", storedBusiness.Status);
        Assert.Null(storedBusiness.ActivatedAtUtc);
    }

    private static ServiceProvider CreateServices(Fixture fixture, ITenantProvisioningHandler handler)
    {
        var services = new ServiceCollection();
        services.AddScoped<PlatformDbContext>(_ => fixture.CreateContext());
        services.AddScoped<TenantProvisioningLeaseService>();
        services.AddScoped(_ => handler);
        return services.BuildServiceProvider();
    }

    private static TenantProvisioningWorker CreateWorker(IServiceProvider services)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Provisioning:WorkerEnabled"] = "true",
                ["Provisioning:WorkerId"] = "integration-worker"
            })
            .Build();
        return new TenantProvisioningWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            NullLogger<TenantProvisioningWorker>.Instance,
            TestLeaseDuration,
            TimeSpan.FromMilliseconds(5),
            TestRenewalInterval);
    }

    private sealed class DelegateHandler(
        Func<TenantProvisioningJob, CancellationToken, Task> action) : ITenantProvisioningHandler
    {
        public Task ProvisionAsync(TenantProvisioningJob job, CancellationToken cancellationToken) =>
            action(job, cancellationToken);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string adminConnection;
        private readonly string schema;
        private readonly string testConnection;

        private Fixture(string adminConnection, string schema, string testConnection)
        {
            this.adminConnection = adminConnection;
            this.schema = schema;
            this.testConnection = testConnection;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var configured = Environment.GetEnvironmentVariable("MG_TEST_POSTGRES");
            if (string.IsNullOrWhiteSpace(configured))
                throw new InvalidOperationException(
                    "MG_TEST_POSTGRES must point to a disposable PostgreSQL test database.");

            var schema = "mg_worker_test_" + Guid.NewGuid().ToString("N");
            var builder = new NpgsqlConnectionStringBuilder(configured);
            var adminConnection = builder.ConnectionString;
            await using (var connection = new NpgsqlConnection(adminConnection))
            {
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand($@"CREATE SCHEMA ""{schema}""", connection);
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

            return new Fixture(adminConnection, schema, testConnection);
        }

        public PlatformDbContext CreateContext() =>
            new(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(testConnection).Options);

        public async Task<(BusinessTenant Business, TenantProvisioningJob Job)> AddBusinessAndJobAsync(
            string slug)
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
                Status = TenantProvisioningStatus.Pending
            };
            await using var context = CreateContext();
            context.Businesses.Add(business);
            context.TenantProvisioningJobs.Add(job);
            await context.SaveChangesAsync();
            return (business, job);
        }

        public async Task ExpireLeaseAsync(Guid jobId)
        {
            await using var connection = new NpgsqlConnection(testConnection);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                UPDATE tenant_provisioning_jobs
                SET lease_expires_at_utc = now() - interval '1 second'
                WHERE id = @job_id;
                """, connection);
            command.Parameters.AddWithValue("job_id", jobId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        public async Task<TenantProvisioningJob> ClaimAsync(string workerId)
        {
            await using var context = CreateContext();
            return await new TenantProvisioningLeaseService(context)
                .ClaimNextAsync(workerId, TestLeaseDuration, CancellationToken.None)
                ?? throw new InvalidOperationException("Expected an expired job to be reclaimable.");
        }

        public async Task<TenantProvisioningJob> ReadJobAsync(Guid jobId)
        {
            await using var context = CreateContext();
            return await context.TenantProvisioningJobs.AsNoTracking()
                .SingleAsync(job => job.Id == jobId);
        }

        public async Task<BusinessTenant> ReadBusinessAsync(Guid businessId)
        {
            await using var context = CreateContext();
            return await context.Businesses.AsNoTracking()
                .SingleAsync(business => business.Id == businessId);
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
