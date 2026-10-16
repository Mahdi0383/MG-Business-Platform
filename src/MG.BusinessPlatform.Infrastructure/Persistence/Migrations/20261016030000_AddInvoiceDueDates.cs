using MG.BusinessPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MG.BusinessPlatform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BusinessDbContext))]
[Migration("20261016030000_AddInvoiceDueDates")]
public sealed class AddInvoiceDueDates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateOnly>(
            name: "DueDate",
            table: "SalesInvoices",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "DueDate",
            table: "PurchaseInvoices",
            type: "TEXT",
            nullable: true);
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        BusinessDbContextModelSnapshot.BuildModelCore(modelBuilder);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DueDate", table: "SalesInvoices");
        migrationBuilder.DropColumn(name: "DueDate", table: "PurchaseInvoices");
    }
}
