using MG.BusinessPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MG.BusinessPlatform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BusinessDbContext))]
[Migration("20261117000000_EnforceSingleOpenGameSessionPerDevice")]
public sealed class EnforceSingleOpenGameSessionPerDevice : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Keep completed/cancelled history unrestricted while making the active-device
        // invariant atomic and authoritative at the database boundary.
        migrationBuilder.CreateIndex(
            name: "IX_GameSessions_OneOpenSessionPerDevice",
            table: "GameSessions",
            column: "DeviceId",
            unique: true,
            filter: "\"Status\" IN ('Active','Paused')");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        GameSessionsSchemaModel.BuildModel(modelBuilder);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_GameSessions_OneOpenSessionPerDevice",
            table: "GameSessions");
    }
}
