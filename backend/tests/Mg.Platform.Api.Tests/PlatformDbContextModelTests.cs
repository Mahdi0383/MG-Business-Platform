using Mg.Platform.Api.Data;
using Mg.Platform.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Mg.Platform.Api.Tests;

public sealed class PlatformDbContextModelTests
{
    private static PlatformDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql("Host=localhost;Database=mg_platform_model_test;Username=test;Password=test")
            .Options;
        return new PlatformDbContext(options);
    }

    [Fact]
    public void MapsCentralBusinessRegistryToExpectedTableAndColumns()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(BusinessTenant));

        Assert.NotNull(entity);
        Assert.Equal("business_tenants", entity!.GetTableName());
        var table = StoreObjectIdentifier.Table("business_tenants", null);
        Assert.Equal("slug", entity.FindProperty(nameof(BusinessTenant.Slug))!.GetColumnName(table));
        Assert.Equal("database_secret_reference",
            entity.FindProperty(nameof(BusinessTenant.DatabaseSecretReference))!.GetColumnName(table));
        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique && index.Properties.Any(property => property.Name == nameof(BusinessTenant.Slug)));
    }

    [Fact]
    public void MapsBusinessCreationIdempotencyWithGlobalKeyAndRestrictedBusinessReference()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(BusinessCreationRequest));

        Assert.NotNull(entity);
        Assert.Equal("business_creation_requests", entity!.GetTableName());
        var table = StoreObjectIdentifier.Table("business_creation_requests", null);
        Assert.Equal("idempotency_key",
            entity.FindProperty(nameof(BusinessCreationRequest.IdempotencyKey))!.GetColumnName(table));
        Assert.Equal("creator_identity_hash",
            entity.FindProperty(nameof(BusinessCreationRequest.CreatorIdentityHash))!.GetColumnName(table));
        Assert.Equal("request_fingerprint",
            entity.FindProperty(nameof(BusinessCreationRequest.RequestFingerprint))!.GetColumnName(table));
        var primaryKey = Assert.Single(entity.GetKeys());
        Assert.Equal(nameof(BusinessCreationRequest.IdempotencyKey), Assert.Single(primaryKey.Properties).Name);
        var foreignKey = Assert.Single(entity.GetForeignKeys());
        Assert.Equal(typeof(BusinessTenant), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
    }

    [Fact]
    public void MapsProvisioningQueueWithRestrictiveBusinessForeignKey()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(TenantProvisioningJob));

        Assert.NotNull(entity);
        Assert.Equal("tenant_provisioning_jobs", entity!.GetTableName());
        var table = StoreObjectIdentifier.Table("tenant_provisioning_jobs", null);
        Assert.Equal("business_id",
            entity.FindProperty(nameof(TenantProvisioningJob.BusinessId))!.GetColumnName(table));
        Assert.Equal("idempotency_key",
            entity.FindProperty(nameof(TenantProvisioningJob.IdempotencyKey))!.GetColumnName(table));
        Assert.Equal("lease_owner",
            entity.FindProperty(nameof(TenantProvisioningJob.LeaseOwner))!.GetColumnName(table));
        Assert.Equal("lease_expires_at_utc",
            entity.FindProperty(nameof(TenantProvisioningJob.LeaseExpiresAtUtc))!.GetColumnName(table));
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique &&
            index.Properties.Any(property => property.Name == nameof(TenantProvisioningJob.IdempotencyKey)));

        var foreignKey = Assert.Single(entity.GetForeignKeys());
        Assert.Equal(typeof(BusinessTenant), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
    }
}
