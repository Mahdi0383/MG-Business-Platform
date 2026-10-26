using Microsoft.EntityFrameworkCore;

namespace MG.BusinessPlatform.Infrastructure.Persistence.Migrations;

internal static class ProductAddonsSchemaModel
{
    public static void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.ProductAddonRecord", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<string>("CurrencyCode").IsRequired().HasMaxLength(3).HasColumnType("TEXT");
            b.Property<bool>("IsActive").HasColumnType("INTEGER");
            b.Property<string>("Name").IsRequired().HasMaxLength(120).HasColumnType("TEXT");
            b.Property<decimal>("Price").HasColumnType("TEXT");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("Name");
            b.ToTable("ProductAddons", t =>
                t.HasCheckConstraint("CK_ProductAddon_Price", "CAST(Price AS NUMERIC) >= 0"));
        });

        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.StoreProductAddonLink", b =>
        {
            b.Property<Guid>("ProductId").HasColumnType("TEXT");
            b.Property<Guid>("AddonId").HasColumnType("TEXT");
            b.HasKey("ProductId", "AddonId");
            b.HasIndex("AddonId");
            b.ToTable("StoreProductAddons");
        });

        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.StoreProductAddonLink", b =>
        {
            b.HasOne("MG.BusinessPlatform.Infrastructure.Persistence.ProductAddonRecord", null)
                .WithMany()
                .HasForeignKey("AddonId")
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            b.HasOne("MG.BusinessPlatform.Infrastructure.Persistence.StoreProduct", null)
                .WithMany()
                .HasForeignKey("ProductId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });
    }
}
