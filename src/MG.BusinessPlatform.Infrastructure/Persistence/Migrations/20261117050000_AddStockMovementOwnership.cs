using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using MG.BusinessPlatform.Infrastructure.Persistence;

namespace MG.BusinessPlatform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BusinessDbContext))]
[Migration("20261117050000_AddStockMovementOwnership")]
public sealed class AddStockMovementOwnership : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "BusinessId",
            table: "StockMovements",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "BranchId",
            table: "StockMovements",
            type: "TEXT",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_StockMovements_BusinessId",
            table: "StockMovements",
            column: "BusinessId");

        migrationBuilder.CreateIndex(
            name: "IX_StockMovements_BusinessId_BranchId_ProductId_OccurredAt",
            table: "StockMovements",
            columns: new[] { "BusinessId", "BranchId", "ProductId", "OccurredAt" });
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        BusinessDbContextModelSnapshot.BuildModelCore(modelBuilder);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_StockMovements_BusinessId",
            table: "StockMovements");
        migrationBuilder.DropIndex(
            name: "IX_StockMovements_BusinessId_BranchId_ProductId_OccurredAt",
            table: "StockMovements");
        migrationBuilder.DropColumn(name: "BusinessId", table: "StockMovements");
        migrationBuilder.DropColumn(name: "BranchId", table: "StockMovements");
    }
}
