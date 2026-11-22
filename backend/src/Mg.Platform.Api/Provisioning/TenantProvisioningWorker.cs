using Mg.Platform.Api.Endpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Mg.Platform.Api.Provisioning;

/// <summary>
/// Opt-in durable queue consumer. It fails closed unless a reviewed provisioner
/// is registered; no job is claimed before that validation succeeds.
/// </summary>
public sealed class TenantProvisioningWorker : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly IConfiguration configuration;
    private readonly ILogger<TenantProvisioningWorker> logger;
    private readonly TimeSpan leaseDuration;
    private readonly TimeSpan pollInterval;
    private readonly TimeSpan renewalInterval;

    private static readonly TimeSpan DefaultLeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultRenewalInterval = TimeSpan.FromSeconds(30);

    public TenantProvisioningWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<TenantProvisioningWorker> logger)
        : this(scopeFactory, configuration, logger,
            DefaultLeaseDuration, DefaultPollInterval, DefaultRenewalInterval)
    {
    }

    internal TenantProvisioningWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<TenantProvisioningWorker> logger,
        TimeSpan leaseDuration,
        TimeSpan pollInterval,
        TimeSpan renewalInterval)
    {
        this.scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (leaseDuration < TimeSpan.FromSeconds(15) || leaseDuration > TimeSpan.FromMinutes(10))
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        if (pollInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
        if (renewalInterval <= TimeSpan.Zero || renewalInterval >= leaseDuration)
            throw new ArgumentOutOfRangeException(nameof(renewalInterval));

        this.leaseDuration = leaseDuration;
        this.pollInterval = pollInterval;
        this.renewalInterval = renewalInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue<bool>("Provisioning:WorkerEnabled"))
        {
            logger.LogInformation("Tenant provisioning worker is disabled.");
            return;
        }

        var workerId = configuration["Provisioning:WorkerId"];
        if (string.IsNullOrWhiteSpace(workerId) || workerId.Length > 120)
            throw new InvalidOperationException(
                "Provisioning:WorkerId must be configured when the worker is enabled.");

        await using (var validationScope = scopeFactory.CreateAsyncScope())
            _ = validationScope.ServiceProvider.GetRequiredService<ITenantProvisioningHandler>();

        logger.LogInformation("Tenant provisioning worker started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOneAsync(workerId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError("Provisioning worker iteration failed ({ExceptionType}).",
                    exception.GetType().Name);
                await Task.Delay(pollInterval, stoppingToken);
            }
        }
    }

    internal async Task ProcessOneAsync(string workerId, CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var leases = services.GetRequiredService<TenantProvisioningLeaseService>();
        var handler = services.GetRequiredService<ITenantProvisioningHandler>();

        _ = await leases.DeadLetterExhaustedAsync(stoppingToken);
        var job = await leases.ClaimNextAsync(workerId, leaseDuration, stoppingToken);
        if (job is null)
        {
            await Task.Delay(pollInterval, stoppingToken);
            return;
        }

        var leaseToken = job.LeaseToken
            ?? throw new InvalidOperationException("Claimed provisioning job has no fencing token.");
        using var handlerCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using var heartbeatStop = new CancellationTokenSource();
        var heartbeat = MaintainLeaseAsync(job.Id, workerId, leaseToken, handlerCancellation,
            heartbeatStop.Token, stoppingToken);

        try
        {
            await handler.ProvisionAsync(job, handlerCancellation.Token);
            heartbeatStop.Cancel();
            var leaseHealthy = await heartbeat;
            if (leaseHealthy)
            {
                var completed = await leases.MarkSucceededAsync(job.Id, workerId, leaseToken, stoppingToken);
                if (!completed)
                    logger.LogWarning("Provisioning job {JobId} lost its lease before completion.", job.Id);
            }
        }
        catch (OperationCanceledException) when (
            stoppingToken.IsCancellationRequested || handlerCancellation.IsCancellationRequested)
        {
            heartbeatStop.Cancel();
            _ = await heartbeat;
        }
        catch (TenantProvisioningException exception)
        {
            heartbeatStop.Cancel();
            _ = await heartbeat;
            if (!stoppingToken.IsCancellationRequested && !handlerCancellation.IsCancellationRequested)
                _ = await leases.MarkFailedAsync(job.Id, workerId, leaseToken, exception.SafeErrorCode,
                    exception.Permanent, stoppingToken);
        }
        catch (Exception exception)
        {
            heartbeatStop.Cancel();
            _ = await heartbeat;
            if (!stoppingToken.IsCancellationRequested && !handlerCancellation.IsCancellationRequested)
            {
                logger.LogError("Provisioning job {JobId} failed ({ExceptionType}).",
                    job.Id, exception.GetType().Name);
                _ = await leases.MarkFailedAsync(job.Id, workerId, leaseToken,
                    "PROVISIONING_FAILED", permanent: false, stoppingToken);
            }
        }
        finally
        {
            heartbeatStop.Cancel();
        }
    }

    private async Task<bool> MaintainLeaseAsync(
        Guid jobId,
        string workerId,
        Guid leaseToken,
        CancellationTokenSource handlerCancellation,
        CancellationToken heartbeatStop,
        CancellationToken stoppingToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            heartbeatStop, stoppingToken);
        try
        {
            while (true)
            {
                await Task.Delay(renewalInterval, linked.Token);
                await using var heartbeatScope = scopeFactory.CreateAsyncScope();
                var leases = heartbeatScope.ServiceProvider
                    .GetRequiredService<TenantProvisioningLeaseService>();
                if (!await leases.RenewAsync(jobId, workerId, leaseToken, leaseDuration, linked.Token))
                {
                    handlerCancellation.Cancel();
                    return false;
                }
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return !stoppingToken.IsCancellationRequested && !handlerCancellation.IsCancellationRequested;
        }
        catch (Exception exception)
        {
            logger.LogError("Lease heartbeat failed ({ExceptionType}).", exception.GetType().Name);
            handlerCancellation.Cancel();
            return false;
        }
    }
}
