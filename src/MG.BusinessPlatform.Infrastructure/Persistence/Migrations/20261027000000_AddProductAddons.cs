using MG.BusinessPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MG.BusinessPlatform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BusinessDbContext))]
[Migration("20261027000000_AddProductAddons")]
public sealed class AddProductAddons : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ProductAddons",
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
                table.PrimaryKey("PK_ProductAddons", x => x.Id);
                table.CheckConstraint("CK_ProductAddon_Price", "CAST(Price AS NUMERIC) >= 0");
            });

        migrationBuilder.CreateTable(
            name: "StoreProductAddons",
            columns: table => new
            {
                ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                AddonId = table.Column<Guid>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StoreProductAddons", x => new { x.ProductId, x.AddonId });
                table.ForeignKey(
                    name: "FK_StoreProductAddons_ProductAddons_AddonId",
                    column: x => x.AddonId,
                    principalTable: "ProductAddons",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_StoreProductAddons_StoreProducts_ProductId",
                    column: x => x.ProductId,
                    principalTable: "StoreProducts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.AddColumn<string>(
            name: "AddonsJson",
            table: "CustomerOrderLines",
            type: "TEXT",
            maxLength: 4000,
            nullable: false,
            defaultValue: "[]");

        migrationBuilder.AddColumn<string>(
            name: "AddonsJson",
            table: "SalesInvoiceLines",
            type: "TEXT",
            maxLength: 4000,
            nullable: false,
            defaultValue: "[]");

        migrationBuilder.CreateIndex(
            name: "IX_ProductAddons_Name",
            table: "ProductAddons",
            column: "Name");

        migrationBuilder.CreateIndex(
            name: "IX_StoreProductAddons_AddonId",
            table: "StoreProductAddons",
            column: "AddonId");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        BusinessDbContextModelSnapshot.BuildModelCore(modelBuilder);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "AddonsJson", table: "CustomerOrderLines");
        migrationBuilder.DropColumn(name: "AddonsJson", table: "SalesInvoiceLines");
        migrationBuilder.DropTable(name: "StoreProductAddons");
        migrationBuilder.DropTable(name: "ProductAddons");
    }
}
