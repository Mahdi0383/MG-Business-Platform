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
                Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                AverageUnitCost = table.Column<decimal>(type: "TEXT", nullable: false),
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
                    "CK_BranchInventoryBalance_Quantity",
                    "Quantity >= 0");
                table.CheckConstraint(
                    "CK_BranchInventoryBalance_AverageUnitCost",
                    "CAST(AverageUnitCost AS NUMERIC) >= 0");
            });

        migrationBuilder.CreateIndex(
            name: "IX_BranchInventoryBalances_ProductId",
            table: "BranchInventoryBalances",
            column: "ProductId");

        migrationBuilder.CreateIndex(
            name: "IX_BranchInventoryBalances_BusinessId_ProductId",
            table: "BranchInventoryBalances",
            columns: new[] { "BusinessId", "ProductId" });
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        BusinessDbContextModelSnapshot.BuildModelCore(modelBuilder);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "BranchInventoryBalances");
}
