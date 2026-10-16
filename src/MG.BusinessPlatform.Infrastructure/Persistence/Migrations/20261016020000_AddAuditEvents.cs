using MG.BusinessPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MG.BusinessPlatform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BusinessDbContext))]
[Migration("20261016020000_AddAuditEvents")]
public sealed class AddAuditEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AuditEvents",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                OccurredAtUnixMilliseconds = table.Column<long>(type: "INTEGER", nullable: false),
                Actor = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                MachineName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Action = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                EntityType = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                EntityId = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                ChangedFields = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_AuditEvents", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_AuditEvents_OccurredAtUtc",
            table: "AuditEvents",
            column: "OccurredAtUnixMilliseconds");

        migrationBuilder.CreateIndex(
            name: "IX_AuditEvents_EntityType_EntityId_OccurredAtUtc",
            table: "AuditEvents",
            columns: new[] { "EntityType", "EntityId", "OccurredAtUnixMilliseconds" });
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        BusinessDbContextModelSnapshot.BuildModelCore(modelBuilder);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "AuditEvents");
}
