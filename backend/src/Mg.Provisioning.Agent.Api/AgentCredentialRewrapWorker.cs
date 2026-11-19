using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mg.Provisioning.Agent.Api;

/// <summary>
/// Periodically re-encrypts persisted tenant credential bundles with the configured current key.
/// Historical keys must remain configured until the worker has rewrapped all rows.
/// </summary>
public sealed class AgentCredentialRewrapWorker(
    NpgsqlAgentProvisioningHandler handler,
    IConfiguration configuration,
    ILogger<AgentCredentialRewrapWorker> logger) : BackgroundService
{
    private readonly int batchSize = ParseBounded(configuration["Agent:CredentialEncryption:RewrapBatchSize"], 100, 1, 1000);
    private readonly int intervalSeconds = ParseBounded(configuration["Agent:CredentialEncryption:RewrapIntervalSeconds"], 900, 60, 86400);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var totalRewrapped = 0;
                int rewrapped;
                do
                {
                    rewrapped = await handler.RewrapStoredCredentialsAsync(batchSize, stoppingToken);
                    totalRewrapped += rewrapped;
                }
                while (rewrapped == batchSize && !stoppingToken.IsCancellationRequested);

                logger.LogInformation(
                    "Credential key rewrap sweep completed. Rewrapped {CredentialCount} stored tenant bundles with the active key.",
                    totalRewrapped);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Tenant credential key rewrap failed. ExceptionType={ExceptionType}",
                    exception.GetType().Name);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private static int ParseBounded(string? value, int fallback, int minimum, int maximum) =>
        int.TryParse(value, out var parsed)
            ? Math.Clamp(parsed, minimum, maximum)
            : fallback;
}
