using MG.BusinessPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MG.BusinessPlatform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BusinessDbContext))]
[Migration("20261017000000_AddProductAddOns")]
public sealed class AddProductAddOns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ProductAddOns",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                Price = table.Column<decimal>(type: "TEXT", nullable: false),
                CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProductAddOns", x => x.Id);
                table.CheckConstraint("CK_ProductAddOn_Price", "CAST(Price AS NUMERIC) >= 0");
            });

        migrationBuilder.CreateIndex(
            name: "IX_ProductAddOns_Name",
            table: "ProductAddOns",
            column: "Name",
            unique: true);

        migrationBuilder.AddColumn<bool>(
            name: "SupportsAddOns",
            table: "StoreProducts",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "AllowedAddOnIdsJson",
            table: "StoreProducts",
            type: "TEXT",
            maxLength: 4000,
            nullable: false,
            defaultValue: "[]");

        migrationBuilder.AddColumn<string>(
            name: "AddOnsJson",
            table: "CustomerOrderLines",
            type: "TEXT",
            maxLength: 4000,
            nullable: false,
            defaultValue: "[]");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        BusinessDbContextModelSnapshot.BuildModelCore(modelBuilder);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "SupportsAddOns", table: "StoreProducts");
        migrationBuilder.DropColumn(name: "AllowedAddOnIdsJson", table: "StoreProducts");
        migrationBuilder.DropColumn(name: "AddOnsJson", table: "CustomerOrderLines");
        migrationBuilder.DropTable(name: "ProductAddOns");
    }
}
