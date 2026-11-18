using Mg.Platform.Api.Data;
using Mg.Platform.Api.Models;
using Microsoft.EntityFrameworkCore;
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
        Assert.Equal("slug", entity.FindProperty(nameof(BusinessTenant.Slug))!.GetColumnName());
        Assert.Equal("database_secret_reference",
            entity.FindProperty(nameof(BusinessTenant.DatabaseSecretReference))!.GetColumnName());
        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique && index.Properties.Any(property => property.Name == nameof(BusinessTenant.Slug)));
    }

    [Fact]
    public void MapsProvisioningQueueWithRestrictiveBusinessForeignKey()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(TenantProvisioningJob));

        Assert.NotNull(entity);
        Assert.Equal("tenant_provisioning_jobs", entity!.GetTableName());
        Assert.Equal("business_id",
            entity.FindProperty(nameof(TenantProvisioningJob.BusinessId))!.GetColumnName());

        var foreignKey = Assert.Single(entity.GetForeignKeys());
        Assert.Equal(typeof(BusinessTenant), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
    }
}
