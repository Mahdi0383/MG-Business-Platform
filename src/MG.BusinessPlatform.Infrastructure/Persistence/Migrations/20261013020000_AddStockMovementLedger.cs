using MG.BusinessPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MG.BusinessPlatform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BusinessDbContext))]
[Migration("20261013020000_AddStockMovementLedger")]
public sealed class AddStockMovementLedger : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "UnitCost",
            table: "StoreProducts",
            type: "TEXT",
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<decimal>(
            name: "UnitCost",
            table: "CustomerOrderLines",
            type: "TEXT",
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.CreateTable(
            name: "StockMovements",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                MovementType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                SignedQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                UnitCost = table.Column<decimal>(type: "TEXT", nullable: false),
                CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                Reference = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StockMovements", x => x.Id);
                table.CheckConstraint("CK_StockMovement_Quantity", "Quantity > 0");
                table.CheckConstraint("CK_StockMovement_SignedQuantity", "SignedQuantity = Quantity OR SignedQuantity = -Quantity");
                table.CheckConstraint("CK_StockMovement_UnitCost", "CAST(UnitCost AS NUMERIC) >= 0");
            });

        migrationBuilder.CreateIndex(
            name: "IX_StockMovements_ProductId_OccurredAt",
            table: "StockMovements",
            columns: new[] { "ProductId", "OccurredAt" });
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        StockLedgerSchemaModel.BuildModel(modelBuilder);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "StockMovements");
        migrationBuilder.DropColumn(name: "UnitCost", table: "StoreProducts");
        migrationBuilder.DropColumn(name: "UnitCost", table: "CustomerOrderLines");
    }
}
