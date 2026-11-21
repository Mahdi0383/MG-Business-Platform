using System.Security.Cryptography;
using System.Text;
using Mg.Platform.Api.Contracts;
using Mg.Platform.Api.Data;
using Mg.Platform.Api.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Mg.Platform.Api.Endpoints;

public sealed class BusinessRegistryService(PlatformDbContext db)
{
    public async Task<IReadOnlyList<BusinessSummary>> ListAsync(CancellationToken cancellationToken) =>
        await db.Businesses.AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new BusinessSummary(x.Id, x.Name, x.Slug, x.Status, x.CreatedAtUtc))
            .ToListAsync(cancellationToken);

    public async Task<(BusinessSummary? Business, bool Conflict, bool Replayed)> CreateAsync(
        CreateBusinessRequest request,
        Guid idempotencyKey,
        string issuer,
        string subject,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (idempotencyKey == Guid.Empty)
            throw new ArgumentException("A non-empty idempotency key is required.", nameof(idempotencyKey));
        if (string.IsNullOrWhiteSpace(issuer) || issuer.Length > 500)
            throw new ArgumentException("A validated OIDC issuer is required.", nameof(issuer));
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 500)
            throw new ArgumentException("A validated OIDC subject is required.", nameof(subject));

        var name = request.Name.Trim();
        var slug = request.Slug.Trim().ToLowerInvariant();
        var creatorIdentityHash = HashUtf8($"{issuer}\n{subject}");
        var requestFingerprint = HashUtf8($"{name.Length}:{name}{slug.Length}:{slug}");

        var previous = await db.BusinessCreationRequests.AsNoTracking()
            .SingleOrDefaultAsync(item => item.IdempotencyKey == idempotencyKey, cancellationToken);
        if (previous is not null)
            return await ReplayOrConflictAsync(
                previous, creatorIdentityHash, requestFingerprint, cancellationToken);

        if (await db.Businesses.AnyAsync(x => x.Slug == slug, cancellationToken))
            return (null, true, false);

        var tenantId = Guid.NewGuid();
        var tenant = new BusinessTenant
        {
            Id = tenantId,
            Name = name,
            Slug = slug,
            Status = "PendingProvisioning",
            // Only a secret-manager reference is stored here, never a connection string/password.
            DatabaseSecretReference = $"tenants/{tenantId:N}/postgres"
        };

        var provisioningJob = new TenantProvisioningJob
        {
            BusinessId = tenant.Id,
            Status = TenantProvisioningStatus.Pending
        };
        var creationRequest = new BusinessCreationRequest
        {
            IdempotencyKey = idempotencyKey,
            CreatorIdentityHash = creatorIdentityHash,
            RequestFingerprint = requestFingerprint,
            BusinessId = tenant.Id
        };

        // The business, durable outbox job and idempotency binding are one atomic unit.
        db.Businesses.Add(tenant);
        db.TenantProvisioningJobs.Add(provisioningJob);
        db.BusinessCreationRequests.Add(creationRequest);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "uq_business_tenants_slug"
            })
        {
            return (null, true, false);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "pk_business_creation_requests"
            })
        {
            // A concurrent request committed this key first. SaveChanges rolled back
            // this request's business + job rows; clear tracked Added entities before replay lookup.
            db.ChangeTracker.Clear();
            var winner = await db.BusinessCreationRequests.AsNoTracking()
                .SingleOrDefaultAsync(item => item.IdempotencyKey == idempotencyKey, cancellationToken);
            if (winner is null)
                return (null, true, false);
            return await ReplayOrConflictAsync(
                winner, creatorIdentityHash, requestFingerprint, cancellationToken);
        }

        return (new BusinessSummary(tenant.Id, tenant.Name, tenant.Slug, tenant.Status, tenant.CreatedAtUtc),
            false, false);
    }

    private async Task<(BusinessSummary? Business, bool Conflict, bool Replayed)> ReplayOrConflictAsync(
        BusinessCreationRequest previous,
        string creatorIdentityHash,
        string requestFingerprint,
        CancellationToken cancellationToken)
    {
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(previous.CreatorIdentityHash),
                Encoding.ASCII.GetBytes(creatorIdentityHash))
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(previous.RequestFingerprint),
                Encoding.ASCII.GetBytes(requestFingerprint)))
        {
            return (null, true, false);
        }

        var business = await db.Businesses.AsNoTracking()
            .Where(item => item.Id == previous.BusinessId)
            .Select(item => new BusinessSummary(
                item.Id, item.Name, item.Slug, item.Status, item.CreatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
        return business is null ? (null, true, false) : (business, false, true);
    }

    private static string HashUtf8(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
