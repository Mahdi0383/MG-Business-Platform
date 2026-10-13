using MG.BusinessPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MG.BusinessPlatform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BusinessDbContext))]
public sealed class BusinessDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder) => BuildModelCore(modelBuilder);

    internal static void BuildModelCore(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasAnnotation("ProductVersion", "10.0.0")
            .HasAnnotation("Relational:MaxIdentifierLength", 64);

        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.CustomerRecord", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<string>("Address").IsRequired().HasMaxLength(1000).HasColumnType("TEXT");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("TEXT");
            b.Property<string>("DisplayName").IsRequired().HasMaxLength(200).HasColumnType("TEXT");
            b.Property<string>("Email").IsRequired().HasMaxLength(254).HasColumnType("TEXT");
            b.Property<bool>("IsActive").HasColumnType("INTEGER");
            b.Property<string>("Phone").IsRequired().HasMaxLength(40).HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("DisplayName");
            b.HasIndex("Phone");
            b.ToTable("Customers");
        });

        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.SupplierRecord", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<string>("Address").IsRequired().HasMaxLength(1000).HasColumnType("TEXT");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("TEXT");
            b.Property<string>("DisplayName").IsRequired().HasMaxLength(200).HasColumnType("TEXT");
            b.Property<string>("Email").IsRequired().HasMaxLength(254).HasColumnType("TEXT");
            b.Property<bool>("IsActive").HasColumnType("INTEGER");
            b.Property<string>("PaymentTerms").IsRequired().HasMaxLength(500).HasColumnType("TEXT");
            b.Property<string>("Phone").IsRequired().HasMaxLength(40).HasColumnType("TEXT");
            b.Property<string>("TaxIdentifier").IsRequired().HasMaxLength(80).HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("DisplayName");
            b.HasIndex("Phone");
            b.ToTable("Suppliers");
        });

        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.StoreSettings", b =>
        {
            b.Property<int>("Id").ValueGeneratedNever().HasColumnType("INTEGER");
            b.Property<string>("CurrencyCode").IsRequired().HasMaxLength(3).HasColumnType("TEXT");
            b.Property<bool>("OnlineOrderingEnabled").HasColumnType("INTEGER");
            b.Property<bool>("PublicPublicationEnabled").HasColumnType("INTEGER");
            b.Property<string>("PublicHostname").IsRequired().HasMaxLength(253).HasColumnType("TEXT");
            b.Property<string>("StoreName").IsRequired().HasMaxLength(200).HasColumnType("TEXT");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("TEXT");
            b.HasKey("Id");
            b.ToTable("StoreSettings", t => t.HasCheckConstraint("CK_StoreSettings_Singleton", "Id = 1"));
        });

        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.StoreProduct", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<string>("CurrencyCode").IsRequired().HasMaxLength(3).HasColumnType("TEXT");
            b.Property<string>("Description").IsRequired().HasMaxLength(2000).HasColumnType("TEXT");
            b.Property<bool>("IsAvailable").HasColumnType("INTEGER");
            b.Property<string>("Name").IsRequired().HasMaxLength(200).HasColumnType("TEXT");
            b.Property<int>("StockQuantity").HasColumnType("INTEGER");
            b.Property<string>("Sku").IsRequired().HasMaxLength(80).HasColumnType("TEXT");
            b.Property<decimal>("UnitPrice").HasColumnType("TEXT");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("Sku").IsUnique();
            b.ToTable("StoreProducts", t =>
            {
                t.HasCheckConstraint("CK_StoreProduct_Price", "CAST(UnitPrice AS NUMERIC) >= 0");
                t.HasCheckConstraint("CK_StoreProduct_Stock", "StockQuantity >= 0");
            });
        });

        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.CustomerOrder", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("TEXT");
            b.Property<string>("CurrencyCode").IsRequired().HasMaxLength(3).HasColumnType("TEXT");
            b.Property<string>("CustomerName").IsRequired().HasMaxLength(160).HasColumnType("TEXT");
            b.Property<string>("CustomerPhone").IsRequired().HasMaxLength(40).HasColumnType("TEXT");
            b.Property<string>("DeliveryAddress").IsRequired().HasMaxLength(1000).HasColumnType("TEXT");
            b.Property<string>("IdempotencyKey").IsRequired().HasMaxLength(100).HasColumnType("TEXT");
            b.Property<string>("Notes").IsRequired().HasMaxLength(1000).HasColumnType("TEXT");
            b.Property<string>("OrderNumber").IsRequired().HasMaxLength(40).HasColumnType("TEXT");
            b.Property<string>("Status").IsRequired().HasMaxLength(30).HasColumnType("TEXT");
            b.Property<decimal>("TotalAmount").HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("IdempotencyKey").IsUnique();
            b.HasIndex("OrderNumber").IsUnique();
            b.ToTable("CustomerOrders", t => t.HasCheckConstraint("CK_CustomerOrder_Total", "CAST(TotalAmount AS NUMERIC) >= 0"));
        });

        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.CustomerOrderLine", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<Guid>("OrderId").HasColumnType("TEXT");
            b.Property<Guid>("ProductId").HasColumnType("TEXT");
            b.Property<string>("ProductName").IsRequired().HasMaxLength(200).HasColumnType("TEXT");
            b.Property<int>("Quantity").HasColumnType("INTEGER");
            b.Property<decimal>("LineTotal").HasColumnType("TEXT");
            b.Property<decimal>("UnitPrice").HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("OrderId");
            b.ToTable("CustomerOrderLines", t =>
            {
                t.HasCheckConstraint("CK_OrderLine_Price", "CAST(UnitPrice AS NUMERIC) >= 0 AND CAST(LineTotal AS NUMERIC) >= 0");
                t.HasCheckConstraint("CK_OrderLine_Quantity", "Quantity > 0");
            });
        });

        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.CustomerOrderLine", b =>
        {
            b.HasOne("MG.BusinessPlatform.Infrastructure.Persistence.CustomerOrder", "Order")
                .WithMany("Lines")
                .HasForeignKey("OrderId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.Navigation("Order");
        });

        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.CustomerOrder", b =>
        {
            b.Navigation("Lines");
        });
    }
}
