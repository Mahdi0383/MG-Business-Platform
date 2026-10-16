using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MG.BusinessPlatform.Infrastructure.Persistence;
using MG.BusinessPlatform.Domain.Finance;
using MG.BusinessPlatform.Domain.GameCafe;

namespace MG.BusinessPlatform.Domain.Tests.Persistence;

public sealed class GameSessionPersistenceTests
{
    [Fact]
    public async Task Repository_RestoresDomainTimerAndPlayerAmountsAfterRestart()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        var start = DateTimeOffset.Parse("2026-10-13T08:00:00+00:00");
        var player = new GameSessionPlayer(Guid.NewGuid(), "Sara",
            new Money(100m, "IRR"), new Money(200m, "IRR"), new Money(50m, "IRR"));
        var session = new GameSession(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Tekken 8", start, new Money(600m, "IRR"), [player]);
        session.Pause(start.AddMinutes(25));

        await using (var db = new BusinessDbContext(options))
        {
            await db.Database.MigrateAsync();
            await new GameSessionRepository(db).SaveAsync(session);
        }

        await using (var reopened = new BusinessDbContext(options))
        {
            var restored = await new GameSessionRepository(reopened).FindAsync(session.Id);
            Assert.NotNull(restored);
            Assert.Equal(GameSessionStatus.Paused, restored.Status);
            Assert.Equal(TimeSpan.FromMinutes(25), restored.ElapsedAt(start.AddHours(2)));
            Assert.Equal(new Money(350m, "IRR"), restored.TotalPlayerCharges);
        }
    }

    [Fact]
    public async Task Repository_RejectsSecondActiveSessionOnTheSameDevice()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        var branchId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var startedAt = DateTimeOffset.Parse("2026-10-16T08:00:00+00:00");

        await using var db = new BusinessDbContext(options);
        await db.Database.MigrateAsync();
        var repository = new GameSessionRepository(db);
        var first = new GameSession(Guid.NewGuid(), branchId, deviceId, "Tekken 8", startedAt,
            new Money(600000m, "IRR"),
            [new GameSessionPlayer(Guid.NewGuid(), "Ali", Money.Zero("IRR"), Money.Zero("IRR"), Money.Zero("IRR"))]);
        await repository.SaveAsync(first);

        var second = new GameSession(Guid.NewGuid(), branchId, deviceId, "FC 26", startedAt.AddMinutes(1),
            new Money(500000m, "IRR"),
            [new GameSessionPlayer(Guid.NewGuid(), "Sara", Money.Zero("IRR"), Money.Zero("IRR"), Money.Zero("IRR"))]);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveAsync(second));
        Assert.Contains("already has an active or paused game session", error.Message, StringComparison.Ordinal);
        Assert.Single(await db.GameSessions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Migration_PersistsPausedSessionAndPlayerAcrossContextRestart()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        var sessionId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var startedAt = DateTimeOffset.Parse("2026-10-13T08:00:00+00:00");
        var pausedAt = startedAt.AddMinutes(25);

        await using (var db = new BusinessDbContext(options))
        {
            await db.Database.MigrateAsync();
            db.GameSessions.Add(new GameSessionRecord
            {
                Id = sessionId, BranchId = Guid.NewGuid(), DeviceId = Guid.NewGuid(),
                GameName = "Tekken 8", Status = "Paused", CurrencyCode = "IRR",
                RatePerHour = 600000m, StartedAtUtc = startedAt, PausedAtUtc = pausedAt,
                LastTransitionAtUtc = pausedAt, AccumulatedPauseTicks = 0,
                Players =
                [
                    new GameSessionPlayerRecord
                    {
                        Id = playerId, DisplayName = "Ali", EntryAmount = 50000m,
                        TimeCharge = 250000m, AdditionalAmount = 20000m,
                        CurrencyCode = "IRR"
                    }
                ]
            });
            await db.SaveChangesAsync();
        }

        await using (var reopened = new BusinessDbContext(options))
        {
            var session = await reopened.GameSessions.Include(x => x.Players).SingleAsync(x => x.Id == sessionId);
            Assert.Equal("Paused", session.Status);
            Assert.Equal(pausedAt, session.PausedAtUtc);
            Assert.Equal("Tekken 8", session.GameName);
            Assert.Single(session.Players);
            Assert.Equal(playerId, session.Players[0].Id);
            Assert.Equal(320000m, session.Players[0].EntryAmount + session.Players[0].TimeCharge + session.Players[0].AdditionalAmount);
            Assert.Contains("20261013040000_AddGameSessions", await reopened.Database.GetAppliedMigrationsAsync());
        }
    }
}
