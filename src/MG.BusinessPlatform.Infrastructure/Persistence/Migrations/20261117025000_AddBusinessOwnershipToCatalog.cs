using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using MG.BusinessPlatform.Infrastructure.Persistence;

namespace MG.BusinessPlatform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BusinessDbContext))]
[Migration("20261117025000_AddBusinessOwnershipToCatalog")]
public sealed class AddBusinessOwnershipToCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "BusinessId",
            table: "StoreProducts",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "BusinessId",
            table: "LedgerAccounts",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "BusinessId",
            table: "Customers",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "BusinessId",
            table: "Suppliers",
            type: "TEXT",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_StoreProducts_BusinessId",
            table: "StoreProducts",
            column: "BusinessId");

        migrationBuilder.CreateIndex(
            name: "IX_StoreProducts_BusinessId_Sku",
            table: "StoreProducts",
            columns: new[] { "BusinessId", "Sku" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_LedgerAccounts_BusinessId",
            table: "LedgerAccounts",
            column: "BusinessId");

        migrationBuilder.CreateIndex(
            name: "IX_LedgerAccounts_BusinessId_Code",
            table: "LedgerAccounts",
            columns: new[] { "BusinessId", "Code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Customers_BusinessId",
            table: "Customers",
            column: "BusinessId");

        migrationBuilder.CreateIndex(
            name: "IX_Suppliers_BusinessId",
            table: "Suppliers",
            column: "BusinessId");

        migrationBuilder.AddForeignKey(
            name: "FK_StoreProducts_Businesses_BusinessId",
            table: "StoreProducts",
            column: "BusinessId",
            principalTable: "Businesses",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_LedgerAccounts_Businesses_BusinessId",
            table: "LedgerAccounts",
            column: "BusinessId",
            principalTable: "Businesses",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_Customers_Businesses_BusinessId",
            table: "Customers",
            column: "BusinessId",
            principalTable: "Businesses",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_Suppliers_Businesses_BusinessId",
            table: "Suppliers",
            column: "BusinessId",
            principalTable: "Businesses",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        BusinessDbContextModelSnapshot.BuildModelCore(modelBuilder);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "FK_StoreProducts_Businesses_BusinessId", table: "StoreProducts");
        migrationBuilder.DropForeignKey(name: "FK_LedgerAccounts_Businesses_BusinessId", table: "LedgerAccounts");
        migrationBuilder.DropForeignKey(name: "FK_Customers_Businesses_BusinessId", table: "Customers");
        migrationBuilder.DropForeignKey(name: "FK_Suppliers_Businesses_BusinessId", table: "Suppliers");

        migrationBuilder.DropIndex(name: "IX_StoreProducts_BusinessId_Sku", table: "StoreProducts");
        migrationBuilder.DropIndex(name: "IX_StoreProducts_BusinessId", table: "StoreProducts");
        migrationBuilder.DropIndex(name: "IX_LedgerAccounts_BusinessId_Code", table: "LedgerAccounts");
        migrationBuilder.DropIndex(name: "IX_LedgerAccounts_BusinessId", table: "LedgerAccounts");
        migrationBuilder.DropIndex(name: "IX_Customers_BusinessId", table: "Customers");
        migrationBuilder.DropIndex(name: "IX_Suppliers_BusinessId", table: "Suppliers");

        migrationBuilder.DropColumn(name: "BusinessId", table: "StoreProducts");
        migrationBuilder.DropColumn(name: "BusinessId", table: "LedgerAccounts");
        migrationBuilder.DropColumn(name: "BusinessId", table: "Customers");
        migrationBuilder.DropColumn(name: "BusinessId", table: "Suppliers");
    }
}
