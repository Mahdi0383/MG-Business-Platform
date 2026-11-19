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

    public async Task<(BusinessSummary? Business, bool Conflict)> CreateAsync(
        CreateBusinessRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        var slug = request.Slug.Trim().ToLowerInvariant();
        if (await db.Businesses.AnyAsync(x => x.Slug == slug, cancellationToken))
            return (null, true);

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

        // Persist both rows in one EF Core transaction so no tenant is left without work.
        db.Businesses.Add(tenant);
        db.TenantProvisioningJobs.Add(provisioningJob);
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
            return (null, true);
        }

        return (new BusinessSummary(tenant.Id, tenant.Name, tenant.Slug, tenant.Status, tenant.CreatedAtUtc), false);
    }
}
