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
        business.Property(x => x.Id).HasColumnName("id");
        business.Property(x => x.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
        business.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(80).IsRequired();
        business.Property(x => x.Status).HasColumnName("status").HasMaxLength(24).IsRequired();
        business.Property(x => x.DatabaseSecretReference).HasColumnName("database_secret_reference").HasMaxLength(300).IsRequired();
        business.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        business.Property(x => x.ActivatedAtUtc).HasColumnName("activated_at_utc");
        business.HasIndex(x => x.Slug).IsUnique();
        business.HasIndex(x => new { x.Status, x.CreatedAtUtc });
    }
}
