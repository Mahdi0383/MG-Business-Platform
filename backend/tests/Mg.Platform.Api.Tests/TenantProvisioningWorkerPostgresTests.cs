using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Mg.Platform.Api.Data;
using Mg.Platform.Api.Endpoints;
using Mg.Platform.Api.Models;
using Mg.Platform.Api.Provisioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        Assert.Equal(claimed.LeaseToken, renewed.LeaseToken);
        Assert.Equal("heartbeat-worker", renewed.LeaseOwner);

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
    public async Task EmitsLowCardinalityMetricsForClaimCompletionAndLeaseRenewal()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddBusinessAndJobAsync("worker-metrics");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new DelegateHandler(async (_, token) =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(token);
        });
        using var services = CreateServices(fixture, handler);
        var observations = new ConcurrentBag<MetricObservation>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == TenantProvisioningWorkerMetrics.MeterName)
                    meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
            observations.Add(new MetricObservation(
                instrument.Name,
                measurement,
                tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value?.ToString()))));
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
            observations.Add(new MetricObservation(
                instrument.Name,
                measurement,
                tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value?.ToString()))));
        listener.Start();

        var worker = CreateWorker(services, renewalInterval: TimeSpan.FromMilliseconds(10));
        var processing = worker.ProcessOneAsync("metrics-worker-id-not-a-tag", CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Task.Delay(TimeSpan.FromMilliseconds(35));
        release.TrySetResult();
        await processing.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Contains(observations, item => item.Name == "mg.provisioning.jobs.claimed");
        Assert.Contains(observations, item =>
            item.Name == "mg.provisioning.jobs.attempts"
            && item.Tags.TryGetValue("outcome", out var outcome)
            && outcome == "succeeded");
        Assert.Contains(observations, item =>
            item.Name == "mg.provisioning.lease.renewals"
            && item.Tags.TryGetValue("outcome", out var outcome)
            && outcome == "succeeded");
        Assert.Contains(observations, item =>
            item.Name == "mg.provisioning.jobs.duration"
            && item.Tags.TryGetValue("outcome", out var outcome)
            && outcome == "succeeded");
        Assert.All(observations, item => Assert.All(item.Tags.Keys, key => Assert.Equal("outcome", key)));
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
    public async Task HeartbeatStorageFailureCancelsHandlerAndLeavesClaimForRecovery()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (business, job) = await fixture.AddBusinessAndJobAsync("heartbeat-storage-failure");
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
        var logger = new RecordingLogger<TenantProvisioningWorker>();
        var worker = CreateWorker(services, logger);

        var processing = worker.ProcessOneAsync("heartbeat-db-failure-worker", CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        fixture.MakeHeartbeatConnectionUnavailable();

        try
        {
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await processing.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            fixture.RestoreConnection();
        }

        var stored = await fixture.ReadJobAsync(job.Id);
        var storedBusiness = await fixture.ReadBusinessAsync(business.Id);
        Assert.Equal(TenantProvisioningStatus.Running, stored.Status);
        Assert.Equal("heartbeat-db-failure-worker", stored.LeaseOwner);
        Assert.NotNull(stored.LeaseToken);
        Assert.Equal("PendingProvisioning", storedBusiness.Status);
        Assert.Null(storedBusiness.ActivatedAtUtc);

        await fixture.ExpireLeaseAsync(job.Id);
        var recovered = await fixture.ClaimAsync("heartbeat-recovery-worker");
        Assert.Equal(job.Id, recovered.Id);
        Assert.Equal("heartbeat-recovery-worker", recovered.LeaseOwner);
        Assert.NotEqual(stored.LeaseToken, recovered.LeaseToken);
        Assert.Contains(logger.Messages, message =>
            message.Contains("Lease heartbeat failed", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message =>
            message.Contains("127.0.0.1", StringComparison.Ordinal)
            || message.Contains("unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RepeatedHeartbeatCompletionRacesNeverActivateAfterLeaseExpiry()
    {
        await using var fixture = await Fixture.CreateAsync();

        for (var attempt = 0; attempt < 8; attempt++)
        {
            var (business, job) = await fixture.AddBusinessAndJobAsync($"heartbeat-race-{attempt}");
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new DelegateHandler(async (_, _) =>
            {
                started.TrySetResult();
                await release.Task;
            });
            using var services = CreateServices(fixture, handler);
            var worker = CreateWorker(services, renewalInterval: TimeSpan.FromMilliseconds(5));

            var processing = worker.ProcessOneAsync($"race-worker-{attempt}", CancellationToken.None);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Task.Delay(TimeSpan.FromMilliseconds(12));
            await fixture.ExpireLeaseAsync(job.Id);
            release.TrySetResult();
            await processing.WaitAsync(TimeSpan.FromSeconds(3));

            var stored = await fixture.ReadJobAsync(job.Id);
            var storedBusiness = await fixture.ReadBusinessAsync(business.Id);
            Assert.Equal(TenantProvisioningStatus.Running, stored.Status);
            Assert.Equal($"race-worker-{attempt}", stored.LeaseOwner);
            Assert.NotNull(stored.LeaseToken);
            Assert.Equal("PendingProvisioning", storedBusiness.Status);
            Assert.Null(storedBusiness.ActivatedAtUtc);

            await fixture.MarkJobDeadLetterAsync(job.Id);
        }
    }

    [Fact]
    public async Task RepeatedHeartbeatRenewalAndHandlerCompletionRacesCompleteWithCurrentLease()
    {
        await using var fixture = await Fixture.CreateAsync();

        for (var attempt = 0; attempt < 8; attempt++)
        {
            var (business, job) = await fixture.AddBusinessAndJobAsync($"heartbeat-success-race-{attempt}");
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new DelegateHandler(async (_, _) =>
            {
                started.TrySetResult();
                await Task.Delay(TimeSpan.FromMilliseconds(5));
            });
            using var services = CreateServices(fixture, handler);
            var worker = CreateWorker(services, renewalInterval: TimeSpan.FromMilliseconds(5));

            var processing = worker.ProcessOneAsync($"success-race-worker-{attempt}", CancellationToken.None);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await processing.WaitAsync(TimeSpan.FromSeconds(3));

            var stored = await fixture.ReadJobAsync(job.Id);
            var storedBusiness = await fixture.ReadBusinessAsync(business.Id);
            Assert.Equal(TenantProvisioningStatus.Succeeded, stored.Status);
            Assert.Null(stored.LeaseOwner);
            Assert.Null(stored.LeaseToken);
            Assert.Null(stored.LeaseExpiresAtUtc);
            Assert.Equal("Active", storedBusiness.Status);
            Assert.NotNull(storedBusiness.ActivatedAtUtc);
        }
    }

    [Fact]
    public async Task HandlerExceptionStoresOnlySafeFallbackCodeAndReleasesLease()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (business, job) = await fixture.AddBusinessAndJobAsync("heartbeat-handler-error");
        var handler = new DelegateHandler((_, _) =>
            Task.FromException(new InvalidOperationException("password=must-never-be-persisted")));
        using var services = CreateServices(fixture, handler);
        var logger = new RecordingLogger<TenantProvisioningWorker>();
        var worker = CreateWorker(services, logger);

        await worker.ProcessOneAsync("handler-error-worker", CancellationToken.None);

        var stored = await fixture.ReadJobAsync(job.Id);
        var storedBusiness = await fixture.ReadBusinessAsync(business.Id);
        Assert.Equal(TenantProvisioningStatus.Retry, stored.Status);
        Assert.Equal("PROVISIONING_FAILED", stored.LastErrorCode);
        Assert.DoesNotContain("password", stored.LastErrorCode, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(logger.Messages, message =>
            message.Contains("InvalidOperationException", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message =>
            message.Contains("password=must-never-be-persisted", StringComparison.Ordinal));
        Assert.Null(stored.LeaseOwner);
        Assert.Null(stored.LeaseToken);
        Assert.Null(stored.LeaseExpiresAtUtc);
        Assert.Equal("PendingProvisioning", storedBusiness.Status);
        Assert.Null(storedBusiness.ActivatedAtUtc);
    }

    [Fact]
    public async Task CompletionRacingWithLeaseExpiryCannotActivateBusiness()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (business, job) = await fixture.AddBusinessAndJobAsync("heartbeat-completion-race");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new DelegateHandler(async (_, _) =>
        {
            started.TrySetResult();
            await release.Task;
        });
        using var services = CreateServices(fixture, handler);
        var worker = CreateWorker(services);

        var processing = worker.ProcessOneAsync("completion-race-worker", CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await fixture.ExpireLeaseAsync(job.Id);
        release.TrySetResult();
        await processing.WaitAsync(TimeSpan.FromSeconds(3));

        var stored = await fixture.ReadJobAsync(job.Id);
        var storedBusiness = await fixture.ReadBusinessAsync(business.Id);
        Assert.Equal(TenantProvisioningStatus.Running, stored.Status);
        Assert.Equal("completion-race-worker", stored.LeaseOwner);
        Assert.NotNull(stored.LeaseToken);
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

        var expiryAtShutdown = stored.LeaseExpiresAtUtc;
        await Task.Delay(TestRenewalInterval + TestRenewalInterval);
        var afterShutdown = await fixture.ReadJobAsync(job.Id);
        Assert.Equal(expiryAtShutdown, afterShutdown.LeaseExpiresAtUtc);
    }

    private static ServiceProvider CreateServices(Fixture fixture, ITenantProvisioningHandler handler)
    {
        var services = new ServiceCollection();
        services.AddScoped<PlatformDbContext>(_ => fixture.CreateContext());
        services.AddScoped<TenantProvisioningLeaseService>();
        services.AddScoped(_ => handler);
        return services.BuildServiceProvider();
    }

    private static TenantProvisioningWorker CreateWorker(
        IServiceProvider services,
        ILogger<TenantProvisioningWorker>? logger = null,
        TimeSpan? renewalInterval = null)
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
            logger ?? NullLogger<TenantProvisioningWorker>.Instance,
            TestLeaseDuration,
            TimeSpan.FromMilliseconds(5),
            renewalInterval ?? TestRenewalInterval);
    }

    private sealed record MetricObservation(
        string Name,
        double Value,
        IReadOnlyDictionary<string, string?> Tags);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
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
        private string testConnection;
        private readonly string stableTestConnection;

        private Fixture(string adminConnection, string schema, string testConnection)
        {
            this.adminConnection = adminConnection;
            this.schema = schema;
            this.testConnection = testConnection;
            stableTestConnection = testConnection;
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

        public void MakeHeartbeatConnectionUnavailable()
        {
            var builder = new NpgsqlConnectionStringBuilder(testConnection)
            {
                Host = "127.0.0.1",
                Port = 1,
                Database = "unavailable"
            };
            testConnection = builder.ConnectionString;
        }

        public void RestoreConnection() => testConnection = stableTestConnection;

        public async Task MarkJobDeadLetterAsync(Guid jobId)
        {
            await using var connection = new NpgsqlConnection(testConnection);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                UPDATE tenant_provisioning_jobs
                SET status = 'DeadLetter',
                    lease_owner = NULL,
                    lease_token = NULL,
                    lease_expires_at_utc = NULL
                WHERE id = @job_id;
                """, connection);
            command.Parameters.AddWithValue("job_id", jobId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
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
