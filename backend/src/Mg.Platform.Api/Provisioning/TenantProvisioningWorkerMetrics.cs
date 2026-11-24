using System.Diagnostics.Metrics;

namespace Mg.Platform.Api.Provisioning;

/// <summary>
/// Low-cardinality worker telemetry. Never add tenant IDs, job IDs, worker IDs,
/// exception messages or credential-derived values as metric tags.
/// </summary>
internal static class TenantProvisioningWorkerMetrics
{
    public const string MeterName = "MG.Platform.Provisioning";

    private static readonly Meter Meter = new(MeterName, "1.0.0");
    private static readonly Counter<long> JobsClaimed =
        Meter.CreateCounter<long>("mg.provisioning.jobs.claimed");
    private static readonly Counter<long> JobAttempts =
        Meter.CreateCounter<long>("mg.provisioning.jobs.attempts");
    private static readonly Counter<long> LeaseRenewals =
        Meter.CreateCounter<long>("mg.provisioning.lease.renewals");
    private static readonly Counter<long> IterationFailures =
        Meter.CreateCounter<long>("mg.provisioning.worker.iteration.failures");
    private static readonly Histogram<double> JobDuration =
        Meter.CreateHistogram<double>("mg.provisioning.jobs.duration", unit: "s");

    public static void RecordJobClaimed() => JobsClaimed.Add(1);

    public static void RecordJobCompleted(string outcome, TimeSpan duration)
    {
        JobsCompleted.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        JobDuration.Record(
            duration.TotalSeconds,
            new KeyValuePair<string, object?>("outcome", outcome));
    }

    public static void RecordLeaseRenewal(string outcome) =>
        LeaseRenewals.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public static void RecordIterationFailure() => IterationFailures.Add(1);
}
