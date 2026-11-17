using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using MG.BusinessPlatform.Infrastructure.Persistence;

namespace MG.BusinessPlatform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BusinessDbContext))]
[Migration("20261117030000_AddStaffIdentityAndSessions")]
public sealed class AddStaffIdentityAndSessions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "StaffUsers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                BusinessId = table.Column<Guid>(type: "TEXT", nullable: false),
                UserNameNormalized = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                DisplayName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                PasswordHash = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                Role = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                MustChangePassword = table.Column<bool>(type: "INTEGER", nullable: false),
                FailedLoginCount = table.Column<int>(type: "INTEGER", nullable: false),
                LockedUntilUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                LastLoginAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StaffUsers", x => x.Id);
                table.UniqueConstraint("AK_StaffUsers_BusinessId_Id", x => new { x.BusinessId, x.Id });
                table.ForeignKey(
                    name: "FK_StaffUsers_Businesses_BusinessId",
                    column: x => x.BusinessId,
                    principalTable: "Businesses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.CheckConstraint("CK_StaffUser_FailedLoginCount", "FailedLoginCount >= 0");
                table.CheckConstraint("CK_StaffUser_Role", "Role IN ('Owner','Administrator','Manager','Cashier','Accountant','ReservationOperator','ReadOnly')");
            });

        migrationBuilder.CreateTable(
            name: "StaffSessions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                BusinessId = table.Column<Guid>(type: "TEXT", nullable: false),
                StaffUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                TokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                RevokedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                LastSeenAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                ClientLabel = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StaffSessions", x => x.Id);
                table.ForeignKey(
                    name: "FK_StaffSessions_StaffUsers_BusinessId_StaffUserId",
                    columns: x => new { x.BusinessId, x.StaffUserId },
                    principalTable: "StaffUsers",
                    principalColumns: new[] { "BusinessId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.CheckConstraint("CK_StaffSession_Expiry", "ExpiresAtUtc > CreatedAtUtc");
            });

        migrationBuilder.CreateTable(
            name: "StaffBranchAccess",
            columns: table => new
            {
                BusinessId = table.Column<Guid>(type: "TEXT", nullable: false),
                StaffUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                BranchId = table.Column<Guid>(type: "TEXT", nullable: false),
                GrantedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StaffBranchAccess", x => new { x.BusinessId, x.StaffUserId, x.BranchId });
                table.ForeignKey(
                    name: "FK_StaffBranchAccess_StaffUsers_BusinessId_StaffUserId",
                    columns: x => new { x.BusinessId, x.StaffUserId },
                    principalTable: "StaffUsers",
                    principalColumns: new[] { "BusinessId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_StaffBranchAccess_Branches_BusinessId_BranchId",
                    columns: x => new { x.BusinessId, x.BranchId },
                    principalTable: "Branches",
                    principalColumns: new[] { "BusinessId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_StaffUsers_BusinessId_UserNameNormalized",
            table: "StaffUsers",
            columns: new[] { "BusinessId", "UserNameNormalized" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_StaffSessions_TokenHash",
            table: "StaffSessions",
            column: "TokenHash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_StaffSessions_BusinessId_StaffUserId_ExpiresAtUtc",
            table: "StaffSessions",
            columns: new[] { "BusinessId", "StaffUserId", "ExpiresAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_StaffBranchAccess_BusinessId_BranchId",
            table: "StaffBranchAccess",
            columns: new[] { "BusinessId", "BranchId" });
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        BusinessDbContextModelSnapshot.BuildModelCore(modelBuilder);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "StaffBranchAccess");
        migrationBuilder.DropTable(name: "StaffSessions");
        migrationBuilder.DropTable(name: "StaffUsers");
    }
}
