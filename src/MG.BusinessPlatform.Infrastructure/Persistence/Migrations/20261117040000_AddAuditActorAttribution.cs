using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using MG.BusinessPlatform.Infrastructure.Persistence;

namespace MG.BusinessPlatform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BusinessDbContext))]
[Migration("20261117040000_AddAuditActorAttribution")]
public sealed class AddAuditActorAttribution : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "BusinessId",
            table: "AuditEvents",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "BranchId",
            table: "AuditEvents",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "StaffUserId",
            table: "AuditEvents",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ActorType",
            table: "AuditEvents",
            type: "TEXT",
            maxLength: 32,
            nullable: false,
            defaultValue: "LegacyProcess");

        migrationBuilder.AddColumn<string>(
            name: "CorrelationId",
            table: "AuditEvents",
            type: "TEXT",
            maxLength: 100,
            nullable: false,
            defaultValue: "legacy-migration");

        migrationBuilder.CreateIndex(
            name: "IX_AuditEvents_BusinessId_BranchId_OccurredAtUnixMilliseconds",
            table: "AuditEvents",
            columns: new[] { "BusinessId", "BranchId", "OccurredAtUnixMilliseconds" });

        migrationBuilder.CreateIndex(
            name: "IX_AuditEvents_StaffUserId_OccurredAtUnixMilliseconds",
            table: "AuditEvents",
            columns: new[] { "StaffUserId", "OccurredAtUnixMilliseconds" });
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        BusinessDbContextModelSnapshot.BuildModelCore(modelBuilder);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_AuditEvents_BusinessId_BranchId_OccurredAtUnixMilliseconds",
            table: "AuditEvents");
        migrationBuilder.DropIndex(
            name: "IX_AuditEvents_StaffUserId_OccurredAtUnixMilliseconds",
            table: "AuditEvents");
        migrationBuilder.DropColumn(name: "BusinessId", table: "AuditEvents");
        migrationBuilder.DropColumn(name: "BranchId", table: "AuditEvents");
        migrationBuilder.DropColumn(name: "StaffUserId", table: "AuditEvents");
        migrationBuilder.DropColumn(name: "ActorType", table: "AuditEvents");
        migrationBuilder.DropColumn(name: "CorrelationId", table: "AuditEvents");
    }
}
