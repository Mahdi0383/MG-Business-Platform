using Microsoft.Data.Sqlite;

namespace MG.BusinessPlatform.Infrastructure.Persistence;

public sealed record DatabaseBackupResult(string Path, long SizeBytes, DateTimeOffset CreatedAt);

/// <summary>
/// Creates consistent SQLite backups through SQLite's online backup API and verifies
/// integrity before replacing any destination. Restore always creates a safety backup first.
/// The caller must coordinate restore with application shutdown/no open target connections.
/// </summary>
public sealed class DatabaseMaintenanceService
{
    public async Task<DatabaseBackupResult> CreateBackupAsync(
        string sourceDatabasePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var source = Path.GetFullPath(sourceDatabasePath);
        var destination = Path.GetFullPath(destinationPath);
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Backup destination must differ from the source database.", nameof(destinationPath));
        if (!File.Exists(source))
            throw new FileNotFoundException("Source database was not found.", source);

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporaryPath = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await CopyDatabaseAsync(source, temporaryPath, cancellationToken);
            await VerifyIntegrityAsync(temporaryPath, cancellationToken);
            await VerifyBusinessSchemaAsync(temporaryPath, cancellationToken);
            File.Move(temporaryPath, destination, overwrite: true);
            var info = new FileInfo(destination);
            return new DatabaseBackupResult(destination, info.Length, DateTimeOffset.UtcNow);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public async Task<DatabaseBackupResult> RestoreBackupAsync(
        string backupPath,
        string targetDatabasePath,
        CancellationToken cancellationToken = default)
    {
        var backup = Path.GetFullPath(backupPath);
        var target = Path.GetFullPath(targetDatabasePath);
        if (string.Equals(backup, target, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Backup and target paths must differ.", nameof(targetDatabasePath));
        if (!File.Exists(backup))
            throw new FileNotFoundException("Backup file was not found.", backup);

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        // EF Core's default SQLite pooling can keep a connection alive after its
        // DbContext is disposed. Release this process's idle pooled connections
        // before checking WAL/SHM files; otherwise a normal restore can be rejected
        // even though the desktop app has stopped all active database work.
        SqliteConnection.ClearAllPools();

        if (File.Exists(target + "-wal") || File.Exists(target + "-shm") || File.Exists(target + "-journal"))
            throw new InvalidOperationException("SQLite WAL, SHM or rollback-journal sidecar files still exist. Stop every process using the database before restoring.");
        var temporaryPath = target + ".restore-" + Guid.NewGuid().ToString("N");
        string? safetyBackupPath = null;
        try
        {
            await CopyDatabaseAsync(backup, temporaryPath, cancellationToken);
            await VerifyIntegrityAsync(temporaryPath, cancellationToken);
            await VerifyBusinessSchemaAsync(temporaryPath, cancellationToken);

            if (File.Exists(target))
            {
                safetyBackupPath = target + ".pre-restore-" + DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + ".bak";
                await CopyDatabaseAsync(target, safetyBackupPath, cancellationToken);
                await VerifyIntegrityAsync(safetyBackupPath, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, target, overwrite: true);
            var info = new FileInfo(target);
            return new DatabaseBackupResult(target, info.Length, DateTimeOffset.UtcNow);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public async Task VerifyIntegrityAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(databasePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Database file was not found.", fullPath);

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        var result = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"SQLite integrity check failed: {result ?? "no result"}.");
    }

    private async Task VerifyBusinessSchemaAsync(string databasePath, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name IN ('StoreSettings', 'StoreProducts', '__EFMigrationsHistory');
            """;
        var result = Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
        if (result != 3)
            throw new InvalidDataException(
                "The selected file is a valid SQLite database but is not an MG Business Platform database. The current data was not replaced.");
    }

    private static async Task CopyDatabaseAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        if (File.Exists(destinationPath))
            File.Delete(destinationPath);
        await using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());
        await source.OpenAsync(cancellationToken);
        await destination.OpenAsync(cancellationToken);
        source.BackupDatabase(destination);
    }
}
