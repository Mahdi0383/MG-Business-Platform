using MG.BusinessPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MG.BusinessPlatform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BusinessDbContext))]
[Migration("20261013040000_AddGameSessions")]
public sealed class AddGameSessions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "GameSessions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                BranchId = table.Column<Guid>(type: "TEXT", nullable: false),
                DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                GameName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                RatePerHour = table.Column<decimal>(type: "TEXT", nullable: false),
                CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                PausedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                EndedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                AccumulatedPauseTicks = table.Column<long>(type: "INTEGER", nullable: false),
                LastTransitionAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GameSessions", x => x.Id);
                table.CheckConstraint("CK_GameSession_Rate", "CAST(RatePerHour AS NUMERIC) >= 0");
                table.CheckConstraint("CK_GameSession_PauseTicks", "AccumulatedPauseTicks >= 0");
                table.CheckConstraint("CK_GameSession_Status", "Status IN ('Active','Paused','Completed','Cancelled')");
            });

        migrationBuilder.CreateTable(
            name: "GameSessionPlayers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                GameSessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                DisplayName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                EntryAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                TimeCharge = table.Column<decimal>(type: "TEXT", nullable: false),
                AdditionalAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GameSessionPlayers", x => x.Id);
                table.CheckConstraint("CK_GameSessionPlayer_Amounts",
                    "CAST(EntryAmount AS NUMERIC) >= 0 AND CAST(TimeCharge AS NUMERIC) >= 0 AND CAST(AdditionalAmount AS NUMERIC) >= 0");
                table.ForeignKey("FK_GameSessionPlayers_GameSessions_GameSessionId", x => x.GameSessionId,
                    "GameSessions", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_GameSessions_Status_StartedAtUtc", "GameSessions", new[] { "Status", "StartedAtUtc" });
        migrationBuilder.CreateIndex("IX_GameSessions_BranchId_DeviceId_Status", "GameSessions", new[] { "BranchId", "DeviceId", "Status" });
        
        migrationBuilder.CreateIndex("IX_GameSessionPlayers_GameSessionId_DisplayName", "GameSessionPlayers", new[] { "GameSessionId", "DisplayName" });
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        GameSessionsSchemaModel.BuildModel(modelBuilder);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "GameSessionPlayers");
        migrationBuilder.DropTable(name: "GameSessions");
    }
}
