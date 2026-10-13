using Microsoft.EntityFrameworkCore;

namespace MG.BusinessPlatform.Infrastructure.Persistence.Migrations;

internal static class GameSessionsSchemaModel
{
    internal static void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.0")
            .HasAnnotation("Relational:MaxIdentifierLength", 64);

        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.GameSessionRecord", b =>
        {
            b.Property<Guid>("Id").HasColumnType("TEXT");
            b.Property<long>("AccumulatedPauseTicks").HasColumnType("INTEGER");
            b.Property<Guid>("BranchId").HasColumnType("TEXT");
            b.Property<DateTimeOffset>("CreatedAtUtc").HasColumnType("TEXT");
            b.Property<string>("CurrencyCode").IsRequired().HasMaxLength(3).HasColumnType("TEXT");
            b.Property<Guid>("DeviceId").HasColumnType("TEXT");
            b.Property<DateTimeOffset?>("EndedAtUtc").HasColumnType("TEXT");
            b.Property<string>("GameName").IsRequired().HasMaxLength(160).HasColumnType("TEXT");
            b.Property<DateTimeOffset>("LastTransitionAtUtc").HasColumnType("TEXT");
            b.Property<DateTimeOffset?>("PausedAtUtc").HasColumnType("TEXT");
            b.Property<decimal>("RatePerHour").HasColumnType("TEXT");
            b.Property<DateTimeOffset>("StartedAtUtc").HasColumnType("TEXT");
            b.Property<string>("Status").IsRequired().HasMaxLength(20).HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("BranchId", "DeviceId", "Status");
            b.HasIndex("Status", "StartedAtUtc");
            b.ToTable("GameSessions", t =>
            {
                t.HasCheckConstraint("CK_GameSession_PauseTicks", "AccumulatedPauseTicks >= 0");
                t.HasCheckConstraint("CK_GameSession_Rate", "CAST(RatePerHour AS NUMERIC) >= 0");
                t.HasCheckConstraint("CK_GameSession_Status", "Status IN ('Active','Paused','Completed','Cancelled')");
            });
        });

        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.GameSessionPlayerRecord", b =>
        {
            b.Property<Guid>("Id").HasColumnType("TEXT");
            b.Property<decimal>("AdditionalAmount").HasColumnType("TEXT");
            b.Property<string>("CurrencyCode").IsRequired().HasMaxLength(3).HasColumnType("TEXT");
            b.Property<DateTimeOffset>("CreatedAtUtc").HasColumnType("TEXT");
            b.Property<string>("DisplayName").IsRequired().HasMaxLength(120).HasColumnType("TEXT");
            b.Property<decimal>("EntryAmount").HasColumnType("TEXT");
            b.Property<Guid>("SessionId").HasColumnType("TEXT");
            b.Property<decimal>("TimeCharge").HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("SessionId", "DisplayName");
            b.ToTable("GameSessionPlayers", t => t.HasCheckConstraint("CK_GameSessionPlayer_Amounts",
                "CAST(EntryAmount AS NUMERIC) >= 0 AND CAST(TimeCharge AS NUMERIC) >= 0 AND CAST(AdditionalAmount AS NUMERIC) >= 0"));
        });

        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.GameSessionPlayerRecord", b =>
        {
            b.HasOne("MG.BusinessPlatform.Infrastructure.Persistence.GameSessionRecord", "Session")
                .WithMany("Players").HasForeignKey("SessionId").OnDelete(DeleteBehavior.Cascade).IsRequired();
            b.Navigation("Session");
        });

        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.GameSessionRecord", b =>
        {
            b.Navigation("Players").UsePropertyAccessMode(PropertyAccessMode.Property);
        });
    }
}
