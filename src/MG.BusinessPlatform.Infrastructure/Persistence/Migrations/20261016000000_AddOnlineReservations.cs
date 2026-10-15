using MG.BusinessPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MG.BusinessPlatform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BusinessDbContext))]
[Migration("20261016000000_AddOnlineReservations")]
public sealed class AddOnlineReservations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "OnlineReservationsEnabled",
            table: "StoreSettings",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateTable(
            name: "ReservationResources",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                BranchId = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                ResourceType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                Capacity = table.Column<int>(type: "INTEGER", nullable: false),
                IsAvailable = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReservationResources", x => x.Id);
                table.CheckConstraint("CK_ReservationResource_Type", "ResourceType IN ('Table','Room','GameStation','ServiceSlot')");
                table.CheckConstraint("CK_ReservationResource_Capacity", "Capacity > 0");
            });

        migrationBuilder.CreateTable(
            name: "CustomerReservations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                ResourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                ReservationNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                GuestName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                GuestPhone = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                GuestCount = table.Column<int>(type: "INTEGER", nullable: false),
                StartsAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                EndsAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CustomerReservations", x => x.Id);
                table.CheckConstraint("CK_CustomerReservation_GuestCount", "GuestCount > 0");
                table.CheckConstraint("CK_CustomerReservation_Interval", "EndsAtUtc > StartsAtUtc");
                table.CheckConstraint("CK_CustomerReservation_Status", "Status IN ('Requested','Confirmed','CheckedIn','Completed','Cancelled','NoShow')");
                table.ForeignKey("FK_CustomerReservations_ReservationResources_ResourceId",
                    x => x.ResourceId, "ReservationResources", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_ReservationResources_BranchId_Name", "ReservationResources",
            new[] { "BranchId", "Name" }, unique: true);
        migrationBuilder.CreateIndex("IX_CustomerReservations_ReservationNumber", "CustomerReservations", "ReservationNumber", unique: true);
        migrationBuilder.CreateIndex("IX_CustomerReservations_IdempotencyKey", "CustomerReservations", "IdempotencyKey", unique: true);
        migrationBuilder.CreateIndex("IX_CustomerReservations_ResourceId_StartsAtUtc_EndsAtUtc", "CustomerReservations",
            new[] { "ResourceId", "StartsAtUtc", "EndsAtUtc" });
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        BusinessDbContextModelSnapshot.BuildModelCore(modelBuilder);
        modelBuilder.Entity<StoreSettings>().Property(x => x.OnlineReservationsEnabled).HasColumnType("INTEGER");

        modelBuilder.Entity<ReservationResourceRecord>(entity =>
        {
            entity.Property(x => x.Id).HasColumnType("TEXT");
            entity.Property(x => x.BranchId).HasColumnType("TEXT");
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired().HasColumnType("TEXT");
            entity.Property(x => x.ResourceType).HasMaxLength(30).IsRequired().HasColumnType("TEXT");
            entity.Property(x => x.Capacity).HasColumnType("INTEGER");
            entity.Property(x => x.IsAvailable).HasColumnType("INTEGER");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.BranchId, x.Name }).IsUnique();
            entity.ToTable(t =>
            {
                t.HasCheckConstraint("CK_ReservationResource_Type", "ResourceType IN ('Table','Room','GameStation','ServiceSlot')");
                t.HasCheckConstraint("CK_ReservationResource_Capacity", "Capacity > 0");
            });
        });

        modelBuilder.Entity<CustomerReservationRecord>(entity =>
        {
            entity.Property(x => x.Id).HasColumnType("TEXT");
            entity.Property(x => x.ResourceId).HasColumnType("TEXT");
            entity.Property(x => x.ReservationNumber).HasMaxLength(40).IsRequired().HasColumnType("TEXT");
            entity.Property(x => x.IdempotencyKey).HasMaxLength(100).IsRequired().HasColumnType("TEXT");
            entity.Property(x => x.GuestName).HasMaxLength(200).IsRequired().HasColumnType("TEXT");
            entity.Property(x => x.GuestPhone).HasMaxLength(40).IsRequired().HasColumnType("TEXT");
            entity.Property(x => x.GuestCount).HasColumnType("INTEGER");
            entity.Property(x => x.StartsAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.EndsAtUtc).HasColumnType("TEXT");
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired().HasColumnType("TEXT");
            entity.Property(x => x.Notes).HasMaxLength(1000).IsRequired().HasColumnType("TEXT");
            entity.Property(x => x.CreatedAtUtc).HasColumnType("TEXT");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.ReservationNumber).IsUnique();
            entity.HasIndex(x => x.IdempotencyKey).IsUnique();
            entity.HasIndex(x => new { x.ResourceId, x.StartsAtUtc, x.EndsAtUtc });
            entity.HasOne<ReservationResourceRecord>().WithMany().HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(t =>
            {
                t.HasCheckConstraint("CK_CustomerReservation_GuestCount", "GuestCount > 0");
                t.HasCheckConstraint("CK_CustomerReservation_Interval", "EndsAtUtc > StartsAtUtc");
                t.HasCheckConstraint("CK_CustomerReservation_Status", "Status IN ('Requested','Confirmed','CheckedIn','Completed','Cancelled','NoShow')");
            });
        });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "CustomerReservations");
        migrationBuilder.DropTable(name: "ReservationResources");
        migrationBuilder.DropColumn(name: "OnlineReservationsEnabled", table: "StoreSettings");
    }
}
