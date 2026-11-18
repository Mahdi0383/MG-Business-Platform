using Mg.Platform.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Mg.Platform.Api.Data;

public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public DbSet<BusinessTenant> Businesses => Set<BusinessTenant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var business = modelBuilder.Entity<BusinessTenant>();
        business.ToTable("business_tenants");
        business.HasKey(x => x.Id);
        business.Property(x => x.Name).HasMaxLength(160).IsRequired();
        business.Property(x => x.Slug).HasMaxLength(80).IsRequired();
        business.Property(x => x.Status).HasMaxLength(24).IsRequired();
        business.Property(x => x.DatabaseSecretReference).HasMaxLength(300).IsRequired();
        business.HasIndex(x => x.Slug).IsUnique();
        business.HasIndex(x => new { x.Status, x.CreatedAtUtc });
    }
}
