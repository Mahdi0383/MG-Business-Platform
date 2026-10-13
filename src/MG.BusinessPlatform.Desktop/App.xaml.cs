using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using MG.BusinessPlatform.Infrastructure.Persistence;

namespace MG.BusinessPlatform.Desktop;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Any(arg => string.Equals(arg, "--start-storefront", StringComparison.OrdinalIgnoreCase)))
        {
            try { await StartStorefrontIfEnabledAsync(); }
            finally { Shutdown(); }
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
        await db.Database.EnsureCreatedAsync();
        if (!await db.StoreSettings.AnyAsync())
        {
            db.StoreSettings.Add(new StoreSettings
            {
                Id = 1,
                StoreName = "فروشگاه شما",
                CurrencyCode = "IRR",
                OnlineOrderingEnabled = false,
                PublicHostname = string.Empty,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var settings = await db.StoreSettings.AsNoTracking().SingleAsync(x => x.Id == 1);
        if (!settings.OnlineOrderingEnabled)
            return;

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        try
        {
            using var response = await client.GetAsync("http://127.0.0.1:8080/health/live");
            if (response.IsSuccessStatusCode)
                return;
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }

        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "storefront", "MG.BusinessPlatform.Storefront.exe"),
            Path.Combine(baseDirectory, "MG.BusinessPlatform.Storefront.exe")
        };
        var executable = candidates.FirstOrDefault(File.Exists);
        if (executable is null)
            return;

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
