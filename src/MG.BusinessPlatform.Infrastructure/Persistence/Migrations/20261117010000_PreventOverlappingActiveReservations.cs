using MG.BusinessPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MG.BusinessPlatform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BusinessDbContext))]
[Migration("20261117010000_PreventOverlappingActiveReservations")]
public sealed class PreventOverlappingActiveReservations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // A pre-insert availability query is not sufficient under concurrent requests.
        // SQLite triggers make the interval exclusion invariant atomic for every writer.
        // julianday() compares instants correctly even if legacy rows contain offsets
        // other than UTC; the public endpoint normalizes new requests to UTC.
        migrationBuilder.Sql("""
            CREATE TRIGGER TR_CustomerReservations_NoOverlap_Insert
            BEFORE INSERT ON CustomerReservations
            FOR EACH ROW
            WHEN NEW.Status IN ('Requested','Confirmed','CheckedIn')
            BEGIN
                SELECT RAISE(ABORT, 'reservation_overlap')
                WHERE EXISTS (
                    SELECT 1
                    FROM CustomerReservations AS existing
                    WHERE existing.ResourceId = NEW.ResourceId
                      AND existing.Status IN ('Requested','Confirmed','CheckedIn')
                      AND julianday(existing.StartsAtUtc) < julianday(NEW.EndsAtUtc)
                      AND julianday(NEW.StartsAtUtc) < julianday(existing.EndsAtUtc)
                );
            END;
            """);

        migrationBuilder.Sql("""
            CREATE TRIGGER TR_CustomerReservations_NoOverlap_Update
            BEFORE UPDATE OF ResourceId, StartsAtUtc, EndsAtUtc, Status
            ON CustomerReservations
            FOR EACH ROW
            WHEN NEW.Status IN ('Requested','Confirmed','CheckedIn')
            BEGIN
                SELECT RAISE(ABORT, 'reservation_overlap')
                WHERE EXISTS (
                    SELECT 1
                    FROM CustomerReservations AS existing
                    WHERE existing.Id <> NEW.Id
                      AND existing.ResourceId = NEW.ResourceId
                      AND existing.Status IN ('Requested','Confirmed','CheckedIn')
                      AND julianday(existing.StartsAtUtc) < julianday(NEW.EndsAtUtc)
                      AND julianday(NEW.StartsAtUtc) < julianday(existing.EndsAtUtc)
                );
            END;
            """);
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        BusinessDbContextModelSnapshot.BuildModelCore(modelBuilder);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_CustomerReservations_NoOverlap_Update;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_CustomerReservations_NoOverlap_Insert;");
    }
}
