using Microsoft.EntityFrameworkCore;
using MG.BusinessPlatform.Infrastructure.Persistence;

namespace MG.BusinessPlatform.Domain.Tests.Persistence;

public sealed class DatabaseMaintenanceServiceTests
{
    [Fact]
    public async Task BackupAndRestore_PreserveDatabaseAndCreateSafetyBackup()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mg-db-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "store.db");
        var backupPath = Path.Combine(directory, "store-backup.db");
        var service = new DatabaseMaintenanceService();

        try
        {
            await SetStoreNameAsync(databasePath, "Before backup");
            var backup = await service.CreateBackupAsync(databasePath, backupPath);
            Assert.True(backup.SizeBytes > 0);
            await service.VerifyIntegrityAsync(backup.Path);

            await SetStoreNameAsync(databasePath, "Changed after backup");
            var restored = await service.RestoreBackupAsync(backupPath, databasePath);

            Assert.Equal(databasePath, restored.Path);
            Assert.Equal("Before backup", await ReadStoreNameAsync(databasePath));
            Assert.Single(Directory.GetFiles(directory, "store.db.pre-restore-*.bak"));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public async Task RestoreBackup_WithCorruptBackup_LeavesCurrentDatabaseUntouched()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mg-db-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "store.db");
        var corruptBackupPath = Path.Combine(directory, "corrupt-backup.db");
        var service = new DatabaseMaintenanceService();

        try
        {
            await SetStoreNameAsync(databasePath, "Keep this data");
            await File.WriteAllTextAsync(corruptBackupPath, "not a SQLite database");

            await Assert.ThrowsAnyAsync<Exception>(() =>
                service.RestoreBackupAsync(corruptBackupPath, databasePath));

            Assert.Equal("Keep this data", await ReadStoreNameAsync(databasePath));
            Assert.Empty(Directory.GetFiles(directory, "store.db.pre-restore-*.bak"));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public async Task CreateBackup_RejectsSameSourceAndDestination()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mg-db-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "store.db");
        try
        {
            await SetStoreNameAsync(databasePath, "Store");
            var service = new DatabaseMaintenanceService();
            await Assert.ThrowsAsync<ArgumentException>(() => service.CreateBackupAsync(databasePath, databasePath));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task SetStoreNameAsync(string path, string name)
    {
        var options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False").Options;
        await using var db = new BusinessDbContext(options);
        await db.Database.MigrateAsync();
        var settings = await db.StoreSettings.SingleOrDefaultAsync(x => x.Id == 1);
        if (settings is null)
        {
            db.StoreSettings.Add(new StoreSettings
            {
                Id = 1,
                StoreName = name,
                CurrencyCode = "IRR",
                PublicHostname = string.Empty,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            settings.StoreName = name;
        }
        await db.SaveChangesAsync();
    }

    private static async Task<string> ReadStoreNameAsync(string path)
    {
        var options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False").Options;
        await using var db = new BusinessDbContext(options);
        return (await db.StoreSettings.SingleAsync(x => x.Id == 1)).StoreName;
    }
}
