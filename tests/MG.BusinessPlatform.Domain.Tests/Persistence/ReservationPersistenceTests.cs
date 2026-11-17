using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MG.BusinessPlatform.Infrastructure.Persistence;

namespace MG.BusinessPlatform.Domain.Tests.Persistence;

public sealed class ReservationPersistenceTests
{
    [Fact]
    public async Task Migration_RejectsOverlappingActiveReservationsOnInsertAndUpdate()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await db.Database.MigrateAsync();

        var resourceId = Guid.NewGuid();
        await using (var resourceCommand = connection.CreateCommand())
        {
            resourceCommand.CommandText = """
                INSERT INTO ReservationResources
                    (Id, BranchId, Name, ResourceType, Capacity, IsAvailable)
                VALUES ($id, $branch, 'Console Room 1', 'GameStation', 4, 1);
                """;
            resourceCommand.Parameters.AddWithValue("$id", resourceId.ToString());
            resourceCommand.Parameters.AddWithValue("$branch", Guid.NewGuid().ToString());
            Assert.Equal(1, await resourceCommand.ExecuteNonQueryAsync());
        }

        const string day = "2026-11-17T";
        async Task InsertReservation(Guid id, string status, string start, string end)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO CustomerReservations
                    (Id, ResourceId, ReservationNumber, IdempotencyKey, GuestName, GuestPhone,
                     GuestCount, StartsAtUtc, EndsAtUtc, Status, Notes, CreatedAtUtc)
                VALUES ($id, $resource, $number, $key, 'Test Guest', '000', 1,
                        $start, $end, $status, '', $created);
                """;
            command.Parameters.AddWithValue("$id", id.ToString());
            command.Parameters.AddWithValue("$resource", resourceId.ToString());
            command.Parameters.AddWithValue("$number", $"RSV-{id:N}");
            command.Parameters.AddWithValue("$key", $"test-key-{id:N}");
            command.Parameters.AddWithValue("$start", start);
            command.Parameters.AddWithValue("$end", end);
            command.Parameters.AddWithValue("$status", status);
            command.Parameters.AddWithValue("$created", day + "08:00:00.0000000+00:00");
            await command.ExecuteNonQueryAsync();
        }

        var firstId = Guid.NewGuid();
        await InsertReservation(firstId, "Confirmed",
            day + "10:00:00.0000000+00:00", day + "11:00:00.0000000+00:00");

        var overlappingInsert = await Assert.ThrowsAsync<SqliteException>(() =>
            InsertReservation(Guid.NewGuid(), "Requested",
                day + "10:30:00.0000000+00:00", day + "11:30:00.0000000+00:00"));
        Assert.Equal(19, overlappingInsert.SqliteErrorCode);
        Assert.Contains("reservation_overlap", overlappingInsert.Message, StringComparison.Ordinal);

        var adjacentId = Guid.NewGuid();
        await InsertReservation(adjacentId, "Requested",
            day + "11:00:00.0000000+00:00", day + "12:00:00.0000000+00:00");

        var cancelledId = Guid.NewGuid();
        await InsertReservation(cancelledId, "Cancelled",
            day + "10:15:00.0000000+00:00", day + "10:45:00.0000000+00:00");

        await using (var activateCancelled = connection.CreateCommand())
        {
            activateCancelled.CommandText = "UPDATE CustomerReservations SET Status = 'Confirmed' WHERE Id = $id;";
            activateCancelled.Parameters.AddWithValue("$id", cancelledId.ToString());
            var overlapOnUpdate = await Assert.ThrowsAsync<SqliteException>(
                () => activateCancelled.ExecuteNonQueryAsync());
            Assert.Contains("reservation_overlap", overlapOnUpdate.Message, StringComparison.Ordinal);
        }

        await using (var completeFirst = connection.CreateCommand())
        {
            completeFirst.CommandText = "UPDATE CustomerReservations SET Status = 'Completed' WHERE Id = $id;";
            completeFirst.Parameters.AddWithValue("$id", firstId.ToString());
            Assert.Equal(1, await completeFirst.ExecuteNonQueryAsync());
        }

        await using (var activateCancelled = connection.CreateCommand())
        {
            activateCancelled.CommandText = "UPDATE CustomerReservations SET Status = 'Confirmed' WHERE Id = $id;";
            activateCancelled.Parameters.AddWithValue("$id", cancelledId.ToString());
            Assert.Equal(1, await activateCancelled.ExecuteNonQueryAsync());
        }

        await using (var moveAdjacent = connection.CreateCommand())
        {
            moveAdjacent.CommandText = """
                UPDATE CustomerReservations
                SET StartsAtUtc = $start, EndsAtUtc = $end
                WHERE Id = $id;
                """;
            moveAdjacent.Parameters.AddWithValue("$start", day + "10:44:00.0000000+00:00");
            moveAdjacent.Parameters.AddWithValue("$end", day + "11:30:00.0000000+00:00");
            moveAdjacent.Parameters.AddWithValue("$id", adjacentId.ToString());
            var overlapOnTimeChange = await Assert.ThrowsAsync<SqliteException>(
                () => moveAdjacent.ExecuteNonQueryAsync());
            Assert.Contains("reservation_overlap", overlapOnTimeChange.Message, StringComparison.Ordinal);
        }

        Assert.Equal(3, await db.CustomerReservations.CountAsync());
    }
}
