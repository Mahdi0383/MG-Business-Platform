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

        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.LedgerAccountRecord", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<string>("AccountType").IsRequired().HasMaxLength(20).HasColumnType("TEXT");
            b.Property<string>("Code").IsRequired().HasMaxLength(20).HasColumnType("TEXT");
            b.Property<bool>("IsActive").HasColumnType("INTEGER");
            b.Property<string>("Name").IsRequired().HasMaxLength(200).HasColumnType("TEXT");
            b.Property<bool>("IsPostingAllowed").HasColumnType("INTEGER");
            b.HasKey("Id");
            b.HasIndex("Code").IsUnique();
            b.ToTable("LedgerAccounts");
        });

        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.SalesInvoiceRecord", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<string>("CurrencyCode").IsRequired().HasMaxLength(3).HasColumnType("TEXT");
            b.Property<decimal>("CostTotal").HasColumnType("TEXT");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("TEXT");
            b.Property<string>("CustomerName").IsRequired().HasMaxLength(160).HasColumnType("TEXT");
            b.Property<string>("CustomerPhone").IsRequired().HasMaxLength(40).HasColumnType("TEXT");
            b.Property<string>("InvoiceNumber").IsRequired().HasMaxLength(40).HasColumnType("TEXT");
            b.Property<DateOnly>("IssueDate").HasColumnType("TEXT");
            b.Property<Guid>("OrderId").HasColumnType("TEXT");
            b.Property<decimal>("PaidAmount").HasColumnType("TEXT");
            b.Property<string>("Status").IsRequired().HasMaxLength(30).HasColumnType("TEXT");
            b.Property<decimal>("TotalAmount").HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("InvoiceNumber").IsUnique();
            b.HasIndex("OrderId").IsUnique();
            b.ToTable("SalesInvoices", t => t.HasCheckConstraint("CK_SalesInvoice_Amounts", "CAST(TotalAmount AS NUMERIC) >= 0 AND CAST(CostTotal AS NUMERIC) >= 0 AND CAST(PaidAmount AS NUMERIC) >= 0 AND CAST(PaidAmount AS NUMERIC) <= CAST(TotalAmount AS NUMERIC)"));
        });

        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.SalesInvoiceLineRecord", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<decimal>("CostTotal").HasColumnType("TEXT");
            b.Property<Guid>("InvoiceId").HasColumnType("TEXT");
            b.Property<decimal>("LineTotal").HasColumnType("TEXT");
            b.Property<string>("ProductName").IsRequired().HasMaxLength(200).HasColumnType("TEXT");
            b.Property<Guid>("ProductId").HasColumnType("TEXT");
            b.Property<int>("Quantity").HasColumnType("INTEGER");
            b.Property<decimal>("UnitCost").HasColumnType("TEXT");
            b.Property<decimal>("UnitPrice").HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("InvoiceId");
            b.ToTable("SalesInvoiceLines", t =>
            {
                t.HasCheckConstraint("CK_SalesInvoiceLine_Amounts", "CAST(UnitPrice AS NUMERIC) >= 0 AND CAST(UnitCost AS NUMERIC) >= 0 AND CAST(LineTotal AS NUMERIC) >= 0 AND CAST(CostTotal AS NUMERIC) >= 0");
                t.HasCheckConstraint("CK_SalesInvoiceLine_Quantity", "Quantity > 0");
            });
        });

        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.JournalEntryRecord", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<string>("CurrencyCode").IsRequired().HasMaxLength(3).HasColumnType("TEXT");
            b.Property<DateOnly>("EntryDate").HasColumnType("TEXT");
            b.Property<string>("Description").IsRequired().HasMaxLength(500).HasColumnType("TEXT");
            b.Property<string>("EntryNumber").IsRequired().HasMaxLength(40).HasColumnType("TEXT");
            b.Property<Guid>("SourceId").HasColumnType("TEXT");
            b.Property<string>("SourceType").IsRequired().HasMaxLength(40).HasColumnType("TEXT");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("EntryNumber").IsUnique();
            b.HasIndex("SourceType", "SourceId").IsUnique();
            b.ToTable("JournalEntries");
        });

        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.JournalLineRecord", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<decimal>("Amount").HasColumnType("TEXT");
            b.Property<string>("AccountCode").IsRequired().HasMaxLength(20).HasColumnType("TEXT");
            b.Property<Guid>("AccountId").HasColumnType("TEXT");
            b.Property<string>("AccountName").IsRequired().HasMaxLength(200).HasColumnType("TEXT");
            b.Property<Guid>("JournalEntryId").HasColumnType("TEXT");
            b.Property<string>("Side").IsRequired().HasMaxLength(10).HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("AccountId");
            b.HasIndex("JournalEntryId");
            b.ToTable("JournalLines", t =>
            {
                t.HasCheckConstraint("CK_JournalLine_Amount", "CAST(Amount AS NUMERIC) > 0");
                t.HasCheckConstraint("CK_JournalLine_Side", "Side IN ('Debit','Credit')");
            });
        });

        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.PaymentRecord", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<decimal>("Amount").HasColumnType("TEXT");
            b.Property<string>("CurrencyCode").IsRequired().HasMaxLength(3).HasColumnType("TEXT");
            b.Property<Guid>("InvoiceId").HasColumnType("TEXT");
            b.Property<string>("Method").IsRequired().HasMaxLength(30).HasColumnType("TEXT");
            b.Property<string>("Notes").IsRequired().HasMaxLength(500).HasColumnType("TEXT");
            b.Property<string>("PaymentNumber").IsRequired().HasMaxLength(40).HasColumnType("TEXT");
            b.Property<string>("Reference").IsRequired().HasMaxLength(100).HasColumnType("TEXT");
            b.Property<DateTimeOffset>("OccurredAt").HasColumnType("TEXT");
            b.Property<Guid>("JournalEntryId").HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("InvoiceId");
            b.HasIndex("JournalEntryId");
            b.HasIndex("PaymentNumber").IsUnique();
            b.ToTable("Payments", t => t.HasCheckConstraint("CK_Payment_Amount", "CAST(Amount AS NUMERIC) > 0"));
        });

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
            b.Property<decimal>("UnitCost").HasColumnType("TEXT");
            b.Property<decimal>("UnitPrice").HasColumnType("TEXT");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("Sku").IsUnique();
            b.ToTable("StoreProducts", t =>
            {
                t.HasCheckConstraint("CK_StoreProduct_Cost", "CAST(UnitCost AS NUMERIC) >= 0");
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

        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.StockMovementRecord", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<string>("CurrencyCode").IsRequired().HasMaxLength(3).HasColumnType("TEXT");
            b.Property<string>("MovementType").IsRequired().HasMaxLength(40).HasColumnType("TEXT");
            b.Property<DateTimeOffset>("OccurredAt").HasColumnType("TEXT");
            b.Property<string>("Note").IsRequired().HasMaxLength(500).HasColumnType("TEXT");
            b.Property<Guid>("ProductId").HasColumnType("TEXT");
            b.Property<string>("Reference").IsRequired().HasMaxLength(100).HasColumnType("TEXT");
            b.Property<int>("Quantity").HasColumnType("INTEGER");
            b.Property<int>("SignedQuantity").HasColumnType("INTEGER");
            b.Property<decimal>("UnitCost").HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("ProductId", "OccurredAt");
            b.ToTable("StockMovements", t =>
            {
                t.HasCheckConstraint("CK_StockMovement_Quantity", "Quantity > 0");
                t.HasCheckConstraint("CK_StockMovement_SignedQuantity", "SignedQuantity = Quantity OR SignedQuantity = -Quantity");
                t.HasCheckConstraint("CK_StockMovement_UnitCost", "CAST(UnitCost AS NUMERIC) >= 0");
            });
        });

        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.CustomerOrderLine", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<Guid>("OrderId").HasColumnType("TEXT");
            b.Property<Guid>("ProductId").HasColumnType("TEXT");
            b.Property<string>("ProductName").IsRequired().HasMaxLength(200).HasColumnType("TEXT");
            b.Property<int>("Quantity").HasColumnType("INTEGER");
            b.Property<decimal>("LineTotal").HasColumnType("TEXT");
            b.Property<decimal>("UnitCost").HasColumnType("TEXT");
            b.Property<decimal>("UnitPrice").HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("OrderId");
            b.ToTable("CustomerOrderLines", t =>
            {
                t.HasCheckConstraint("CK_OrderLine_Price", "CAST(UnitPrice AS NUMERIC) >= 0 AND CAST(UnitCost AS NUMERIC) >= 0 AND CAST(LineTotal AS NUMERIC) >= 0");
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

        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.SalesInvoiceLineRecord", b =>
        {
            b.HasOne("MG.BusinessPlatform.Infrastructure.Persistence.SalesInvoiceRecord", "Invoice")
                .WithMany("Lines")
                .HasForeignKey("InvoiceId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.Navigation("Invoice");
        });

        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.SalesInvoiceRecord", b =>
        {
            b.HasOne("MG.BusinessPlatform.Infrastructure.Persistence.CustomerOrder", null)
                .WithMany()
                .HasForeignKey("OrderId")
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            b.Navigation("Lines");
        });

        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.JournalLineRecord", b =>
        {
            b.HasOne("MG.BusinessPlatform.Infrastructure.Persistence.JournalEntryRecord", "JournalEntry")
                .WithMany("Lines")
                .HasForeignKey("JournalEntryId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.HasOne("MG.BusinessPlatform.Infrastructure.Persistence.LedgerAccountRecord", null)
                .WithMany()
                .HasForeignKey("AccountId")
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            b.Navigation("JournalEntry");
        });

        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.JournalEntryRecord", b =>
        {
            b.Navigation("Lines");
        });

        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.PaymentRecord", b =>
        {
            b.HasOne("MG.BusinessPlatform.Infrastructure.Persistence.JournalEntryRecord", null)
                .WithMany()
                .HasForeignKey("JournalEntryId")
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            b.HasOne("MG.BusinessPlatform.Infrastructure.Persistence.SalesInvoiceRecord", null)
                .WithMany()
                .HasForeignKey("InvoiceId")
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
        });

        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.CustomerOrder", b =>
        {
            b.Navigation("Lines");
        });

        GameSessionsSchemaModel.BuildModel(modelBuilder);
    }
}
