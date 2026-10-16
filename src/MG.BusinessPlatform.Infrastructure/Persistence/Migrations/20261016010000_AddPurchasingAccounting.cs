using MG.BusinessPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MG.BusinessPlatform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BusinessDbContext))]
[Migration("20261016010000_AddPurchasingAccounting")]
public sealed class AddPurchasingAccounting : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PurchaseInvoices",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                InvoiceNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                BranchId = table.Column<Guid>(type: "TEXT", nullable: false),
                SupplierId = table.Column<Guid>(type: "TEXT", nullable: false),
                IssueDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                TotalAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                PaidAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PurchaseInvoices", x => x.Id);
                table.CheckConstraint("CK_PurchaseInvoice_Amounts",
                    "CAST(TotalAmount AS NUMERIC) > 0 AND CAST(PaidAmount AS NUMERIC) >= 0 AND CAST(PaidAmount AS NUMERIC) <= CAST(TotalAmount AS NUMERIC)");
                table.ForeignKey("FK_PurchaseInvoices_Suppliers_SupplierId",
                    x => x.SupplierId, "Suppliers", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PurchaseInvoiceLines",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                InvoiceId = table.Column<Guid>(type: "TEXT", nullable: false),
                ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                ProductName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                UnitCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                UnitCost = table.Column<decimal>(type: "TEXT", nullable: false),
                DiscountRate = table.Column<decimal>(type: "TEXT", nullable: false),
                LineTotal = table.Column<decimal>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PurchaseInvoiceLines", x => x.Id);
                table.CheckConstraint("CK_PurchaseInvoiceLine_Quantity", "Quantity > 0");
                table.CheckConstraint("CK_PurchaseInvoiceLine_Amounts",
                    "CAST(UnitCost AS NUMERIC) >= 0 AND CAST(DiscountRate AS NUMERIC) >= 0 AND CAST(DiscountRate AS NUMERIC) <= 1 AND CAST(LineTotal AS NUMERIC) >= 0");
                table.ForeignKey("FK_PurchaseInvoiceLines_PurchaseInvoices_InvoiceId",
                    x => x.InvoiceId, "PurchaseInvoices", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_PurchaseInvoiceLines_StoreProducts_ProductId",
                    x => x.ProductId, "StoreProducts", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "SupplierPayments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                PaymentNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                PurchaseInvoiceId = table.Column<Guid>(type: "TEXT", nullable: false),
                JournalEntryId = table.Column<Guid>(type: "TEXT", nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                Method = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                Reference = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SupplierPayments", x => x.Id);
                table.CheckConstraint("CK_SupplierPayment_Amount", "CAST(Amount AS NUMERIC) > 0");
                table.ForeignKey("FK_SupplierPayments_PurchaseInvoices_PurchaseInvoiceId",
                    x => x.PurchaseInvoiceId, "PurchaseInvoices", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_SupplierPayments_JournalEntries_JournalEntryId",
                    x => x.JournalEntryId, "JournalEntries", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_PurchaseInvoices_InvoiceNumber", "PurchaseInvoices", "InvoiceNumber", unique: true);
        migrationBuilder.CreateIndex("IX_PurchaseInvoices_SupplierId_IssueDate", "PurchaseInvoices",
            new[] { "SupplierId", "IssueDate" });
        migrationBuilder.CreateIndex("IX_PurchaseInvoiceLines_InvoiceId", "PurchaseInvoiceLines", "InvoiceId");
        migrationBuilder.CreateIndex("IX_PurchaseInvoiceLines_ProductId", "PurchaseInvoiceLines", "ProductId");
        migrationBuilder.CreateIndex("IX_SupplierPayments_PaymentNumber", "SupplierPayments", "PaymentNumber", unique: true);
        migrationBuilder.CreateIndex("IX_SupplierPayments_PurchaseInvoiceId", "SupplierPayments", "PurchaseInvoiceId");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        BusinessDbContextModelSnapshot.BuildModelCore(modelBuilder);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "SupplierPayments");
        migrationBuilder.DropTable(name: "PurchaseInvoiceLines");
        migrationBuilder.DropTable(name: "PurchaseInvoices");
    }
}
