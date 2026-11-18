using Mg.Platform.Api.Endpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Mg.Platform.Api.Provisioning;

/// <summary>
/// Opt-in durable queue consumer. It fails closed unless a reviewed provisioner
/// is registered; no job is claimed before that validation succeeds.
/// </summary>
public sealed class TenantProvisioningWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<TenantProvisioningWorker> logger) : BackgroundService
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RenewalInterval = TimeSpan.FromSeconds(30);

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
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    private async Task ProcessOneAsync(string workerId, CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var leases = services.GetRequiredService<TenantProvisioningLeaseService>();
        var handler = services.GetRequiredService<ITenantProvisioningHandler>();

        _ = await leases.DeadLetterExhaustedAsync(stoppingToken);
        var job = await leases.ClaimNextAsync(workerId, LeaseDuration, stoppingToken);
        if (job is null)
        {
            await Task.Delay(PollInterval, stoppingToken);
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
                await Task.Delay(RenewalInterval, linked.Token);
                await using var heartbeatScope = scopeFactory.CreateAsyncScope();
                var leases = heartbeatScope.ServiceProvider
                    .GetRequiredService<TenantProvisioningLeaseService>();
                if (!await leases.RenewAsync(jobId, workerId, leaseToken, LeaseDuration, linked.Token))
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
