using MG.BusinessPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MG.BusinessPlatform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BusinessDbContext))]
[Migration("20261013010000_AddCustomerSupplierTables")]
public sealed class AddCustomerSupplierTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Customers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Phone = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Email = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                Address = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Customers", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Suppliers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Phone = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Email = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                Address = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                TaxIdentifier = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                PaymentTerms = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Suppliers", x => x.Id));

        migrationBuilder.CreateIndex(name: "IX_Customers_DisplayName", table: "Customers", column: "DisplayName");
        migrationBuilder.CreateIndex(name: "IX_Customers_Phone", table: "Customers", column: "Phone");
        migrationBuilder.CreateIndex(name: "IX_Suppliers_DisplayName", table: "Suppliers", column: "DisplayName");
        migrationBuilder.CreateIndex(name: "IX_Suppliers_Phone", table: "Suppliers", column: "Phone");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        CustomerSupplierSchemaModel.BuildModel(modelBuilder);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Customers");
        migrationBuilder.DropTable(name: "Suppliers");
    }
}
