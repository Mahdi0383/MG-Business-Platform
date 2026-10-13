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
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<Guid>("BranchId").HasColumnType("TEXT");
            b.Property<Guid>("DeviceId").HasColumnType("TEXT");
            b.Property<string>("GameName").IsRequired().HasMaxLength(160).HasColumnType("TEXT");
            b.Property<decimal>("RatePerHour").HasColumnType("TEXT");
            b.Property<string>("CurrencyCode").IsRequired().HasMaxLength(3).HasColumnType("TEXT");
            b.Property<DateTimeOffset>("StartedAtUtc").HasColumnType("TEXT");
            b.Property<DateTimeOffset?>("PausedAtUtc").HasColumnType("TEXT");
            b.Property<DateTimeOffset?>("EndedAtUtc").HasColumnType("TEXT");
            b.Property<long>("AccumulatedPauseTicks").HasColumnType("INTEGER");
            b.Property<DateTimeOffset>("LastTransitionAtUtc").HasColumnType("TEXT");
            b.Property<string>("Status").IsRequired().HasMaxLength(20).HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("BranchId", "StartedAtUtc");
            b.HasIndex("DeviceId", "Status");
            b.ToTable("GameSessions");
        });
        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.GameSessionPlayerRecord", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<DateTimeOffset>("CreatedAtUtc").HasColumnType("TEXT");
            b.Property<Guid>("GameSessionId").HasColumnType("TEXT");
            b.Property<string>("DisplayName").IsRequired().HasMaxLength(120).HasColumnType("TEXT");
            b.Property<decimal>("EntryAmount").HasColumnType("TEXT");
            b.Property<decimal>("TimeCharge").HasColumnType("TEXT");
            b.Property<decimal>("AdditionalAmount").HasColumnType("TEXT");
            b.Property<string>("CurrencyCode").IsRequired().HasMaxLength(3).HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("GameSessionId");
            b.ToTable("GameSessionPlayers");
        });
        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.GameSessionPlayerRecord", b =>
        {
            b.HasOne("MG.BusinessPlatform.Infrastructure.Persistence.GameSessionRecord", "Session")
                .WithMany("Players").HasForeignKey("GameSessionId").OnDelete(DeleteBehavior.Cascade).IsRequired();
            b.Navigation("Session");
        });
        modelBuilder.Entity("MG.BusinessPlatform.Infrastructure.Persistence.GameSessionRecord", b =>
        {
            b.Navigation("Players");
        });
    }
}
