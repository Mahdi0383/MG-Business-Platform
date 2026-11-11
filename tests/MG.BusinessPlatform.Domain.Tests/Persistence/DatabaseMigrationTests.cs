using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MG.BusinessPlatform.Infrastructure.Persistence;

namespace MG.BusinessPlatform.Domain.Tests.Persistence;

public sealed class DatabaseMigrationTests
{
    [Fact]
    public async Task FreshDatabase_MigratesAndCanPersistStoreSettingsAndCatalog()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);

        await db.Database.MigrateAsync();

        db.StoreSettings.Add(new StoreSettings
        {
            Id = 1,
            StoreName = "Test Store",
            CurrencyCode = "IRR",
            OnlineOrderingEnabled = false,
            OnlineReservationsEnabled = true,
            PublicPublicationEnabled = false,
            PublicHostname = string.Empty,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        var reservationResourceId = Guid.NewGuid();
        db.ReservationResources.Add(new ReservationResourceRecord
        {
            Id = reservationResourceId,
            BranchId = Guid.NewGuid(),
            Name = "Test Station",
            ResourceType = "GameStation",
            Capacity = 2,
            IsAvailable = true
        });
        db.CustomerReservations.Add(new CustomerReservationRecord
        {
            Id = Guid.NewGuid(),
            ResourceId = reservationResourceId,
            ReservationNumber = "RSV-MIGRATION-TEST",
            IdempotencyKey = "migration-reservation-key-0001",
            GuestName = "Reservation Guest",
            GuestPhone = "09120000000",
            GuestCount = 2,
            StartsAtUtc = DateTimeOffset.UtcNow.AddHours(2),
            EndsAtUtc = DateTimeOffset.UtcNow.AddHours(3),
            Status = "Requested",
            Notes = string.Empty,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        db.StoreProducts.Add(new StoreProduct
        {
            Id = Guid.NewGuid(),
            Sku = "MIGRATION-1",
            Name = "Migration test",
            UnitPrice = 123.45m,
            UnitCost = 80m,
            CurrencyCode = "IRR",
            IsAvailable = true,
            StockQuantity = 8,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.StockMovements.Add(new StockMovementRecord
        {
            Id = Guid.NewGuid(), ProductId = db.StoreProducts.Local.Single().Id,
            OccurredAt = DateTimeOffset.UtcNow, MovementType = "OpeningBalance",
            Quantity = 8, SignedQuantity = 8, UnitCost = 80m, CurrencyCode = "IRR",
            Reference = "MIGRATION", Note = string.Empty
        });
        db.Customers.Add(new CustomerRecord
        {
            Id = Guid.NewGuid(), DisplayName = "Test Customer", Phone = "0912", Email = string.Empty,
            Address = string.Empty, CreatedAt = DateTimeOffset.UtcNow, IsActive = true
        });
        var supplierId = Guid.NewGuid();
        db.Suppliers.Add(new SupplierRecord
        {
            Id = supplierId, DisplayName = "Test Supplier", Phone = "021", Email = string.Empty,
            Address = string.Empty, TaxIdentifier = string.Empty, PaymentTerms = string.Empty,
            CreatedAt = DateTimeOffset.UtcNow, IsActive = true
        });
        var issueDate = new DateOnly(2026, 10, 1);
        var dueDate = new DateOnly(2026, 11, 15);
        db.PurchaseInvoices.Add(new PurchaseInvoiceRecord
        {
            Id = Guid.NewGuid(), InvoiceNumber = "PUR-DUE-TEST", BranchId = Guid.NewGuid(),
            SupplierId = supplierId, IssueDate = issueDate, DueDate = dueDate,
            CurrencyCode = "IRR", TotalAmount = 100m, PaidAmount = 0m,
            Status = "Received", CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        Assert.Equal("Test Store", (await db.StoreSettings.SingleAsync()).StoreName);
        Assert.True((await db.StoreSettings.SingleAsync()).OnlineReservationsEnabled);
        Assert.Equal("Test Station", (await db.ReservationResources.SingleAsync()).Name);
        Assert.Equal("Reservation Guest", (await db.CustomerReservations.SingleAsync()).GuestName);
        var product = await db.StoreProducts.SingleAsync();
        Assert.Equal(123.45m, product.UnitPrice);
        Assert.Equal(80m, product.UnitCost);
        Assert.Equal(8, (await db.StockMovements.SingleAsync()).Quantity);
        Assert.Equal("Test Customer", (await db.Customers.SingleAsync()).DisplayName);
        Assert.Equal("Test Supplier", (await db.Suppliers.SingleAsync()).DisplayName);
        var persistedPurchase = await db.PurchaseInvoices.SingleAsync();
        Assert.Equal(issueDate, persistedPurchase.IssueDate);
        Assert.Equal(dueDate, persistedPurchase.DueDate!.Value);
        Assert.True(await db.AuditEvents.AnyAsync());
        var appliedMigrations = await db.Database.GetAppliedMigrationsAsync();
        Assert.Contains("20261013000000_InitialStorefrontSchema", appliedMigrations);
        Assert.Contains("20261013010000_AddCustomerSupplierTables", appliedMigrations);
        Assert.Contains("20261013020000_AddStockMovementLedger", appliedMigrations);
        Assert.Contains("20261013030000_AddAccountingLedger", appliedMigrations);
        Assert.Contains("20261013040000_AddGameSessions", appliedMigrations);
        Assert.Contains("20261016000000_AddOnlineReservations", appliedMigrations);
        Assert.Contains("20261016010000_AddPurchasingAccounting", appliedMigrations);
        Assert.Contains("20261016020000_AddAuditEvents", appliedMigrations);
        Assert.Contains("20261016030000_AddInvoiceDueDates", appliedMigrations);
    }

    [Fact]
    public async Task DatabaseConstraint_RejectsNegativeDecimalPrice()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await db.Database.MigrateAsync();

        db.StoreProducts.Add(new StoreProduct
        {
            Id = Guid.NewGuid(),
            Sku = "NEGATIVE-PRICE",
            Name = "Invalid price",
            UnitPrice = -1.25m,
            CurrencyCode = "IRR",
            IsAvailable = true,
            StockQuantity = 1,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public async Task InitializeAsync_CreatesDefaultStoreSettingsAndLedgerAccountsForFreshDatabase()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);

        await DatabaseInitializer.InitializeAsync(db);

        var settings = await db.StoreSettings.AsNoTracking().SingleAsync();
        Assert.Equal(1, settings.Id);
        Assert.Equal("فروشگاه شما", settings.StoreName);
        Assert.Equal("IRR", settings.CurrencyCode);
        Assert.False(settings.OnlineOrderingEnabled);
        Assert.False(settings.OnlineReservationsEnabled);
        Assert.False(settings.PublicPublicationEnabled);
        Assert.Equal(string.Empty, settings.PublicHostname);

        var accounts = await db.LedgerAccounts.AsNoTracking()
            .OrderBy(account => account.Code)
            .ToArrayAsync();
        Assert.Equal(
            new[] { "1000", "1010", "1020", "1100", "1200", "2000", "3000", "4000", "5000", "6000" },
            accounts.Select(account => account.Code).ToArray());
        Assert.All(accounts, account =>
        {
            Assert.True(account.IsActive);
            Assert.True(account.IsPostingAllowed);
        });
    }

    [Fact]
    public async Task InitializeAsync_SeedsMissingDefaultLedgerAccountsWithoutOverwritingExistingAccounts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BusinessDbContext>().UseSqlite(connection).Options;
        await using var db = new BusinessDbContext(options);
        await db.Database.MigrateAsync();

        // Initialization must not replace store-specific configuration in an existing database.
        db.StoreSettings.Add(new StoreSettings
        {
            Id = 1,
            StoreName = "Configured Store",
            CurrencyCode = "USD",
            OnlineOrderingEnabled = true,
            OnlineReservationsEnabled = true,
            PublicPublicationEnabled = true,
            PublicHostname = "shop.example.test",
            UpdatedAt = DateTimeOffset.UtcNow
        });

        db.LedgerAccounts.Add(new LedgerAccountRecord
        {
            Id = Guid.NewGuid(),
            Code = "1000",
            Name = "Custom cash account",
            AccountType = "Asset",
            IsPostingAllowed = false,
            IsActive = false
        });
        await db.SaveChangesAsync();

        await DatabaseInitializer.InitializeAsync(db);

        var storeSettings = await db.StoreSettings.AsNoTracking().SingleAsync();
        Assert.Equal("Configured Store", storeSettings.StoreName);
        Assert.Equal("USD", storeSettings.CurrencyCode);
        Assert.True(storeSettings.OnlineOrderingEnabled);
        Assert.True(storeSettings.OnlineReservationsEnabled);
        Assert.True(storeSettings.PublicPublicationEnabled);
        Assert.Equal("shop.example.test", storeSettings.PublicHostname);

        var accounts = await db.LedgerAccounts.AsNoTracking().ToDictionaryAsync(x => x.Code);
        Assert.Equal(10, accounts.Count);
        Assert.Equal("Custom cash account", accounts["1000"].Name);
        Assert.Equal("Asset", accounts["1000"].AccountType);
        Assert.False(accounts["1000"].IsPostingAllowed);
        Assert.False(accounts["1000"].IsActive);
        Assert.Contains("1010", accounts.Keys);
        Assert.Contains("1100", accounts.Keys);
        Assert.Contains("4000", accounts.Keys);
        Assert.Contains("6000", accounts.Keys);

        // Running initialization repeatedly must be safe and must not create duplicates.
        await DatabaseInitializer.InitializeAsync(db);
        Assert.Equal(10, await db.LedgerAccounts.CountAsync());
    }


    [Fact]
    public async Task InitializeAsync_BackupsExistingDatabaseBeforeApplyingPendingMigrations()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mg-db-migration-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "store.db");

        try
        {
            var options = new DbContextOptionsBuilder<BusinessDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = databasePath,
                    Pooling = false
                }.ToString())
                .Options;
            await using var db = new BusinessDbContext(options);

            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261013000000_InitialStorefrontSchema");

            await db.Database.OpenConnectionAsync();
            await using (var seed = db.Database.GetDbConnection().CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO StoreSettings
                        (Id, StoreName, CurrencyCode, OnlineOrderingEnabled, PublicPublicationEnabled, PublicHostname, UpdatedAt)
                    VALUES
                        (1, 'Pre-migration store', 'IRR', 0, 0, '', '2026-01-01T00:00:00+00:00');

                    INSERT INTO StoreProducts
                        (Id, Sku, Name, Description, UnitPrice, CurrencyCode, IsAvailable, StockQuantity, UpdatedAt)
                    VALUES
                        ('00000000-0000-0000-0000-000000000123', 'LEGACY-1', 'Legacy product',
                         'Created before schema upgrade', '125.50', 'IRR', 1, 7, '2026-01-01T00:00:00+00:00');
                    """;
                await seed.ExecuteNonQueryAsync();
            }
            await db.Database.CloseConnectionAsync();

            await DatabaseInitializer.InitializeAsync(db);

            var backups = Directory.GetFiles(directory, "store.db.pre-migration-*.bak");
            Assert.Single(backups);
            await new DatabaseMaintenanceService().VerifyIntegrityAsync(backups[0]);

            await using (var backupConnection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = backups[0],
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString()))
            {
                await backupConnection.OpenAsync();
                await using var backupCommand = backupConnection.CreateCommand();
                backupCommand.CommandText = "SELECT StoreName FROM StoreSettings WHERE Id = 1;";
                Assert.Equal("Pre-migration store", Convert.ToString(
                    await backupCommand.ExecuteScalarAsync(),
                    System.Globalization.CultureInfo.InvariantCulture));

                backupCommand.CommandText = "SELECT Name FROM StoreProducts WHERE Sku = 'LEGACY-1';";
                Assert.Equal("Legacy product", Convert.ToString(
                    await backupCommand.ExecuteScalarAsync(),
                    System.Globalization.CultureInfo.InvariantCulture));

                backupCommand.CommandText = "SELECT COUNT(*) FROM pragma_table_info('StoreProducts') WHERE name = 'UnitCost';";
                Assert.Equal(0, Convert.ToInt32(
                    await backupCommand.ExecuteScalarAsync(),
                    System.Globalization.CultureInfo.InvariantCulture));
            }

            var upgradedProduct = await db.StoreProducts.AsNoTracking().SingleAsync();
            Assert.Equal("LEGACY-1", upgradedProduct.Sku);
            Assert.Equal(125.50m, upgradedProduct.UnitPrice);
            Assert.Equal(0m, upgradedProduct.UnitCost);
            Assert.Equal("Pre-migration store",
                await db.StoreSettings.AsNoTracking().Select(x => x.StoreName).SingleAsync());

            var appliedMigrations = await db.Database.GetAppliedMigrationsAsync();
            Assert.Contains("20261016030000_AddInvoiceDueDates", appliedMigrations);

            // A second startup with no pending migrations must not create another backup.
            await DatabaseInitializer.InitializeAsync(db);
            Assert.Single(Directory.GetFiles(directory, "store.db.pre-migration-*.bak"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }



    [Fact]
    public async Task InitializeAsync_WaitsForExistingInitializationLockBeforeApplyingMigrations()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mg-db-initialization-lock-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "store.db");
        var lockPath = databasePath + ".migration.lock";
        var options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Pooling = false
            }.ToString())
            .Options;

        try
        {
            await using (var setupDb = new BusinessDbContext(options))
            {
                var migrator = setupDb.GetService<IMigrator>();
                await migrator.MigrateAsync("20261013000000_InitialStorefrontSchema");
            }

            // Hold the same OS-level exclusive lock used by another application process.
            await using var heldLock = new FileStream(
                lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            await using var db = new BusinessDbContext(options);

            var initializationTask = DatabaseInitializer.InitializeAsync(db);
            await Task.Delay(TimeSpan.FromMilliseconds(250));

            Assert.False(initializationTask.IsCompleted,
                "Initialization must wait while another process owns the migration lock.");

            await heldLock.DisposeAsync();
            await initializationTask.WaitAsync(TimeSpan.FromSeconds(15));

            var appliedMigrations = await db.Database.GetAppliedMigrationsAsync();
            Assert.Contains("20261016030000_AddInvoiceDueDates", appliedMigrations);
            Assert.Single(Directory.GetFiles(directory, "store.db.pre-migration-*.bak"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }


    [Fact]
    public async Task InitializeAsync_HonorsCancellationWhileWaitingForInitializationLock()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mg-db-initialization-cancel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "store.db");
        var lockPath = databasePath + ".migration.lock";
        var options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Pooling = false
            }.ToString())
            .Options;

        try
        {
            await using (var setupDb = new BusinessDbContext(options))
            {
                var migrator = setupDb.GetService<IMigrator>();
                await migrator.MigrateAsync("20261013000000_InitialStorefrontSchema");
            }

            // Simulate another process owning the migration lock. Cancellation must
            // stop this initializer before it creates a backup or changes the schema.
            await using var heldLock = new FileStream(
                lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            await using var db = new BusinessDbContext(options);
            using var cancellation = new CancellationTokenSource();

            var initializationTask = DatabaseInitializer.InitializeAsync(db, cancellation.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(250));

            Assert.False(initializationTask.IsCompleted,
                "Initialization must remain pending while another process owns the migration lock.");

            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => initializationTask);

            var appliedMigrations = await db.Database.GetAppliedMigrationsAsync();
            Assert.Contains("20261013000000_InitialStorefrontSchema", appliedMigrations);
            Assert.DoesNotContain("20261016030000_AddInvoiceDueDates", appliedMigrations);
            Assert.Empty(Directory.GetFiles(directory, "store.db.pre-migration-*.bak"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }


    [Fact]
    public async Task InitializeAsync_RejectsDatabaseWithoutRecognizedMigrationHistoryWithoutChangingLegacyData()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mg-db-unknown-migration-history-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "store.db");

        try
        {
            await using (var legacyConnection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Pooling = false
            }.ToString()))
            {
                await legacyConnection.OpenAsync();
                await using var setup = legacyConnection.CreateCommand();
                setup.CommandText = """
                    CREATE TABLE StoreSettings (Id INTEGER PRIMARY KEY, StoreName TEXT NOT NULL);
                    CREATE TABLE StoreProducts (Id TEXT PRIMARY KEY, Sku TEXT NOT NULL, Name TEXT NOT NULL);
                    CREATE TABLE __EFMigrationsHistory (MigrationId TEXT NOT NULL PRIMARY KEY, ProductVersion TEXT NOT NULL);
                    INSERT INTO StoreSettings (Id, StoreName) VALUES (1, 'Legacy store');
                    INSERT INTO StoreProducts (Id, Sku, Name) VALUES ('legacy-1', 'LEGACY-1', 'Legacy product');
                    """;
                await setup.ExecuteNonQueryAsync();
            }

            var options = new DbContextOptionsBuilder<BusinessDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = databasePath,
                    Pooling = false
                }.ToString())
                .Options;
            await using var db = new BusinessDbContext(options);

            var exception = await Assert.ThrowsAsync<InvalidDataException>(
                () => DatabaseInitializer.InitializeAsync(db));
            Assert.Contains("does not contain the MG Business Platform migration history", exception.Message);

            await using var verify = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());
            await verify.OpenAsync();

            await using var command = verify.CreateCommand();
            command.CommandText = "SELECT StoreName FROM StoreSettings WHERE Id = 1;";
            Assert.Equal("Legacy store", Convert.ToString(
                await command.ExecuteScalarAsync(),
                System.Globalization.CultureInfo.InvariantCulture));

            command.CommandText = "SELECT Name FROM StoreProducts WHERE Sku = 'LEGACY-1';";
            Assert.Equal("Legacy product", Convert.ToString(
                await command.ExecuteScalarAsync(),
                System.Globalization.CultureInfo.InvariantCulture));

            command.CommandText = "SELECT COUNT(*) FROM __EFMigrationsHistory;";
            Assert.Equal(0, Convert.ToInt32(
                await command.ExecuteScalarAsync(),
                System.Globalization.CultureInfo.InvariantCulture));

            command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('StoreProducts') WHERE name = 'UnitCost';";
            Assert.Equal(0, Convert.ToInt32(
                await command.ExecuteScalarAsync(),
                System.Globalization.CultureInfo.InvariantCulture));

            Assert.Empty(Directory.GetFiles(directory, "store.db.pre-migration-*.bak"));
            Assert.Empty(Directory.GetFiles(directory, "store.db.pre-migration-*.bak.tmp-*"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

}
