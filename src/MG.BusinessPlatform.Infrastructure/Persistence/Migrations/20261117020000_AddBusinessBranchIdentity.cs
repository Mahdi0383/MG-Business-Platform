using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using MG.BusinessPlatform.Infrastructure.Persistence;

namespace MG.BusinessPlatform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BusinessDbContext))]
[Migration("20261117020000_AddBusinessBranchIdentity")]
public sealed class AddBusinessBranchIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Businesses",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                LegalName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                DefaultCurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                TimeZoneId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Businesses", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Branches",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                BusinessId = table.Column<Guid>(type: "TEXT", nullable: false),
                Code = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                Address = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                Phone = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Branches", x => x.Id);
                table.UniqueConstraint("AK_Branches_BusinessId_Id", x => new { x.BusinessId, x.Id });
                table.ForeignKey(
                    name: "FK_Branches_Businesses_BusinessId",
                    column: x => x.BusinessId,
                    principalTable: "Businesses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddColumn<Guid>(
            name: "BusinessId",
            table: "StoreSettings",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "DefaultBranchId",
            table: "StoreSettings",
            type: "TEXT",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Businesses_DisplayName",
            table: "Businesses",
            column: "DisplayName");

        migrationBuilder.CreateIndex(
            name: "IX_Branches_BusinessId_Code",
            table: "Branches",
            columns: new[] { "BusinessId", "Code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_StoreSettings_BusinessId",
            table: "StoreSettings",
            column: "BusinessId");

        migrationBuilder.CreateIndex(
            name: "IX_StoreSettings_BusinessId_DefaultBranchId",
            table: "StoreSettings",
            columns: new[] { "BusinessId", "DefaultBranchId" });

        migrationBuilder.AddForeignKey(
            name: "FK_StoreSettings_Businesses_BusinessId",
            table: "StoreSettings",
            column: "BusinessId",
            principalTable: "Businesses",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_StoreSettings_Branches_BusinessId_DefaultBranchId",
            table: "StoreSettings",
            columns: new[] { "BusinessId", "DefaultBranchId" },
            principalTable: "Branches",
            principalColumns: new[] { "BusinessId", "Id" },
            onDelete: ReferentialAction.Restrict);
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        BusinessDbContextModelSnapshot.BuildModelCore(modelBuilder);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_StoreSettings_BusinessId_DefaultBranchId", table: "StoreSettings");
        migrationBuilder.DropColumn(name: "BusinessId", table: "StoreSettings");
        migrationBuilder.DropColumn(name: "DefaultBranchId", table: "StoreSettings");
        migrationBuilder.DropTable(name: "Branches");
        migrationBuilder.DropTable(name: "Businesses");
    }
}
