using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using MG.BusinessPlatform.Infrastructure.Persistence;

namespace MG.BusinessPlatform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BusinessDbContext))]
[Migration("20261117060000_EnforceStockMovementBranchOwnership")]
public sealed class EnforceStockMovementBranchOwnership : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddCheckConstraint(
            name: "CK_StockMovement_BusinessBranchPair",
            table: "StockMovements",
            sql: "(BusinessId IS NULL AND BranchId IS NULL) OR (BusinessId IS NOT NULL AND BranchId IS NOT NULL)");

        migrationBuilder.AddForeignKey(
            name: "FK_StockMovements_Branches_BusinessId_BranchId",
            table: "StockMovements",
            columns: new[] { "BusinessId", "BranchId" },
            principalTable: "Branches",
            principalColumns: new[] { "BusinessId", "Id" },
            onDelete: ReferentialAction.Restrict);
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        BusinessDbContextModelSnapshot.BuildModelCore(modelBuilder);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_StockMovements_Branches_BusinessId_BranchId",
            table: "StockMovements");
        migrationBuilder.DropCheckConstraint(
            name: "CK_StockMovement_BusinessBranchPair",
            table: "StockMovements");
    }
}
