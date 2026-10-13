using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using MG.BusinessPlatform.Infrastructure.Persistence;
using MG.BusinessPlatform.Desktop.Services;

namespace MG.BusinessPlatform.Desktop;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Any(arg => string.Equals(arg, "--start-storefront", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                await StartStorefrontIfEnabledAsync();
            }
            catch (Exception ex)
            {
                try
                {
                    var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MGBusinessPlatform");
                    Directory.CreateDirectory(directory);
                    await File.AppendAllTextAsync(Path.Combine(directory, "publication-startup.log"),
                        $"{DateTimeOffset.UtcNow:O} Startup error: {ex}{Environment.NewLine}");
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            finally
            {
                Shutdown();
            }
            return;
        }

        new MainWindow().Show();
    }

    private static async Task StartStorefrontIfEnabledAsync()
    {
        var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MGBusinessPlatform");
        Directory.CreateDirectory(dataDirectory);
        var databasePath = Path.Combine(dataDirectory, "mg-business-platform.db");
        var pidPath = Path.Combine(dataDirectory, "storefront.pid");
        var options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseSqlite($"Data Source={databasePath};Cache=Shared;Default Timeout=15")
            .Options;

        await using var db = new BusinessDbContext(options);
        await DatabaseInitializer.InitializeAsync(db);

        var settings = await db.StoreSettings.AsNoTracking().SingleAsync(x => x.Id == 1);
        if (!settings.OnlineOrderingEnabled)
            return;

        var localRunning = false;
        using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) })
        {
            try
            {
                using var response = await client.GetAsync("http://127.0.0.1:8080/health/live");
                localRunning = response.IsSuccessStatusCode;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
        }

        var baseDirectory = AppContext.BaseDirectory;
        if (!localRunning)
        {
            var candidates = new[]
            {
                Path.Combine(baseDirectory, "storefront", "MG.BusinessPlatform.Storefront.exe"),
                Path.Combine(baseDirectory, "MG.BusinessPlatform.Storefront.exe")
            };
            var executable = candidates.FirstOrDefault(File.Exists);
            if (executable is not null)
            {
                var start = new ProcessStartInfo(executable)
                {
                    WorkingDirectory = Path.GetDirectoryName(executable)!,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                start.Environment["MG_BUSINESS_DB_PATH"] = databasePath;
                start.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:8080";
                using var process = Process.Start(start);
                if (process is not null)
                    await File.WriteAllTextAsync(pidPath, process.Id.ToString(CultureInfo.InvariantCulture));
            }
        }

        if (settings.PublicPublicationEnabled && !string.IsNullOrWhiteSpace(settings.PublicHostname))
        {
            var hosting = new PublicHostingManager(baseDirectory, dataDirectory);
            var result = await hosting.StartAsync(settings.PublicHostname);
            await File.AppendAllTextAsync(Path.Combine(dataDirectory, "publication-startup.log"),
                $"{DateTimeOffset.UtcNow:O} {result.Message}{Environment.NewLine}");
        }
    }
}
