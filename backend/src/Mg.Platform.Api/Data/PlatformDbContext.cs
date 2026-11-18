using Mg.Platform.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Mg.Platform.Api.Data;

public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public DbSet<BusinessTenant> Businesses => Set<BusinessTenant>();
    public DbSet<TenantProvisioningJob> TenantProvisioningJobs => Set<TenantProvisioningJob>();

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

        var job = modelBuilder.Entity<TenantProvisioningJob>();
        job.ToTable("tenant_provisioning_jobs");
        job.HasKey(x => x.Id);
        job.Property(x => x.Id).HasColumnName("id");
        job.Property(x => x.BusinessId).HasColumnName("business_id");
        job.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        job.Property(x => x.Attempts).HasColumnName("attempts");
        job.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        job.Property(x => x.NextAttemptAtUtc).HasColumnName("next_attempt_at_utc");
        job.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc");
        job.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
        job.Property(x => x.LastErrorCode).HasColumnName("last_error_code").HasMaxLength(80);
        job.HasOne<BusinessTenant>().WithMany().HasForeignKey(x => x.BusinessId)
            .OnDelete(DeleteBehavior.Restrict);
        job.HasIndex(x => new { x.Status, x.NextAttemptAtUtc, x.CreatedAtUtc });
        job.HasIndex(x => new { x.BusinessId, x.CreatedAtUtc });
    }
}
