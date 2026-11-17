using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using MG.BusinessPlatform.Infrastructure.Persistence;

namespace MG.BusinessPlatform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BusinessDbContext))]
[Migration("20261117070000_AddBranchInventoryBalances")]
public sealed class AddBranchInventoryBalances : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BranchInventoryBalances",
            columns: table => new
            {
                BusinessId = table.Column<Guid>(type: "TEXT", nullable: false),
                BranchId = table.Column<Guid>(type: "TEXT", nullable: false),
                ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                OnHandQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_BranchInventoryBalances",
                    x => new { x.BusinessId, x.BranchId, x.ProductId });
                table.ForeignKey(
                    name: "FK_BranchInventoryBalances_Branches_BusinessId_BranchId",
                    columns: x => new { x.BusinessId, x.BranchId },
                    principalTable: "Branches",
                    principalColumns: new[] { "BusinessId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_BranchInventoryBalances_StoreProducts_ProductId",
                    column: x => x.ProductId,
                    principalTable: "StoreProducts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.CheckConstraint(
                    "CK_BranchInventoryBalance_OnHand",
                    "OnHandQuantity >= 0");
            });

        migrationBuilder.CreateIndex(
            name: "IX_BranchInventoryBalances_ProductId",
            table: "BranchInventoryBalances",
            column: "ProductId");

        migrationBuilder.CreateIndex(
            name: "IX_BranchInventoryBalances_BusinessId_ProductId_BranchId",
            table: "BranchInventoryBalances",
            columns: new[] { "BusinessId", "ProductId", "BranchId" });

        migrationBuilder.Sql("""
            CREATE TRIGGER TR_BranchInventoryBalances_Insert_Ownership
            BEFORE INSERT ON BranchInventoryBalances
            FOR EACH ROW
            WHEN NOT EXISTS (
                SELECT 1 FROM StoreProducts
                WHERE Id = NEW.ProductId AND BusinessId = NEW.BusinessId
            )
            BEGIN
                SELECT RAISE(ABORT, 'Inventory balance product belongs to another business.');
            END;
            """);

        migrationBuilder.Sql("""
            CREATE TRIGGER TR_StoreProducts_BusinessOwnership_Immutable_When_InventoryExists
            BEFORE UPDATE OF BusinessId ON StoreProducts
            FOR EACH ROW
            WHEN EXISTS (
                SELECT 1 FROM BranchInventoryBalances
                WHERE ProductId = OLD.Id AND (NEW.BusinessId IS NULL OR BusinessId != NEW.BusinessId)
            )
            BEGIN
                SELECT RAISE(ABORT, 'Product business ownership cannot change while branch inventory exists.');
            END;
            """);

        migrationBuilder.Sql("""
            CREATE TRIGGER TR_BranchInventoryBalances_Update_Ownership
            BEFORE UPDATE OF BusinessId, BranchId, ProductId ON BranchInventoryBalances
            FOR EACH ROW
            WHEN NOT EXISTS (
                SELECT 1 FROM StoreProducts
                WHERE Id = NEW.ProductId AND BusinessId = NEW.BusinessId
            )
            BEGIN
                SELECT RAISE(ABORT, 'Inventory balance product belongs to another business.');
            END;
            """);
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        BusinessDbContextModelSnapshot.BuildModelCore(modelBuilder);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_BranchInventoryBalances_Update_Ownership;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_StoreProducts_BusinessOwnership_Immutable_When_InventoryExists;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_BranchInventoryBalances_Insert_Ownership;");
        migrationBuilder.DropTable(name: "BranchInventoryBalances");
    }
}
