using Microsoft.EntityFrameworkCore;
using MG.BusinessPlatform.Domain.Finance;
using MG.BusinessPlatform.Domain.GameCafe;

namespace MG.BusinessPlatform.Infrastructure.Persistence;

/// <summary>
/// Stores and rehydrates the game-session aggregate so a process restart does not reset
/// the timer or lose per-player amounts. Callers should use a fresh DbContext per operation.
/// </summary>
public sealed class GameSessionRepository(BusinessDbContext db)
{
    public async Task SaveAsync(GameSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var record = await db.GameSessions
            .Include(x => x.Players)
            .SingleOrDefaultAsync(x => x.Id == session.Id, cancellationToken);

        if (record is null)
        {
            record = new GameSessionRecord { Id = session.Id };
            db.GameSessions.Add(record);
        }

        record.BranchId = session.BranchId;
        record.DeviceId = session.DeviceId;
        record.GameName = session.GameName;
        record.Status = session.Status.ToString();
        record.CurrencyCode = session.RatePerHour.CurrencyCode;
        record.RatePerHour = session.RatePerHour.Amount;
        record.StartedAtUtc = session.StartedAtUtc;
        record.PausedAtUtc = session.PausedAtUtc;
        record.EndedAtUtc = session.EndedAtUtc;
        record.LastTransitionAtUtc = session.LastTransitionAtUtc;
        record.AccumulatedPauseTicks = session.AccumulatedPause.Ticks;

        var incomingIds = session.Players.Select(x => x.Id).ToHashSet();
        foreach (var existing in record.Players.Where(x => !incomingIds.Contains(x.Id)).ToArray())
            db.GameSessionPlayers.Remove(existing);

        foreach (var player in session.Players)
        {
            var playerRecord = record.Players.SingleOrDefault(x => x.Id == player.Id);
            if (playerRecord is null)
            {
                playerRecord = new GameSessionPlayerRecord
                {
                    Id = player.Id,
                    GameSessionId = session.Id,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };
                record.Players.Add(playerRecord);
            }

            playerRecord.DisplayName = player.DisplayName;
            playerRecord.EntryAmount = player.EntryAmount.Amount;
            playerRecord.TimeCharge = player.TimeCharge.Amount;
            playerRecord.AdditionalAmount = player.AdditionalAmount.Amount;
            playerRecord.CurrencyCode = player.EntryAmount.CurrencyCode;
            playerRecord.GameSessionId = session.Id;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<GameSession?> FindAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var record = await db.GameSessions.AsNoTracking()
            .Include(x => x.Players)
            .SingleOrDefaultAsync(x => x.Id == sessionId, cancellationToken);
        return record is null ? null : Restore(record);
    }

    public async Task<IReadOnlyList<GameSession>> FindOpenAsync(CancellationToken cancellationToken = default)
    {
        var records = await db.GameSessions.AsNoTracking()
            .Include(x => x.Players)
            .Where(x => x.Status == nameof(GameSessionStatus.Active) || x.Status == nameof(GameSessionStatus.Paused))
            .ToListAsync(cancellationToken);
        return records.OrderBy(x => x.StartedAtUtc).Select(Restore).ToArray();
    }

    public async Task<IReadOnlyList<GameSession>> FindRecentAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(limit), "The recent-session limit must be between 1 and 1000.");

        var openRecords = await db.GameSessions.AsNoTracking()
            .Include(x => x.Players)
            .Where(x => x.Status == nameof(GameSessionStatus.Active)
                || x.Status == nameof(GameSessionStatus.Paused))
            .ToListAsync(cancellationToken);

        // SQLite's EF provider cannot translate DateTimeOffset ordering. Sort these
        // persisted UTC instants in memory instead of failing on real session data.
        var completedRecords = (await db.GameSessions.AsNoTracking()
                .Include(x => x.Players)
                .Where(x => x.Status == nameof(GameSessionStatus.Completed))
                .ToListAsync(cancellationToken))
            .OrderByDescending(x => x.StartedAtUtc)
            .Take(limit)
            .ToArray();

        return openRecords.Concat(completedRecords)
            .OrderByDescending(x => x.StartedAtUtc)
            .Select(Restore)
            .ToArray();
    }

    private static GameSession Restore(GameSessionRecord record)
    {
        if (!Enum.TryParse<GameSessionStatus>(record.Status, ignoreCase: false, out var status))
            throw new InvalidDataException($"Stored game session {record.Id} has an unknown status.");

        var players = record.Players.Select(player =>
        {
            if (!string.Equals(player.CurrencyCode, record.CurrencyCode, StringComparison.Ordinal))
                throw new InvalidDataException($"Player {player.Id} has a currency different from its session.");
            return new GameSessionPlayer(player.Id, player.DisplayName,
                new Money(player.EntryAmount, player.CurrencyCode),
                new Money(player.TimeCharge, player.CurrencyCode),
                new Money(player.AdditionalAmount, player.CurrencyCode));
        }).ToArray();

        return GameSession.Restore(record.Id, record.BranchId, record.DeviceId, record.GameName,
            record.StartedAtUtc, new Money(record.RatePerHour, record.CurrencyCode), players,
            status, record.PausedAtUtc, record.EndedAtUtc,
            TimeSpan.FromTicks(record.AccumulatedPauseTicks), record.LastTransitionAtUtc);
    }
}
