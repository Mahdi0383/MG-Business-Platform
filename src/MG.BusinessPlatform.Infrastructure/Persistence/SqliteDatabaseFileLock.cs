using System.Diagnostics;

namespace MG.BusinessPlatform.Infrastructure.Persistence;

/// <summary>
/// Coordinates file-backed SQLite maintenance operations across processes.
/// The lock file is intentionally retained; the exclusive OS handle represents ownership.
/// </summary>
internal static class SqliteDatabaseFileLock
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);

    public static async Task<FileStream> AcquireAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var fullDatabasePath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullDatabasePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var lockPath = fullDatabasePath + ".migration.lock";
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // The OS releases this exclusive handle if the owning process exits unexpectedly.
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException exception) when (IsFileLockContention(exception))
            {
                if (stopwatch.Elapsed >= Timeout)
                {
                    throw new TimeoutException(
                        $"Timed out after {Timeout.TotalSeconds:0} seconds waiting for database maintenance lock '{lockPath}'.",
                        exception);
                }

                await Task.Delay(RetryDelay, cancellationToken);
            }
        }
    }

    private static bool IsFileLockContention(IOException exception)
    {
        // Windows: ERROR_SHARING_VIOLATION (32) / ERROR_LOCK_VIOLATION (33).
        // Unix: EAGAIN/EWOULDBLOCK (11), as surfaced by FileShare.None.
        var nativeErrorCode = exception.HResult & 0xFFFF;
        return nativeErrorCode is 11 or 32 or 33;
    }
}
