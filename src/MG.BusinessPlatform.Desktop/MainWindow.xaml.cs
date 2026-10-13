using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using MG.BusinessPlatform.Infrastructure.Persistence;
using MG.BusinessPlatform.Desktop.Services;

namespace MG.BusinessPlatform.Desktop;

public partial class MainWindow : Window
{
    private readonly string _databasePath;
    private readonly DispatcherTimer _refreshTimer;
    private readonly string _storefrontPidPath;
    private readonly PublicHostingManager _publicHostingManager;
    private Process? _storefrontProcess;
    private static readonly HttpClient HealthClient = new() { Timeout = TimeSpan.FromSeconds(1) };

    public MainWindow()
    {
        InitializeComponent();
        var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MGBusinessPlatform");
        Directory.CreateDirectory(dataDirectory);
        _databasePath = Path.Combine(dataDirectory, "mg-business-platform.db");
        _storefrontPidPath = Path.Combine(dataDirectory, "storefront.pid");
        _publicHostingManager = new PublicHostingManager(AppContext.BaseDirectory, dataDirectory);
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        Loaded += MainWindow_Loaded;
        Closed += (_, _) => _refreshTimer.Stop();
    }

    private DbContextOptions<BusinessDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<BusinessDbContext>()
            .UseSqlite($"Data Source={_databasePath};Cache=Shared;Default Timeout=15")
            .Options;

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await using var db = new BusinessDbContext(CreateOptions());
            await db.Database.MigrateAsync();
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

            await LoadSettingsAsync();
            await RefreshAsync();
            _refreshTimer.Start();
            StatusText.Text = "سامانه آماده است. اطلاعات به‌صورت محلی ذخیره می‌شوند.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"خطا در آماده‌سازی پایگاه داده: {ex.Message}";
        }
    }

    private async Task LoadSettingsAsync()
    {
        await using var db = new BusinessDbContext(CreateOptions());
        var settings = await db.StoreSettings.AsNoTracking().SingleAsync(x => x.Id == 1);
        StoreNameInput.Text = settings.StoreName;
        CurrencyInput.Text = settings.CurrencyCode;
        HostnameInput.Text = settings.PublicHostname;
        OnlineOrdersInput.IsChecked = settings.OnlineOrderingEnabled;
        StoreUrlText.Text = string.IsNullOrWhiteSpace(settings.PublicHostname)
            ? "http://127.0.0.1:8080"
            : $"دامنه ثبت‌شده: {settings.PublicHostname} — انتشار عمومی هنوز تأیید نشده است.";
        PublicHostingStatusText.Text = settings.PublicPublicationEnabled
            ? "انتشار عمومی برای راه‌اندازی خودکار فعال است؛ وضعیت دسترسی باید بررسی شود."
            : "انتشار عمومی راه‌اندازی نشده است.";
    }

    private async Task RefreshAsync()
    {
        try
        {
            await using var db = new BusinessDbContext(CreateOptions());
            ProductsGrid.ItemsSource = await db.StoreProducts.AsNoTracking()
                .OrderBy(x => x.Name).ToListAsync();
            OrdersGrid.ItemsSource = await db.CustomerOrders.AsNoTracking()
                .OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync();
            ProductCountText.Text = (await db.StoreProducts.CountAsync(x => x.IsAvailable))
                .ToString("N0", CultureInfo.GetCultureInfo("fa-IR"));
            OrderCountText.Text = (await db.CustomerOrders.CountAsync())
                .ToString("N0", CultureInfo.GetCultureInfo("fa-IR"));
            var running = await IsStorefrontRunningAsync();
            ServerStatusText.Text = running ? "در حال اجرا" : "متوقف";
            StartServerButton.IsEnabled = !running;
            StopServerButton.IsEnabled = running;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"خطا در به‌روزرسانی اطلاعات: {ex.Message}";
        }
    }

    private async void AddProduct_Click(object sender, RoutedEventArgs e)
    {
        var sku = SkuInput.Text.Trim();
        var name = NameInput.Text.Trim();
        if (sku.Length is < 1 or > 80 || name.Length is < 1 or > 200)
        {
            StatusText.Text = "کد کالا و نام محصول را وارد کنید.";
            return;
        }

        if (!decimal.TryParse(PriceInput.Text.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out var price)
            && !decimal.TryParse(PriceInput.Text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out price))
        {
            StatusText.Text = "قیمت واردشده معتبر نیست.";
            return;
        }
        if (price < 0m || !int.TryParse(StockInput.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var stock)
            && !int.TryParse(StockInput.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out stock))
        {
            StatusText.Text = "قیمت یا موجودی معتبر نیست.";
            return;
        }
        if (stock < 0)
        {
            StatusText.Text = "موجودی نمی‌تواند منفی باشد.";
            return;
        }

        try
        {
            await using var db = new BusinessDbContext(CreateOptions());
            var settings = await db.StoreSettings.AsNoTracking().SingleAsync(x => x.Id == 1);
            db.StoreProducts.Add(new StoreProduct
            {
                Id = Guid.NewGuid(),
                Sku = sku.ToUpperInvariant(),
                Name = name,
                Description = string.Empty,
                UnitPrice = price,
                CurrencyCode = settings.CurrencyCode,
                IsAvailable = true,
                StockQuantity = stock,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            SkuInput.Clear();
            NameInput.Clear();
            PriceInput.Clear();
            StockInput.Clear();
            StatusText.Text = "محصول ذخیره شد.";
            await RefreshAsync();
        }
        catch (DbUpdateException)
        {
            StatusText.Text = "ذخیره محصول انجام نشد؛ کد کالا احتمالاً تکراری است.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"ذخیره محصول ناموفق بود: {ex.Message}";
        }
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        var storeName = StoreNameInput.Text.Trim();
        var currency = CurrencyInput.Text.Trim().ToUpperInvariant();
        var hostname = HostnameInput.Text.Trim().TrimEnd('.').ToLowerInvariant();

        if (storeName.Length is < 1 or > 200)
        {
            StatusText.Text = "نام فروشگاه باید بین ۱ تا ۲۰۰ نویسه باشد.";
            return;
        }
        if (currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z'))
        {
            StatusText.Text = "کد واحد پول باید سه حرف انگلیسی باشد.";
            return;
        }
        if (hostname.Length > 253 || hostname.Contains('/') || hostname.Contains(':')
            || hostname.Contains(' ') || (hostname.Length > 0 && Uri.CheckHostName(hostname) != UriHostNameType.Dns))
        {
            StatusText.Text = "دامنه را فقط به‌صورت shop.example.com وارد کنید؛ بدون پروتکل یا مسیر.";
            return;
        }

        try
        {
            await using var db = new BusinessDbContext(CreateOptions());
            var settings = await db.StoreSettings.SingleAsync(x => x.Id == 1);
            if (settings.PublicPublicationEnabled
                && !string.Equals(settings.PublicHostname, hostname, StringComparison.OrdinalIgnoreCase))
            {
                StatusText.Text = "ابتدا انتشار HTTPS را متوقف کنید، سپس دامنه را تغییر دهید.";
                return;
            }
            if (settings.PublicPublicationEnabled && OnlineOrdersInput.IsChecked != true)
            {
                StatusText.Text = "ابتدا انتشار HTTPS را متوقف کنید، سپس سفارش آنلاین را غیرفعال کنید.";
                return;
            }
            if (!string.Equals(settings.CurrencyCode, currency, StringComparison.Ordinal)
                && await db.StoreProducts.AnyAsync())
            {
                StatusText.Text = "تغییر واحد پول پس از ثبت کالا مجاز نیست؛ ابتدا باید تبدیل و بازبینی قیمت‌ها انجام شود.";
                return;
            }
            settings.StoreName = storeName;
            settings.CurrencyCode = currency;
            settings.PublicHostname = hostname;
            settings.OnlineOrderingEnabled = OnlineOrdersInput.IsChecked == true;
            settings.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            StatusText.Text = "تنظیمات ذخیره شد. ثبت دامنه به‌تنهایی به معنی انتشار عمومی نیست.";
            await LoadSettingsAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"ذخیره تنظیمات ناموفق بود: {ex.Message}";
        }
    }

    private async void CancelOrder_Click(object sender, RoutedEventArgs e)
    {
        if (OrdersGrid.SelectedItem is not CustomerOrder selected)
        {
            StatusText.Text = "ابتدا یک سفارش را انتخاب کنید.";
            return;
        }

        try
        {
            await using var db = new BusinessDbContext(CreateOptions());
            await using var transaction = await db.Database.BeginTransactionAsync();
            var order = await db.CustomerOrders.Include(x => x.Lines)
                .SingleOrDefaultAsync(x => x.Id == selected.Id);
            if (order is null)
            {
                StatusText.Text = "سفارش پیدا نشد؛ فهرست را تازه‌سازی کنید.";
                return;
            }
            if (!string.Equals(order.Status, "Received", StringComparison.Ordinal))
            {
                StatusText.Text = "فقط سفارش‌های دریافت‌شده و لغونشده قابل لغو هستند.";
                return;
            }

            foreach (var line in order.Lines)
            {
                var product = await db.StoreProducts.SingleOrDefaultAsync(x => x.Id == line.ProductId);
                if (product is null)
                {
                    StatusText.Text = $"کالای سفارش «{line.ProductName}» پیدا نشد؛ برای جلوگیری از مغایرت، لغو انجام نشد.";
                    return;
                }
                product.StockQuantity = checked(product.StockQuantity + line.Quantity);
                product.UpdatedAt = DateTimeOffset.UtcNow;
            }

            order.Status = "Cancelled";
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            StatusText.Text = "سفارش لغو شد و موجودی به‌صورت تراکنشی بازگردانده شد.";
            await RefreshAsync();
        }
        catch (OverflowException)
        {
            StatusText.Text = "بازگرداندن موجودی به دلیل سرریز عددی انجام نشد.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"لغو سفارش ناموفق بود: {ex.Message}";
        }
    }

    private async void ConfigureFirewall_Click(object sender, RoutedEventArgs e)
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "configure-firewall.ps1");
        if (!File.Exists(scriptPath))
        {
            PublicHostingStatusText.Text = "اسکریپت تنظیم فایروال در بسته نصب پیدا نشد.";
            return;
        }

        try
        {
            PublicHostingStatusText.Text = "برای افزودن قانون ورودی TCP 80 و 443، تأیید پنجره دسترسی مدیر ویندوز لازم است.";
            var start = new ProcessStartInfo("powershell.exe")
            {
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var process = Process.Start(start);
            if (process is null)
            {
                PublicHostingStatusText.Text = "راه‌اندازی تنظیم فایروال ناموفق بود.";
                return;
            }
            await process.WaitForExitAsync();
            PublicHostingStatusText.Text = process.ExitCode == 0
                ? "قانون فایروال برای TCP 80 و 443 تنظیم شد. انتقال درگاه‌ها در روتر همچنان باید بررسی شود."
                : $"تنظیم فایروال با کد {process.ExitCode} ناموفق بود.";
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            PublicHostingStatusText.Text = "درخواست دسترسی مدیر لغو شد؛ فایروال تغییر نکرد.";
        }
        catch (Exception ex)
        {
            PublicHostingStatusText.Text = $"تنظیم فایروال ناموفق بود: {ex.Message}";
        }
    }

    private async void StartPublicHosting_Click(object sender, RoutedEventArgs e)
    {
        var hostname = HostnameInput.Text.Trim().TrimEnd('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(hostname) || Uri.CheckHostName(hostname) != UriHostNameType.Dns)
        {
            PublicHostingStatusText.Text = "ابتدا دامنه معتبر را در تنظیمات ذخیره کنید.";
            return;
        }

        try
        {
            await using (var db = new BusinessDbContext(CreateOptions()))
            {
                var settings = await db.StoreSettings.SingleAsync(x => x.Id == 1);
                if (!settings.OnlineOrderingEnabled)
                {
                    PublicHostingStatusText.Text = "ابتدا ثبت سفارش آنلاین را در تنظیمات فعال و ذخیره کنید.";
                    return;
                }
                if (!string.Equals(settings.PublicHostname, hostname, StringComparison.OrdinalIgnoreCase))
                {
                    PublicHostingStatusText.Text = "ابتدا تنظیمات فروشگاه را ذخیره کنید.";
                    return;
                }
            }

            if (!await IsStorefrontRunningAsync())
            {
                PublicHostingStatusText.Text = "ابتدا سرویس وب محلی راه‌اندازی می‌شود…";
                StartServer_Click(this, new RoutedEventArgs());
                var ready = false;
                for (var attempt = 0; attempt < 12; attempt++)
                {
                    await Task.Delay(500);
                    if (await IsStorefrontRunningAsync()) { ready = true; break; }
                }
                if (!ready)
                {
                    PublicHostingStatusText.Text = "سرویس وب محلی آماده نشد؛ گزارش وضعیت را بررسی کنید.";
                    return;
                }
            }

            var result = await _publicHostingManager.StartAsync(hostname);
            PublicHostingStatusText.Text = result.Message;
            if (!result.Success)
                return;

            await using (var db = new BusinessDbContext(CreateOptions()))
            {
                var settings = await db.StoreSettings.SingleAsync(x => x.Id == 1);
                settings.PublicPublicationEnabled = true;
                settings.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }

            await Task.Delay(2500);
            var verification = await _publicHostingManager.CheckPublicHttpsAsync(hostname);
            PublicHostingStatusText.Text = verification.Success
                ? "HTTPS عمومی از این رایانه تأیید شد. برای اطمینان نهایی، لینک را با اینترنت موبایل نیز آزمایش کنید."
                : $"سرویس شروع شده ولی دسترسی عمومی تأیید نشد. {verification.Message}";
        }
        catch (Exception ex)
        {
            PublicHostingStatusText.Text = $"راه‌اندازی انتشار HTTPS ناموفق بود: {ex.Message}";
        }
    }

    private async void CheckPublicHosting_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await using var db = new BusinessDbContext(CreateOptions());
            var settings = await db.StoreSettings.AsNoTracking().SingleAsync(x => x.Id == 1);
            if (string.IsNullOrWhiteSpace(settings.PublicHostname))
            {
                PublicHostingStatusText.Text = "دامنه‌ای ثبت نشده است.";
                return;
            }
            var result = await _publicHostingManager.CheckPublicHttpsAsync(settings.PublicHostname);
            PublicHostingStatusText.Text = result.Message;
        }
        catch (Exception ex)
        {
            PublicHostingStatusText.Text = $"بررسی HTTPS ناموفق بود: {ex.Message}";
        }
    }

    private async void StopPublicHosting_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await _publicHostingManager.StopAsync();
            PublicHostingStatusText.Text = result.Message;
            if (!result.Success)
                return;
            await using var db = new BusinessDbContext(CreateOptions());
            var settings = await db.StoreSettings.SingleAsync(x => x.Id == 1);
            settings.PublicPublicationEnabled = false;
            settings.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            PublicHostingStatusText.Text = $"توقف انتشار HTTPS ناموفق بود: {ex.Message}";
        }
    }

    private async Task<bool> IsStorefrontRunningAsync()
    {
        if (_storefrontProcess is { HasExited: false })
            return true;
        await TryAttachManagedProcessAsync();
        if (_storefrontProcess is { HasExited: false })
            return true;

        try
        {
            using var response = await HealthClient.GetAsync("http://127.0.0.1:8080/health/live");
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
    }

    private async Task TryAttachManagedProcessAsync()
    {
        if (_storefrontProcess is { HasExited: false } || !File.Exists(_storefrontPidPath))
            return;

        try
        {
            var text = await File.ReadAllTextAsync(_storefrontPidPath);
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
            {
                File.Delete(_storefrontPidPath);
                return;
            }

            var candidate = Process.GetProcessById(pid);
            var baseDirectory = AppContext.BaseDirectory;
            var expectedPaths = new[]
            {
                Path.Combine(baseDirectory, "storefront", "MG.BusinessPlatform.Storefront.exe"),
                Path.Combine(baseDirectory, "MG.BusinessPlatform.Storefront.exe")
            }.Where(File.Exists).Select(Path.GetFullPath).ToArray();
            var actualPath = candidate.MainModule?.FileName;
            if (actualPath is not null && expectedPaths.Contains(Path.GetFullPath(actualPath), StringComparer.OrdinalIgnoreCase))
                _storefrontProcess = candidate;
            else
                candidate.Dispose();
        }
        catch (ArgumentException)
        {
            if (File.Exists(_storefrontPidPath)) File.Delete(_storefrontPidPath);
        }
        catch (System.ComponentModel.Win32Exception) { }
        catch (InvalidOperationException) { }
    }

    private async void StartServer_Click(object sender, RoutedEventArgs e)
    {
        if (await IsStorefrontRunningAsync())
        {
            StatusText.Text = "سرویس وب از قبل در حال اجراست.";
            return;
        }

        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "storefront", "MG.BusinessPlatform.Storefront.exe"),
            Path.Combine(baseDirectory, "MG.BusinessPlatform.Storefront.exe")
        };
        var executable = candidates.FirstOrDefault(File.Exists);
        if (executable is null)
        {
            StatusText.Text = "فایل سرویس وب پیدا نشد. خروجی Storefront باید کنار برنامه در پوشه storefront قرار گیرد.";
            return;
        }

        try
        {
            var start = new ProcessStartInfo(executable)
            {
                WorkingDirectory = Path.GetDirectoryName(executable)!,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            start.Environment["MG_BUSINESS_DB_PATH"] = _databasePath;
            start.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:8080";
            _storefrontProcess = Process.Start(start);
            if (_storefrontProcess is not null)
                await File.WriteAllTextAsync(_storefrontPidPath, _storefrontProcess.Id.ToString(CultureInfo.InvariantCulture));
            StatusText.Text = _storefrontProcess is null ? "راه‌اندازی سرویس وب ناموفق بود." : "درخواست راه‌اندازی سرویس وب ارسال شد.";
            await Task.Delay(500);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"راه‌اندازی سرویس وب ناموفق بود: {ex.Message}";
        }
    }

    private async void StopServer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await TryAttachManagedProcessAsync();
            if (_storefrontProcess is not { HasExited: false })
            {
                if (await IsStorefrontRunningAsync())
                {
                    StatusText.Text = "سرویس وب فعال است اما مالکیت پردازش تأیید نشد؛ برای جلوگیری از بستن پردازش نامرتبط، متوقف نشد.";
                    return;
                }
            }
            else
            {
                _storefrontProcess.Kill(entireProcessTree: true);
                await _storefrontProcess.WaitForExitAsync();
            }
            _storefrontProcess?.Dispose();
            _storefrontProcess = null;
            if (File.Exists(_storefrontPidPath)) File.Delete(_storefrontPidPath);
            ServerStatusText.Text = "متوقف";
            StatusText.Text = "سرویس وب متوقف شد.";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"توقف سرویس وب ناموفق بود: {ex.Message}";
        }
    }
}
