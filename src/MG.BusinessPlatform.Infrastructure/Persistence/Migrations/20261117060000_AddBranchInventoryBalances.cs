using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using MG.BusinessPlatform.Infrastructure.Persistence;

namespace MG.BusinessPlatform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BusinessDbContext))]
[Migration("20261117060000_AddBranchInventoryBalances")]
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
                Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_BranchInventoryBalances",
                    x => new { x.BusinessId, x.BranchId, x.ProductId });
                table.ForeignKey(
                    name: "FK_BranchInventoryBalances_Businesses_BusinessId",
                    column: x => x.BusinessId,
                    principalTable: "Businesses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_BranchInventoryBalances_Branches_BusinessId_BranchId",
                    columns: x => new { x.BusinessId, x.BranchId },
                    principalTable: "Branches",
                    principalColumns: new[] { "BusinessId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.CheckConstraint(
                    "CK_BranchInventoryBalance_Quantity",
                    "Quantity >= 0");
            });

        migrationBuilder.CreateIndex(
            name: "IX_BranchInventoryBalances_BusinessId_ProductId",
            table: "BranchInventoryBalances",
            columns: new[] { "BusinessId", "ProductId" });

        migrationBuilder.Sql(
            """
            CREATE TRIGGER TR_StockMovements_ValidateBranchOwnership
            BEFORE INSERT ON StockMovements
            WHEN NEW.BusinessId IS NOT NULL AND NEW.BranchId IS NOT NULL
            BEGIN
                SELECT CASE WHEN NOT EXISTS (
                    SELECT 1
                    FROM Branches
                    WHERE Id = NEW.BranchId
                      AND BusinessId = NEW.BusinessId
                      AND IsActive = 1
                ) THEN RAISE(ABORT, 'Stock movement branch does not belong to the business or is inactive') END;

                SELECT CASE WHEN NOT EXISTS (
                    SELECT 1
                    FROM StoreProducts
                    WHERE Id = NEW.ProductId
                      AND BusinessId = NEW.BusinessId
                ) THEN RAISE(ABORT, 'Stock movement product does not belong to the business') END;
            END;
            """);

        migrationBuilder.Sql(
            """
            CREATE TRIGGER TR_StockMovements_ApplyBranchInventoryBalance
            AFTER INSERT ON StockMovements
            WHEN NEW.BusinessId IS NOT NULL AND NEW.BranchId IS NOT NULL
            BEGIN
                INSERT INTO BranchInventoryBalances
                    (BusinessId, BranchId, ProductId, Quantity, UpdatedAt)
                VALUES
                    (
                        NEW.BusinessId,
                        NEW.BranchId,
                        NEW.ProductId,
                        CASE
                            WHEN NEW.BranchId = (SELECT DefaultBranchId FROM StoreSettings WHERE Id = 1)
                            THEN (SELECT StockQuantity FROM StoreProducts WHERE Id = NEW.ProductId)
                            ELSE NEW.SignedQuantity
                        END,
                        NEW.OccurredAt
                    )
                ON CONFLICT(BusinessId, BranchId, ProductId)
                DO UPDATE SET
                    Quantity = BranchInventoryBalances.Quantity + NEW.SignedQuantity,
                    UpdatedAt = NEW.OccurredAt;
            END;
            """);

        migrationBuilder.Sql(
            """
            CREATE TRIGGER TR_StockMovements_PreventUpdate
            BEFORE UPDATE ON StockMovements
            BEGIN
                SELECT RAISE(ABORT, 'Stock movements are immutable; record a compensating movement instead');
            END;
            """);

        migrationBuilder.Sql(
            """
            CREATE TRIGGER TR_StockMovements_PreventDelete
            BEFORE DELETE ON StockMovements
            BEGIN
                SELECT RAISE(ABORT, 'Stock movements are immutable; record a compensating movement instead');
            END;
            """);
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        BusinessDbContextModelSnapshot.BuildModelCore(modelBuilder);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_StockMovements_PreventDelete;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_StockMovements_PreventUpdate;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_StockMovements_ApplyBranchInventoryBalance;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_StockMovements_ValidateBranchOwnership;");
        migrationBuilder.DropTable(name: "BranchInventoryBalances");
    }
}
