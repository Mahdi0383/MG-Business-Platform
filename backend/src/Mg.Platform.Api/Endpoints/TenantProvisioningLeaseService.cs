using Mg.Platform.Api.Data;
using Mg.Platform.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Mg.Platform.Api.Endpoints;

/// <summary>
/// Atomic PostgreSQL queue lease operations. This is infrastructure only:
/// it does not create tenant databases or activate tenants by itself.
/// </summary>
public sealed class TenantProvisioningLeaseService(PlatformDbContext db)
{
    private const int MaxWorkerIdLength = 120;
    private const int MaxAttempts = 8;

    public async Task<TenantProvisioningJob?> ClaimNextAsync(
        string workerId, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        ValidateLease(workerId, leaseDuration);
        var leaseSeconds = (int)leaseDuration.TotalSeconds;

        // A single UPDATE statement claims one due job. SKIP LOCKED allows multiple
        // replicas to compete without waiting on a row another worker has locked.
        return await db.TenantProvisioningJobs
            .FromSqlInterpolated($"""
                WITH candidate AS (
                    SELECT id
                    FROM tenant_provisioning_jobs
                    WHERE (
                        (status IN ('Pending', 'Retry') AND next_attempt_at_utc <= now())
                        OR (status = 'Running' AND lease_expires_at_utc <= now())
                    )
                    AND attempts < {MaxAttempts}
                    ORDER BY next_attempt_at_utc, created_at_utc
                    FOR UPDATE SKIP LOCKED
                    LIMIT 1
                )
                UPDATE tenant_provisioning_jobs AS job
                SET status = 'Running',
                    attempts = job.attempts + 1,
                    lease_owner = {workerId},
                    lease_expires_at_utc = now() + ({leaseSeconds} * interval '1 second'),
                    started_at_utc = COALESCE(job.started_at_utc, now())
                FROM candidate
                WHERE job.id = candidate.id
                RETURNING job.*
                """)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> RenewAsync(
        Guid jobId, string workerId, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        ValidateLease(workerId, leaseDuration);
        var expiry = DateTimeOffset.UtcNow.Add(leaseDuration);
        var updated = await db.TenantProvisioningJobs
            .Where(job => job.Id == jobId
                && job.Status == TenantProvisioningStatus.Running
                && job.LeaseOwner == workerId
                && job.LeaseExpiresAtUtc > DateTimeOffset.UtcNow)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.LeaseExpiresAtUtc, expiry), cancellationToken);
        return updated == 1;
    }

    public async Task<bool> MarkSucceededAsync(
        Guid jobId, string workerId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workerId) || workerId.Length > MaxWorkerIdLength)
            throw new ArgumentException("Worker identifier is invalid.", nameof(workerId));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var updated = await db.TenantProvisioningJobs
            .Where(job => job.Id == jobId
                && job.Status == TenantProvisioningStatus.Running
                && job.LeaseOwner == workerId
                && job.LeaseExpiresAtUtc > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.Status, TenantProvisioningStatus.Succeeded)
                .SetProperty(job => job.CompletedAtUtc, now)
                .SetProperty(job => job.LeaseOwner, (string?)null)
                .SetProperty(job => job.LeaseExpiresAtUtc, (DateTimeOffset?)null), cancellationToken);

        if (updated == 1)
        {
            var businessId = await db.TenantProvisioningJobs
                .Where(job => job.Id == jobId)
                .Select(job => job.BusinessId)
                .SingleAsync(cancellationToken);
            var activated = await db.Businesses
                .Where(business => business.Id == businessId
                    && business.Status == "PendingProvisioning")
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(business => business.Status, "Active")
                    .SetProperty(business => business.ActivatedAtUtc, now), cancellationToken);
            if (activated != 1)
                throw new InvalidOperationException("Business state was not eligible for activation; provisioning completion was rolled back.");
        }

        await transaction.CommitAsync(cancellationToken);
        return updated == 1;
    }

    public async Task<bool> MarkFailedAsync(
        Guid jobId, string workerId, string safeErrorCode, bool permanent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workerId) || workerId.Length > MaxWorkerIdLength)
            throw new ArgumentException("Worker identifier is invalid.", nameof(workerId));
        if (string.IsNullOrWhiteSpace(safeErrorCode)
            || safeErrorCode.Length > 80
            || safeErrorCode.Any(character => !(char.IsAsciiLetterUpper(character)
                || char.IsAsciiDigit(character) || character == '_')))
            throw new ArgumentException("Error code must be a safe uppercase identifier.", nameof(safeErrorCode));

        var now = DateTimeOffset.UtcNow;
        var job = await db.TenantProvisioningJobs.AsNoTracking()
            .Where(item => item.Id == jobId
                && item.Status == TenantProvisioningStatus.Running
                && item.LeaseOwner == workerId
                && item.LeaseExpiresAtUtc > now)
            .Select(item => new { item.Attempts, item.Id })
            .SingleOrDefaultAsync(cancellationToken);
        if (job is null) return false;

        var deadLetter = permanent || job.Attempts >= MaxAttempts;
        var backoffSeconds = Math.Min(3600, 15 * Math.Pow(2, Math.Max(0, job.Attempts - 1)));
        var jitterSeconds = (job.Id.GetHashCode() & 0x7fffffff) % 13;
        var nextAttempt = now.AddSeconds(backoffSeconds + jitterSeconds);

        var updated = await db.TenantProvisioningJobs
            .Where(item => item.Id == jobId
                && item.Status == TenantProvisioningStatus.Running
                && item.LeaseOwner == workerId
                && item.LeaseExpiresAtUtc > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, deadLetter
                    ? TenantProvisioningStatus.DeadLetter : TenantProvisioningStatus.Retry)
                .SetProperty(item => item.NextAttemptAtUtc, nextAttempt)
                .SetProperty(item => item.LastErrorCode, safeErrorCode)
                .SetProperty(item => item.LeaseOwner, (string?)null)
                .SetProperty(item => item.LeaseExpiresAtUtc, (DateTimeOffset?)null), cancellationToken);
        return updated == 1;
    }

    private static void ValidateLease(string workerId, TimeSpan leaseDuration)
    {
        if (string.IsNullOrWhiteSpace(workerId) || workerId.Length > MaxWorkerIdLength)
            throw new ArgumentException("Worker identifier is invalid.", nameof(workerId));
        if (leaseDuration < TimeSpan.FromSeconds(15) || leaseDuration > TimeSpan.FromMinutes(10))
            throw new ArgumentOutOfRangeException(nameof(leaseDuration),
                "Lease duration must be between 15 seconds and 10 minutes.");
    }
}
