using Microsoft.EntityFrameworkCore;

namespace MG.BusinessPlatform.Infrastructure.Persistence.Migrations;

internal static class PurchasingSchemaModel
{
    public static void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.PurchaseInvoiceRecord", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<Guid>("BranchId").HasColumnType("TEXT");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("TEXT");
            b.Property<string>("CurrencyCode").IsRequired().HasMaxLength(3).HasColumnType("TEXT");
            b.Property<string>("InvoiceNumber").IsRequired().HasMaxLength(40).HasColumnType("TEXT");
            b.Property<DateOnly>("IssueDate").HasColumnType("TEXT");
            b.Property<decimal>("PaidAmount").HasColumnType("TEXT");
            b.Property<Guid>("SupplierId").HasColumnType("TEXT");
            b.Property<string>("Status").IsRequired().HasMaxLength(30).HasColumnType("TEXT");
            b.Property<decimal>("TotalAmount").HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("InvoiceNumber").IsUnique();
            b.HasIndex("SupplierId", "IssueDate");
            b.ToTable("PurchaseInvoices", t =>
            {
                t.HasCheckConstraint("CK_PurchaseInvoice_Amounts",
                    "CAST(TotalAmount AS NUMERIC) > 0 AND CAST(PaidAmount AS NUMERIC) >= 0 AND CAST(PaidAmount AS NUMERIC) <= CAST(TotalAmount AS NUMERIC)");
            });
        });

        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.PurchaseInvoiceLineRecord", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<decimal>("DiscountRate").HasColumnType("TEXT");
            b.Property<Guid>("InvoiceId").HasColumnType("TEXT");
            b.Property<decimal>("LineTotal").HasColumnType("TEXT");
            b.Property<string>("ProductName").IsRequired().HasMaxLength(200).HasColumnType("TEXT");
            b.Property<Guid>("ProductId").HasColumnType("TEXT");
            b.Property<int>("Quantity").HasColumnType("INTEGER");
            b.Property<string>("UnitCode").IsRequired().HasMaxLength(20).HasColumnType("TEXT");
            b.Property<decimal>("UnitCost").HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("InvoiceId");
            b.ToTable("PurchaseInvoiceLines", t =>
            {
                t.HasCheckConstraint("CK_PurchaseInvoiceLine_Quantity", "Quantity > 0");
                t.HasCheckConstraint("CK_PurchaseInvoiceLine_Amounts",
                    "CAST(UnitCost AS NUMERIC) >= 0 AND CAST(DiscountRate AS NUMERIC) >= 0 AND CAST(DiscountRate AS NUMERIC) <= 1 AND CAST(LineTotal AS NUMERIC) >= 0");
            });
        });

        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.SupplierPaymentRecord", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<decimal>("Amount").HasColumnType("TEXT");
            b.Property<string>("CurrencyCode").IsRequired().HasMaxLength(3).HasColumnType("TEXT");
            b.Property<string>("Method").IsRequired().HasMaxLength(30).HasColumnType("TEXT");
            b.Property<DateTimeOffset>("OccurredAt").HasColumnType("TEXT");
            b.Property<Guid>("JournalEntryId").HasColumnType("TEXT");
            b.Property<string>("Notes").IsRequired().HasMaxLength(500).HasColumnType("TEXT");
            b.Property<string>("PaymentNumber").IsRequired().HasMaxLength(40).HasColumnType("TEXT");
            b.Property<Guid>("PurchaseInvoiceId").HasColumnType("TEXT");
            b.Property<string>("Reference").IsRequired().HasMaxLength(100).HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("PaymentNumber").IsUnique();
            b.HasIndex("PurchaseInvoiceId");
            b.ToTable("SupplierPayments", t =>
            {
                t.HasCheckConstraint("CK_SupplierPayment_Amount", "CAST(Amount AS NUMERIC) > 0");
            });
        });

        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.PurchaseInvoiceLineRecord", b =>
        {
            b.HasOne("MG.BusinessPlatform.Infrastructure.Persistence.PurchaseInvoiceRecord", "Invoice")
                .WithMany("Lines").HasForeignKey("InvoiceId").OnDelete(DeleteBehavior.Cascade).IsRequired();
            b.Navigation("Invoice");
        });

        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.PurchaseInvoiceRecord", b =>
        {
            b.HasOne("MG.BusinessPlatform.Infrastructure.Persistence.SupplierRecord", null)
                .WithMany().HasForeignKey("SupplierId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.Navigation("Lines");
        });

        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.SupplierPaymentRecord", b =>
        {
            b.HasOne("MG.BusinessPlatform.Infrastructure.Persistence.PurchaseInvoiceRecord", null)
                .WithMany().HasForeignKey("PurchaseInvoiceId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne("MG.BusinessPlatform.Infrastructure.Persistence.JournalEntryRecord", null)
                .WithMany().HasForeignKey("JournalEntryId").OnDelete(DeleteBehavior.Restrict).IsRequired();
        });
    }
}
