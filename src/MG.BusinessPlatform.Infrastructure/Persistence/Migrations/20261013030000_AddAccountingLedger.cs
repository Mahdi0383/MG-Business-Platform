using MG.BusinessPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MG.BusinessPlatform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BusinessDbContext))]
[Migration("20261013030000_AddAccountingLedger")]
public sealed class AddAccountingLedger : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "LedgerAccounts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Code = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                AccountType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                IsPostingAllowed = table.Column<bool>(type: "INTEGER", nullable: false),
                IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_LedgerAccounts", x => x.Id));

        migrationBuilder.CreateTable(
            name: "SalesInvoices",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                InvoiceNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                OrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                IssueDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                CustomerName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                CustomerPhone = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                TotalAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                CostTotal = table.Column<decimal>(type: "TEXT", nullable: false),
                PaidAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SalesInvoices", x => x.Id);
                table.CheckConstraint("CK_SalesInvoice_Amounts", "CAST(TotalAmount AS NUMERIC) >= 0 AND CAST(CostTotal AS NUMERIC) >= 0 AND CAST(PaidAmount AS NUMERIC) >= 0 AND CAST(PaidAmount AS NUMERIC) <= CAST(TotalAmount AS NUMERIC)");
                table.ForeignKey("FK_SalesInvoices_CustomerOrders_OrderId", x => x.OrderId, "CustomerOrders", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "JournalEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                EntryNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                EntryDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                SourceType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                SourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_JournalEntries", x => x.Id));

        migrationBuilder.CreateTable(
            name: "SalesInvoiceLines",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                InvoiceId = table.Column<Guid>(type: "TEXT", nullable: false),
                ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                ProductName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                UnitPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                UnitCost = table.Column<decimal>(type: "TEXT", nullable: false),
                LineTotal = table.Column<decimal>(type: "TEXT", nullable: false),
                CostTotal = table.Column<decimal>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SalesInvoiceLines", x => x.Id);
                table.CheckConstraint("CK_SalesInvoiceLine_Quantity", "Quantity > 0");
                table.CheckConstraint("CK_SalesInvoiceLine_Amounts", "CAST(UnitPrice AS NUMERIC) >= 0 AND CAST(UnitCost AS NUMERIC) >= 0 AND CAST(LineTotal AS NUMERIC) >= 0 AND CAST(CostTotal AS NUMERIC) >= 0");
                table.ForeignKey("FK_SalesInvoiceLines_SalesInvoices_InvoiceId", x => x.InvoiceId, "SalesInvoices", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "JournalLines",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                JournalEntryId = table.Column<Guid>(type: "TEXT", nullable: false),
                AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                AccountCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                AccountName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Side = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                Amount = table.Column<decimal>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_JournalLines", x => x.Id);
                table.CheckConstraint("CK_JournalLine_Side", "Side IN ('Debit','Credit')");
                table.CheckConstraint("CK_JournalLine_Amount", "CAST(Amount AS NUMERIC) > 0");
                table.ForeignKey("FK_JournalLines_JournalEntries_JournalEntryId", x => x.JournalEntryId, "JournalEntries", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_JournalLines_LedgerAccounts_AccountId", x => x.AccountId, "LedgerAccounts", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Payments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                PaymentNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                InvoiceId = table.Column<Guid>(type: "TEXT", nullable: false),
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
                table.PrimaryKey("PK_Payments", x => x.Id);
                table.CheckConstraint("CK_Payment_Amount", "CAST(Amount AS NUMERIC) > 0");
                table.ForeignKey("FK_Payments_SalesInvoices_InvoiceId", x => x.InvoiceId, "SalesInvoices", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_Payments_JournalEntries_JournalEntryId", x => x.JournalEntryId, "JournalEntries", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_LedgerAccounts_Code", "LedgerAccounts", "Code", unique: true);
        migrationBuilder.CreateIndex("IX_SalesInvoices_InvoiceNumber", "SalesInvoices", "InvoiceNumber", unique: true);
        migrationBuilder.CreateIndex("IX_SalesInvoices_OrderId", "SalesInvoices", "OrderId", unique: true);
        migrationBuilder.CreateIndex("IX_JournalEntries_EntryNumber", "JournalEntries", "EntryNumber", unique: true);
        migrationBuilder.CreateIndex("IX_JournalEntries_SourceType_SourceId", "JournalEntries", new[] { "SourceType", "SourceId" }, unique: true);
        migrationBuilder.CreateIndex("IX_SalesInvoiceLines_InvoiceId", "SalesInvoiceLines", "InvoiceId");
        migrationBuilder.CreateIndex("IX_JournalLines_JournalEntryId", "JournalLines", "JournalEntryId");
        migrationBuilder.CreateIndex("IX_JournalLines_AccountId", "JournalLines", "AccountId");
        migrationBuilder.CreateIndex("IX_Payments_PaymentNumber", "Payments", "PaymentNumber", unique: true);
        migrationBuilder.CreateIndex("IX_Payments_InvoiceId", "Payments", "InvoiceId");
        migrationBuilder.CreateIndex("IX_Payments_JournalEntryId", "Payments", "JournalEntryId");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        AccountingSchemaModel.BuildModel(modelBuilder);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Payments");
        migrationBuilder.DropTable(name: "JournalLines");
        migrationBuilder.DropTable(name: "SalesInvoiceLines");
        migrationBuilder.DropTable(name: "JournalEntries");
        migrationBuilder.DropTable(name: "SalesInvoices");
        migrationBuilder.DropTable(name: "LedgerAccounts");
    }
}
