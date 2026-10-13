using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.EntityFrameworkCore;
using MG.BusinessPlatform.Infrastructure.Persistence;
using MG.BusinessPlatform.Domain.Finance;
using MG.BusinessPlatform.Domain.GameCafe;
using MG.BusinessPlatform.Desktop.Services;

namespace MG.BusinessPlatform.Desktop;

public sealed record InvoiceListItem(Guid Id, string InvoiceNumber, string CustomerName, string CustomerPhone,
    DateOnly IssueDate, decimal TotalAmount, decimal PaidAmount, decimal OutstandingAmount, string CurrencyCode, string Status);

public sealed record TrialBalanceRow(string AccountCode, string AccountName, string AccountType,
    decimal DebitTurnover, decimal CreditTurnover, decimal DebitBalance, decimal CreditBalance);

public sealed record FinancialStatementRow(string Statement, string AccountCode, string AccountName,
    string AccountType, decimal Balance);

public sealed record GameSessionGridRow(Guid Id, string GameName, string StatusText, string StartedAtText,
    string PlayerNames, decimal ElapsedMinutes, decimal PerPlayerTimeCharge);

public sealed class GameSessionPlayerDraft : INotifyPropertyChanged
{
    private string _displayName = string.Empty;
    private decimal _entryAmount;
    private decimal _timeCharge;
    private decimal _additionalAmount;

    public string DisplayName { get => _displayName; set { if (_displayName == value) return; _displayName = value; OnChanged(); } }
    public decimal EntryAmount { get => _entryAmount; set { if (_entryAmount == value) return; _entryAmount = value; OnChanged(); OnChanged(nameof(FinalAmount)); } }
    public decimal TimeCharge { get => _timeCharge; set { if (_timeCharge == value) return; _timeCharge = value; OnChanged(); OnChanged(nameof(FinalAmount)); } }
    public decimal AdditionalAmount { get => _additionalAmount; set { if (_additionalAmount == value) return; _additionalAmount = value; OnChanged(); OnChanged(nameof(FinalAmount)); } }
    public decimal FinalAmount => EntryAmount + TimeCharge + AdditionalAmount;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public partial class MainWindow : Window
{
    private readonly string _databasePath;
    private readonly string _dataDirectory;
    private readonly LicenseService _licenseService = new();
    private MachineLicenseInfo? _machineLicenseInfo;
    private readonly DispatcherTimer _refreshTimer;
    private readonly string _storefrontPidPath;
    private readonly PublicHostingManager _publicHostingManager;
    private readonly ObservableCollection<GameSessionPlayerDraft> _sessionPlayerDrafts = [];
    private readonly Guid _sessionBranchId;
    private readonly Guid _sessionDeviceId;
    private Guid? _selectedGameSessionId;
    private Process? _storefrontProcess;
    private static readonly HttpClient HealthClient = new() { Timeout = TimeSpan.FromSeconds(1) };

    public MainWindow()
    {
        InitializeComponent();
        TrialBalanceDateInput.SelectedDate = DateTime.Today;
        FinancialToDateInput.SelectedDate = DateTime.Today;
        FinancialFromDateInput.SelectedDate = new DateTime(DateTime.Today.Year, 1, 1);
        var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MGBusinessPlatform");
        Directory.CreateDirectory(dataDirectory);
        _dataDirectory = dataDirectory;
        _sessionBranchId = LoadOrCreateIdentity(Path.Combine(dataDirectory, "branch-id.txt"));
        _sessionDeviceId = LoadOrCreateIdentity(Path.Combine(dataDirectory, "device-id.txt"));
        SessionPlayersGrid.ItemsSource = _sessionPlayerDrafts;
        _sessionPlayerDrafts.Add(new GameSessionPlayerDraft { DisplayName = "بازیکن ۱" });
        _databasePath = Path.Combine(dataDirectory, "mg-business-platform.db");
        _storefrontPidPath = Path.Combine(dataDirectory, "storefront.pid");
        _publicHostingManager = new PublicHostingManager(AppContext.BaseDirectory, dataDirectory);
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _refreshTimer.Tick += async (_, _) =>
        {
            RefreshLicenseStatus();
            await RefreshAsync();
        };
        Loaded += MainWindow_Loaded;
        Closed += (_, _) => _refreshTimer.Stop();
    }

    private static Guid LoadOrCreateIdentity(string path)
    {
        if (File.Exists(path) && Guid.TryParse(File.ReadAllText(path).Trim(), out var existing) && existing != Guid.Empty)
            return existing;

        var created = Guid.NewGuid();
        File.WriteAllText(path, created.ToString("D"));
        return created;
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
            await DatabaseInitializer.InitializeAsync(db);

            await LoadSettingsAsync();
            RefreshLicenseStatus();
            await RefreshAsync();
            _refreshTimer.Start();
            StatusText.Text = "سامانه آماده است. اطلاعات به‌صورت محلی ذخیره می‌شوند.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"خطا در آماده‌سازی پایگاه داده: {ex.Message}";
        }
    }

    private void RefreshLicenseStatus_Click(object sender, RoutedEventArgs e) => RefreshLicenseStatus();

    private void RefreshLicenseStatus()
    {
        try
        {
            var info = _licenseService.LoadOrCreate(_dataDirectory);
            _machineLicenseInfo = info;
            SoftwareSerialText.Text = info.SoftwareSerial;
            HardwareSerialText.Text = info.HardwareSerial;
            LicenseStatusText.Text = $"{LicenseStatusTitle(info.Validation.Status)} — {info.Validation.Message}";
            ApplyLicenseAccessPolicy(info);
        }
        catch (Exception ex)
        {
            _machineLicenseInfo = null;
            LicenseStatusText.Text = $"بررسی لایسنس ناموفق بود: {ex.Message}";
            ApplyLicenseAccessPolicy(null);
        }
    }

    private void ApplyLicenseAccessPolicy(MachineLicenseInfo? info)
    {
        var tabs = MainTabs.Items.OfType<TabItem>().ToArray();
        var settingsTab = tabs.FirstOrDefault(tab => string.Equals(tab.Header?.ToString(), "تنظیمات فروشگاه", StringComparison.Ordinal));
        if (settingsTab is null)
            return;

        var payload = info?.Validation.IsValid == true ? info.Validation.Payload : null;
        if (payload is null)
        {
            // Keep activation, serial display and diagnostics available; all business
            // modules stay disabled until a valid signed license is loaded.
            MainTabs.SelectedItem = settingsTab;
            foreach (var tab in tabs)
                tab.IsEnabled = ReferenceEquals(tab, settingsTab);
            return;
        }

        foreach (var tab in tabs)
        {
            var module = tab.Header?.ToString() switch
            {
                "نمای کلی" => "Core",
                "محصولات" or "گردش انبار" => "Inventory",
                "مشتریان" or "تأمین‌کنندگان" => "Core",
                "فروش حضوری (POS)" => "POS",
                "فاکتورهای فروش" or "تراز آزمایشی" or "صورت‌های مالی" => "Accounting",
                "سفارش‌های آنلاین" => "Storefront",
                "تنظیمات فروشگاه" => null,
                "جلسات بازی" => "GameCafe",
                _ => "Core"
            };
            tab.IsEnabled = module is null || payload.Modules.Contains(module, StringComparer.Ordinal);
        }

        if (MainTabs.SelectedItem is TabItem selected && !selected.IsEnabled)
            MainTabs.SelectedItem = tabs.FirstOrDefault(tab => tab.IsEnabled) ?? settingsTab;
    }

    private static string LicenseStatusTitle(MG.BusinessPlatform.Licensing.LicenseValidationStatus status) => status switch
    {
        MG.BusinessPlatform.Licensing.LicenseValidationStatus.Valid => "لایسنس معتبر",
        MG.BusinessPlatform.Licensing.LicenseValidationStatus.MissingLicense => "فعال‌سازی انجام نشده",
        MG.BusinessPlatform.Licensing.LicenseValidationStatus.MissingPublicKey => "کلید عمومی پیکربندی نشده",
        MG.BusinessPlatform.Licensing.LicenseValidationStatus.Expired => "لایسنس منقضی شده",
        MG.BusinessPlatform.Licensing.LicenseValidationStatus.WrongHardwareSerial => "سریال سخت‌افزار مطابقت ندارد",
        MG.BusinessPlatform.Licensing.LicenseValidationStatus.WrongSoftwareSerial => "سریال نرم‌افزار مطابقت ندارد",
        MG.BusinessPlatform.Licensing.LicenseValidationStatus.NotYetValid => "لایسنس هنوز فعال نشده",
        MG.BusinessPlatform.Licensing.LicenseValidationStatus.InvalidSignature => "امضای لایسنس نامعتبر است",
        _ => "لایسنس نامعتبر"
    };

    private void CopySoftwareSerial_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(SoftwareSerialText.Text))
            Clipboard.SetText(SoftwareSerialText.Text);
    }

    private void CopyHardwareSerial_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(HardwareSerialText.Text))
            Clipboard.SetText(HardwareSerialText.Text);
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
            var products = await db.StoreProducts.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
            ProductsGrid.ItemsSource = products;
            PosProductInput.ItemsSource = products.Where(x => x.IsAvailable && x.StockQuantity > 0).ToList();
            CustomersGrid.ItemsSource = await db.Customers.AsNoTracking()
                .OrderBy(x => x.DisplayName).ToListAsync();
            SuppliersGrid.ItemsSource = await db.Suppliers.AsNoTracking()
                .OrderBy(x => x.DisplayName).ToListAsync();
            StockMovementsGrid.ItemsSource = await (
                from movement in db.StockMovements.AsNoTracking()
                join product in db.StoreProducts.AsNoTracking() on movement.ProductId equals product.Id
                orderby movement.OccurredAt descending
                select new
                {
                    movement.OccurredAt,
                    ProductSku = product.Sku,
                    ProductName = product.Name,
                    movement.MovementType,
                    movement.SignedQuantity,
                    movement.UnitCost,
                    movement.CurrencyCode,
                    movement.Reference,
                    movement.Note
                }).Take(500).ToListAsync();
            var invoiceRows = await db.SalesInvoices.AsNoTracking()
                .OrderByDescending(x => x.IssueDate).ThenByDescending(x => x.CreatedAt).Take(500)
                .Select(x => new InvoiceListItem(x.Id, x.InvoiceNumber, x.CustomerName, x.CustomerPhone,
                    x.IssueDate, x.TotalAmount, x.PaidAmount, x.TotalAmount - x.PaidAmount, x.CurrencyCode, x.Status))
                .ToListAsync();
            InvoicesGrid.ItemsSource = invoiceRows;
            PosRecentInvoicesGrid.ItemsSource = invoiceRows.Take(100).ToList();
            OrdersGrid.ItemsSource = await db.CustomerOrders.AsNoTracking()
                .OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync();
            await RefreshGameSessionsAsync(db);
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

    private async Task RefreshGameSessionsAsync(BusinessDbContext db)
    {
        var sessions = await new GameSessionRepository(db).FindOpenAsync();
        var now = DateTimeOffset.UtcNow;
        var rows = sessions.Select(session => new GameSessionGridRow(
            session.Id,
            session.GameName,
            session.Status switch
            {
                GameSessionStatus.Active => "در حال اجرا",
                GameSessionStatus.Paused => "متوقف موقت",
                _ => "پایان‌یافته"
            },
            session.StartedAtUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.GetCultureInfo("fa-IR")),
            string.Join("، ", session.Players.Select(player => player.DisplayName)),
            decimal.Round((decimal)session.ElapsedAt(now).TotalMinutes, 1),
            session.CalculateTimeCharge(now, 15).Amount)).ToList();

        GameSessionsGrid.ItemsSource = rows;
        if (_selectedGameSessionId is Guid selectedId)
            GameSessionsGrid.SelectedItem = rows.FirstOrDefault(row => row.Id == selectedId);

        GameSessionStatusText.Text = rows.Count == 0
            ? "جلسه بازی فعالی ثبت نشده است."
            : $"تعداد جلسات فعال: {rows.Count} — هزینه زمان هر بازیکن بر اساس نرخ ساعتی و گردکردن ۱۵ دقیقه‌ای محاسبه می‌شود.";
    }

    private void AddSessionPlayer_Click(object sender, RoutedEventArgs e)
    {
        SessionPlayersGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        SessionPlayersGrid.CommitEdit(DataGridEditingUnit.Row, true);
        _sessionPlayerDrafts.Add(new GameSessionPlayerDraft
        {
            DisplayName = $"بازیکن {_sessionPlayerDrafts.Count + 1}"
        });
    }

    private async void StartGameSession_Click(object sender, RoutedEventArgs e)
    {
        SessionPlayersGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        SessionPlayersGrid.CommitEdit(DataGridEditingUnit.Row, true);

        var gameName = GameSessionNameInput.Text.Trim();
        if (gameName.Length is < 1 or > 160)
        {
            GameSessionStatusText.Text = "نام بازی را وارد کنید.";
            return;
        }

        if (!TryParseAmount(GameSessionRateInput.Text, out var hourlyRate) || hourlyRate < 0m)
        {
            GameSessionStatusText.Text = "نرخ ساعتی باید عددی معتبر و غیرمنفی باشد.";
            return;
        }

        var drafts = _sessionPlayerDrafts.Where(player => !string.IsNullOrWhiteSpace(player.DisplayName)).ToArray();
        if (drafts.Length == 0)
        {
            GameSessionStatusText.Text = "حداقل نام یک بازیکن را وارد کنید.";
            return;
        }

        try
        {
            var players = drafts.Select(player =>
            {
                if (player.EntryAmount < 0m || player.TimeCharge < 0m || player.AdditionalAmount < 0m)
                    throw new ArgumentOutOfRangeException(nameof(player), "مبالغ بازیکنان نمی‌توانند منفی باشند.");
                return new GameSessionPlayer(Guid.NewGuid(), player.DisplayName,
                    new Money(player.EntryAmount, "IRR"),
                    new Money(player.TimeCharge, "IRR"),
                    new Money(player.AdditionalAmount, "IRR"));
            }).ToArray();

            var session = new GameSession(Guid.NewGuid(), _sessionBranchId, _sessionDeviceId,
                gameName, DateTimeOffset.UtcNow, new Money(hourlyRate, "IRR"), players);

            await using var db = new BusinessDbContext(CreateOptions());
            await new GameSessionRepository(db).SaveAsync(session);
            _selectedGameSessionId = session.Id;
            _sessionPlayerDrafts.Clear();
            _sessionPlayerDrafts.Add(new GameSessionPlayerDraft { DisplayName = "بازیکن ۱" });
            GameSessionNameInput.Clear();
            await RefreshAsync();
            GameSessionStatusText.Text = $"جلسه «{gameName}» با {players.Length} بازیکن شروع شد.";
        }
        catch (Exception ex)
        {
            GameSessionStatusText.Text = $"شروع جلسه ناموفق بود: {ex.Message}";
        }
    }

    private async void PauseGameSession_Click(object sender, RoutedEventArgs e) =>
        await ChangeSelectedGameSessionAsync(GameSessionTransition.Pause);

    private async void ResumeGameSession_Click(object sender, RoutedEventArgs e) =>
        await ChangeSelectedGameSessionAsync(GameSessionTransition.Resume);

    private async void CompleteGameSession_Click(object sender, RoutedEventArgs e) =>
        await ChangeSelectedGameSessionAsync(GameSessionTransition.Complete);

    private async void RefreshGameSessions_Click(object sender, RoutedEventArgs e) =>
        await RefreshAsync();

    private async Task ChangeSelectedGameSessionAsync(GameSessionTransition transition)
    {
        if (GameSessionsGrid.SelectedItem is not GameSessionGridRow row)
        {
            GameSessionStatusText.Text = "ابتدا یک جلسه را از فهرست انتخاب کنید.";
            return;
        }

        _selectedGameSessionId = row.Id;
        try
        {
            await using var db = new BusinessDbContext(CreateOptions());
            var repository = new GameSessionRepository(db);
            var session = await repository.FindAsync(row.Id);
            if (session is null)
            {
                GameSessionStatusText.Text = "جلسه انتخاب‌شده پیدا نشد؛ فهرست را به‌روزرسانی کنید.";
                await RefreshAsync();
                return;
            }

            var now = DateTimeOffset.UtcNow;
            switch (transition)
            {
                case GameSessionTransition.Pause:
                    session.Pause(now);
                    break;
                case GameSessionTransition.Resume:
                    session.Resume(now);
                    break;
                case GameSessionTransition.Complete:
                    if (session.Status == GameSessionStatus.Paused)
                        session.Resume(now);
                    var finalTimeCharge = session.CalculateTimeCharge(now, 15);
                    foreach (var player in session.Players.ToArray())
                        session.UpdatePlayerCharges(player.Id, player.EntryAmount, finalTimeCharge, player.AdditionalAmount);
                    session.Complete(now);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(transition));
            }

            await repository.SaveAsync(session);
            if (transition == GameSessionTransition.Complete)
                _selectedGameSessionId = null;
            await RefreshAsync();
            GameSessionStatusText.Text = transition switch
            {
                GameSessionTransition.Pause => "جلسه متوقف موقت شد؛ زمان توقف محاسبه نمی‌شود.",
                GameSessionTransition.Resume => "جلسه ادامه یافت.",
                _ => "جلسه پایان یافت و مبلغ نهایی هر بازیکن ذخیره شد."
            };
        }
        catch (Exception ex)
        {
            GameSessionStatusText.Text = $"تغییر وضعیت جلسه ناموفق بود: {ex.Message}";
        }
    }

    private static bool TryParseAmount(string text, out decimal amount) =>
        decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out amount)
        || decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out amount);

    private enum GameSessionTransition
    {
        Pause,
        Resume,
        Complete
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
        if (!decimal.TryParse(UnitCostInput.Text.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out var unitCost)
            && !decimal.TryParse(UnitCostInput.Text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out unitCost))
        {
            StatusText.Text = "بهای واحد واردشده معتبر نیست.";
            return;
        }
        if (price < 0m || unitCost < 0m || !int.TryParse(StockInput.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var stock)
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
                UnitCost = unitCost,
                CurrencyCode = settings.CurrencyCode,
                IsAvailable = ProductAvailableInput.IsChecked == true,
                StockQuantity = stock,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            if (stock > 0)
            {
                db.StockMovements.Add(new StockMovementRecord
                {
                    Id = Guid.NewGuid(),
                    ProductId = db.StoreProducts.Local.Single().Id,
                    OccurredAt = DateTimeOffset.UtcNow,
                    MovementType = "OpeningBalance",
                    Quantity = stock,
                    SignedQuantity = stock,
                    UnitCost = unitCost,
                    CurrencyCode = settings.CurrencyCode,
                    Reference = $"OPENING-{sku.ToUpperInvariant()}",
                    Note = "Initial stock on product creation"
                });
            }
            await db.SaveChangesAsync();
            SkuInput.Clear();
            NameInput.Clear();
            PriceInput.Clear();
            UnitCostInput.Clear();
            StockInput.Clear();
            ProductAvailableInput.IsChecked = true;
            ProductsGrid.SelectedItem = null;
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

    private void ProductsGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ProductsGrid.SelectedItem is not StoreProduct product)
            return;
        SkuInput.Text = product.Sku;
        NameInput.Text = product.Name;
        PriceInput.Text = product.UnitPrice.ToString(CultureInfo.CurrentCulture);
        UnitCostInput.Text = product.UnitCost.ToString(CultureInfo.CurrentCulture);
        StockInput.Text = product.StockQuantity.ToString(CultureInfo.CurrentCulture);
        ProductAvailableInput.IsChecked = product.IsAvailable;
    }

    private async void UpdateSelectedProduct_Click(object sender, RoutedEventArgs e)
    {
        if (ProductsGrid.SelectedItem is not StoreProduct selected)
        {
            StatusText.Text = "برای ویرایش، ابتدا کالا را از فهرست انتخاب کنید.";
            return;
        }
        var sku = SkuInput.Text.Trim().ToUpperInvariant();
        var name = NameInput.Text.Trim();
        if (sku.Length is < 1 or > 80 || name.Length is < 1 or > 200)
        {
            StatusText.Text = "کد کالا و نام محصول معتبر نیست.";
            return;
        }
        if ((!decimal.TryParse(PriceInput.Text.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out var price)
            && !decimal.TryParse(PriceInput.Text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out price))
            || price < 0m)
        {
            StatusText.Text = "قیمت فروش معتبر نیست.";
            return;
        }
        if ((!decimal.TryParse(UnitCostInput.Text.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out var unitCost)
            && !decimal.TryParse(UnitCostInput.Text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out unitCost))
            || unitCost < 0m)
        {
            StatusText.Text = "بهای واحد معتبر نیست.";
            return;
        }

        try
        {
            await using var db = new BusinessDbContext(CreateOptions());
            var duplicateSku = await db.StoreProducts.AnyAsync(x => x.Id != selected.Id && x.Sku == sku);
            if (duplicateSku)
            {
                StatusText.Text = "این کد کالا قبلاً استفاده شده است.";
                return;
            }
            var product = await db.StoreProducts.SingleOrDefaultAsync(x => x.Id == selected.Id);
            if (product is null)
            {
                StatusText.Text = "کالا پیدا نشد؛ فهرست را تازه‌سازی کنید.";
                return;
            }

            product.Sku = sku;
            product.Name = name;
            product.UnitPrice = price;
            product.UnitCost = unitCost;
            product.IsAvailable = ProductAvailableInput.IsChecked == true;
            product.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            ProductsGrid.SelectedItem = null;
            StatusText.Text = "تغییرات کالا ذخیره شد؛ سوابق گردش انبار بدون تغییر باقی ماندند.";
            await RefreshAsync();
        }
        catch (DbUpdateException)
        {
            StatusText.Text = "ذخیره تغییرات ناموفق بود؛ کد کالا ممکن است تکراری باشد.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"ذخیره تغییرات کالا ناموفق بود: {ex.Message}";
        }
    }

    private async void AdjustStock_Click(object sender, RoutedEventArgs e)
    {
        if (ProductsGrid.SelectedItem is not StoreProduct selected)
        {
            StatusText.Text = "ابتدا کالا را از فهرست انتخاب کنید.";
            return;
        }
        var rawDelta = StockAdjustmentInput.Text.Trim();
        var note = StockAdjustmentNoteInput.Text.Trim();
        if ((!int.TryParse(rawDelta, NumberStyles.Integer, CultureInfo.CurrentCulture, out var delta)
            && !int.TryParse(rawDelta, NumberStyles.Integer, CultureInfo.InvariantCulture, out delta))
            || delta == 0 || delta == int.MinValue)
        {
            StatusText.Text = "مقدار اصلاح باید یک عدد صحیح غیرصفر باشد.";
            return;
        }
        if (note.Length is < 1 or > 500)
        {
            StatusText.Text = "علت اصلاح موجودی را وارد کنید (حداکثر ۵۰۰ نویسه).";
            return;
        }

        try
        {
            await using var db = new BusinessDbContext(CreateOptions());
            await using var transaction = await db.Database.BeginTransactionAsync();
            var product = await db.StoreProducts.SingleOrDefaultAsync(x => x.Id == selected.Id);
            if (product is null)
            {
                StatusText.Text = "کالا پیدا نشد؛ فهرست را تازه‌سازی کنید.";
                return;
            }
            var updatedStock = checked(product.StockQuantity + delta);
            if (updatedStock < 0)
            {
                StatusText.Text = "اصلاح موجودی نمی‌تواند موجودی کالا را منفی کند.";
                return;
            }

            var updatedAt = DateTimeOffset.UtcNow;
            var affected = await db.StoreProducts
                .Where(x => x.Id == product.Id && x.StockQuantity == product.StockQuantity
                    && x.UnitCost == product.UnitCost && x.CurrencyCode == product.CurrencyCode)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.StockQuantity, updatedStock)
                    .SetProperty(x => x.UpdatedAt, updatedAt));
            if (affected != 1)
                throw new InvalidOperationException("موجودی یا بهای کالا هم‌زمان تغییر کرده است؛ فهرست را تازه‌سازی کنید.");

            db.StockMovements.Add(new StockMovementRecord
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                OccurredAt = DateTimeOffset.UtcNow,
                MovementType = delta > 0 ? "ManualPositiveAdjustment" : "ManualNegativeAdjustment",
                Quantity = Math.Abs(delta),
                SignedQuantity = delta,
                UnitCost = product.UnitCost,
                CurrencyCode = product.CurrencyCode,
                Reference = "MANUAL",
                Note = note
            });
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            StockAdjustmentInput.Clear();
            StockAdjustmentNoteInput.Clear();
            StatusText.Text = "اصلاح موجودی ثبت و در دفتر گردش انبار ذخیره شد.";
            await RefreshAsync();
        }
        catch (OverflowException)
        {
            StatusText.Text = "عدد واردشده خارج از محدوده قابل پشتیبانی است.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"اصلاح موجودی ناموفق بود: {ex.Message}";
        }
    }

    private void PosProductInput_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (PosProductInput.SelectedItem is StoreProduct product)
            PosProductInfoText.Text = $"قیمت: {product.UnitPrice:N2} {product.CurrencyCode} | موجودی: {product.StockQuantity}";
        else
            PosProductInfoText.Text = "کالا و تعداد را انتخاب کنید.";
    }

    private async void CompletePosSale_Click(object sender, RoutedEventArgs e)
    {
        if (PosProductInput.SelectedItem is not StoreProduct product)
        {
            StatusText.Text = "ابتدا کالای فروش حضوری را انتخاب کنید.";
            return;
        }
        var rawQuantity = PosQuantityInput.Text.Trim();
        if ((!int.TryParse(rawQuantity, NumberStyles.Integer, CultureInfo.CurrentCulture, out var quantity)
            && !int.TryParse(rawQuantity, NumberStyles.Integer, CultureInfo.InvariantCulture, out quantity))
            || quantity < 1 || quantity > 10000)
        {
            StatusText.Text = "تعداد باید عدد صحیح بین ۱ تا ۱۰۰۰۰ باشد.";
            return;
        }
        if (PosPaymentMethodInput.SelectedItem is not ComboBoxItem methodItem || methodItem.Tag is not string method)
        {
            StatusText.Text = "روش دریافت وجه را انتخاب کنید.";
            return;
        }

        var customerName = string.IsNullOrWhiteSpace(PosCustomerNameInput.Text)
            ? "مشتری حضوری" : PosCustomerNameInput.Text.Trim();
        var customerPhone = PosCustomerPhoneInput.Text.Trim();
        if (customerName.Length > 160 || customerPhone.Length > 40)
        {
            StatusText.Text = "طول نام مشتری یا شماره تماس بیش از حد مجاز است.";
            return;
        }

        decimal estimatedTotal;
        try { estimatedTotal = checked(product.UnitPrice * quantity); }
        catch (OverflowException)
        {
            StatusText.Text = "مبلغ فروش خارج از محدوده قابل پشتیبانی است.";
            return;
        }

        if (!ConfirmationDialog.Confirm(this, "ثبت فروش حضوری",
            $"فروش {quantity} عدد از «{product.Name}» با مبلغ {estimatedTotal:N2} {product.CurrencyCode} ثبت می‌شود. موجودی کم، فاکتور صادر و دریافت وجه در حسابداری ثبت خواهد شد.",
            "ثبت فروش"))
            return;

        try
        {
            await using var db = new BusinessDbContext(CreateOptions());
            var accounting = new SalesAccountingService(db);
            var invoice = await accounting.CreatePosSaleAsync(product.Id, quantity, customerName, customerPhone,
                method, string.Empty, "POS checkout", DateOnly.FromDateTime(DateTime.Today));
            StatusText.Text = $"فروش حضوری ثبت شد: فاکتور {invoice.InvoiceNumber}، مبلغ {invoice.TotalAmount:N2} {invoice.CurrencyCode}، وضعیت {invoice.Status}.";
            await RefreshAsync();
            await RefreshTrialBalanceAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"ثبت فروش حضوری ناموفق بود: {ex.Message}";
        }
    }

    private async void RecordPayment_Click(object sender, RoutedEventArgs e)
    {
        if (InvoicesGrid.SelectedItem is not InvoiceListItem selected)
        {
            StatusText.Text = "ابتدا یک فاکتور را انتخاب کنید.";
            return;
        }
        var rawAmount = PaymentAmountInput.Text.Trim();
        if ((!decimal.TryParse(rawAmount, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount)
            && !decimal.TryParse(rawAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out amount))
            || amount <= 0m)
        {
            StatusText.Text = "مبلغ دریافت باید عددی معتبر و بزرگ‌تر از صفر باشد.";
            return;
        }
        if (PaymentMethodInput.SelectedItem is not ComboBoxItem methodItem || methodItem.Tag is not string method)
        {
            StatusText.Text = "روش دریافت وجه را انتخاب کنید.";
            return;
        }

        var reference = PaymentReferenceInput.Text.Trim();
        var notes = PaymentNotesInput.Text.Trim();
        if (reference.Length > 100 || notes.Length > 500)
        {
            StatusText.Text = "طول شماره پیگیری یا توضیحات بیش از حد مجاز است.";
            return;
        }

        try
        {
            await using var db = new BusinessDbContext(CreateOptions());
            var accounting = new SalesAccountingService(db);
            await accounting.RecordPaymentAsync(selected.Id, amount, method, reference, notes,
                DateTimeOffset.UtcNow, DateOnly.FromDateTime(DateTime.Today));
            PaymentAmountInput.Clear();
            PaymentReferenceInput.Clear();
            PaymentNotesInput.Clear();
            StatusText.Text = $"دریافت {amount:N2} {selected.CurrencyCode} ثبت شد و سند دوبل آن ایجاد شد.";
            await RefreshAsync();
            await RefreshTrialBalanceAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"ثبت دریافت وجه ناموفق بود: {ex.Message}";
        }
    }

    private async void CalculateFinancialStatements_Click(object sender, RoutedEventArgs e) =>
        await CalculateFinancialStatementsAsync();

    private async Task CalculateFinancialStatementsAsync()
    {
        try
        {
            var fromDate = DateOnly.FromDateTime(FinancialFromDateInput.SelectedDate ?? new DateTime(DateTime.Today.Year, 1, 1));
            var toDate = DateOnly.FromDateTime(FinancialToDateInput.SelectedDate ?? DateTime.Today);
            if (fromDate > toDate)
            {
                FinancialStatementTotalsText.Text = "تاریخ شروع نمی‌تواند بعد از تاریخ پایان باشد.";
                return;
            }

            await using var db = new BusinessDbContext(CreateOptions());
            var settings = await db.StoreSettings.AsNoTracking().SingleAsync(x => x.Id == 1);
            var ledgerAccounts = await db.LedgerAccounts.AsNoTracking().ToListAsync();
            var journalRecords = await db.JournalEntries.AsNoTracking()
                .Include(x => x.Lines)
                .Where(x => x.EntryDate <= toDate && x.CurrencyCode == settings.CurrencyCode)
                .ToListAsync();

            var accounts = ledgerAccounts.Select(account => new MG.BusinessPlatform.Domain.Finance.Account(
                account.Code, account.Name,
                Enum.Parse<MG.BusinessPlatform.Domain.Finance.AccountType>(account.AccountType, ignoreCase: false),
                isPostingAllowed: account.IsPostingAllowed, isActive: account.IsActive)).ToArray();

            var entries = journalRecords.Select(entry => new MG.BusinessPlatform.Domain.Finance.JournalEntry(
                entry.Id, entry.EntryDate, entry.Description,
                entry.Lines.Select(line => new MG.BusinessPlatform.Domain.Finance.JournalLine(
                    line.AccountCode, line.AccountName,
                    Enum.Parse<MG.BusinessPlatform.Domain.Finance.EntrySide>(line.Side, ignoreCase: false),
                    new MG.BusinessPlatform.Domain.Finance.Money(line.Amount, entry.CurrencyCode))))).ToArray();

            var profitLoss = MG.BusinessPlatform.Domain.Finance.FinancialStatementCalculator.CalculateProfitAndLoss(
                accounts, entries, fromDate, toDate, settings.CurrencyCode);
            var balanceSheet = MG.BusinessPlatform.Domain.Finance.FinancialStatementCalculator.CalculateBalanceSheet(
                accounts, entries, toDate, settings.CurrencyCode);

            var rows = new List<FinancialStatementRow>();
            rows.AddRange(profitLoss.RevenueLines.Select(line =>
                new FinancialStatementRow("سود و زیان", line.AccountCode, line.AccountName, "درآمد", line.Balance.Amount)));
            rows.AddRange(profitLoss.ExpenseLines.Select(line =>
                new FinancialStatementRow("سود و زیان", line.AccountCode, line.AccountName, "هزینه", line.Balance.Amount)));
            rows.AddRange(balanceSheet.AssetLines.Select(line =>
                new FinancialStatementRow("ترازنامه", line.AccountCode, line.AccountName, "دارایی", line.Balance.Amount)));
            rows.AddRange(balanceSheet.LiabilityLines.Select(line =>
                new FinancialStatementRow("ترازنامه", line.AccountCode, line.AccountName, "بدهی", line.Balance.Amount)));
            rows.AddRange(balanceSheet.EquityLines.Select(line =>
                new FinancialStatementRow("ترازنامه", line.AccountCode, line.AccountName, "حقوق مالکانه", line.Balance.Amount)));
            rows.Add(new FinancialStatementRow("ترازنامه", "EQ-CURRENT", "سود/زیان انباشته تا تاریخ گزارش",
                "حقوق مالکانه", balanceSheet.AccumulatedEarnings.Amount));
            FinancialStatementsGrid.ItemsSource = rows;

            FinancialStatementTotalsText.Text =
                $"سود و زیان ({fromDate:yyyy-MM-dd} تا {toDate:yyyy-MM-dd}) | درآمد: {profitLoss.TotalRevenue.Amount:N2} | هزینه: {profitLoss.TotalExpenses.Amount:N2} | سود/زیان خالص: {profitLoss.NetProfit.Amount:N2} {settings.CurrencyCode}\n" +
                $"ترازنامه تا {toDate:yyyy-MM-dd} | دارایی: {balanceSheet.TotalAssets.Amount:N2} | بدهی: {balanceSheet.TotalLiabilities.Amount:N2} | حقوق مالکانه: {balanceSheet.TotalEquity.Amount:N2} | سود/زیان انباشته: {balanceSheet.AccumulatedEarnings.Amount:N2} | " +
                (balanceSheet.IsBalanced ? "ترازنامه متوازن است." : "هشدار: ترازنامه نامتوازن است.");
        }
        catch (Exception ex)
        {
            FinancialStatementTotalsText.Text = $"محاسبه صورت‌های مالی ناموفق بود: {ex.Message}";
        }
    }

    private async void RefreshTrialBalance_Click(object sender, RoutedEventArgs e) =>
        await RefreshTrialBalanceAsync();

    private async Task RefreshTrialBalanceAsync()
    {
        try
        {
            var asOfDate = DateOnly.FromDateTime(TrialBalanceDateInput.SelectedDate ?? DateTime.Today);
            await using var db = new BusinessDbContext(CreateOptions());
            var settings = await db.StoreSettings.AsNoTracking().SingleAsync(x => x.Id == 1);
            var accounts = await db.LedgerAccounts.AsNoTracking().OrderBy(x => x.Code).ToListAsync();
            var entryIds = await db.JournalEntries.AsNoTracking()
                .Where(entry => entry.EntryDate <= asOfDate && entry.CurrencyCode == settings.CurrencyCode)
                .Select(entry => entry.Id).ToListAsync();
            var journalLines = await db.JournalLines.AsNoTracking()
                .Where(line => entryIds.Contains(line.JournalEntryId)).ToListAsync();

            var rows = accounts.Select(account =>
            {
                var lines = journalLines.Where(line => line.AccountId == account.Id).ToArray();
                var debit = lines.Where(line => line.Side == "Debit").Aggregate(0m, (sum, line) => checked(sum + line.Amount));
                var credit = lines.Where(line => line.Side == "Credit").Aggregate(0m, (sum, line) => checked(sum + line.Amount));
                var net = checked(debit - credit);
                return new TrialBalanceRow(account.Code, account.Name, account.AccountType, debit, credit,
                    net > 0m ? net : 0m, net < 0m ? -net : 0m);
            }).ToList();

            TrialBalanceGrid.ItemsSource = rows;
            var debitTurnover = rows.Aggregate(0m, (sum, row) => checked(sum + row.DebitTurnover));
            var creditTurnover = rows.Aggregate(0m, (sum, row) => checked(sum + row.CreditTurnover));
            var debitBalance = rows.Aggregate(0m, (sum, row) => checked(sum + row.DebitBalance));
            var creditBalance = rows.Aggregate(0m, (sum, row) => checked(sum + row.CreditBalance));
            TrialBalanceTotalsText.Text = debitTurnover == creditTurnover && debitBalance == creditBalance
                ? $"تراز متوازن | گردش بدهکار: {debitTurnover:N2} | گردش بستانکار: {creditTurnover:N2} | مانده بدهکار/بستانکار: {debitBalance:N2}/{creditBalance:N2} {settings.CurrencyCode}"
                : $"هشدار: تراز نامتوازن | بدهکار: {debitTurnover:N2} | بستانکار: {creditTurnover:N2} {settings.CurrencyCode}";
        }
        catch (Exception ex)
        {
            TrialBalanceTotalsText.Text = $"محاسبه تراز آزمایشی ناموفق بود: {ex.Message}";
        }
    }

    private async void AddCustomer_Click(object sender, RoutedEventArgs e)
    {
        var name = CustomerNameInput.Text.Trim();
        var phone = CustomerPhoneInput.Text.Trim();
        var email = CustomerEmailInput.Text.Trim();
        var address = CustomerAddressInput.Text.Trim();
        if (name.Length is < 1 or > 200 || phone.Length > 40 || email.Length > 254 || address.Length > 1000)
        {
            StatusText.Text = "نام مشتری الزامی است؛ طول شماره تماس، ایمیل یا نشانی بیش از حد مجاز است.";
            return;
        }
        if (email.Length > 0 && !System.Net.Mail.MailAddress.TryCreate(email, out _))
        {
            StatusText.Text = "نشانی ایمیل معتبر نیست.";
            return;
        }

        try
        {
            await using var db = new BusinessDbContext(CreateOptions());
            db.Customers.Add(new CustomerRecord
            {
                Id = Guid.NewGuid(),
                DisplayName = name,
                Phone = phone,
                Email = email.ToLowerInvariant(),
                Address = address,
                CreatedAt = DateTimeOffset.UtcNow,
                IsActive = true
            });
            await db.SaveChangesAsync();
            CustomerNameInput.Clear();
            CustomerPhoneInput.Clear();
            CustomerEmailInput.Clear();
            CustomerAddressInput.Clear();
            StatusText.Text = "مشتری ذخیره شد.";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"ذخیره مشتری ناموفق بود: {ex.Message}";
        }
    }

    private async void AddSupplier_Click(object sender, RoutedEventArgs e)
    {
        var name = SupplierNameInput.Text.Trim();
        var phone = SupplierPhoneInput.Text.Trim();
        var email = SupplierEmailInput.Text.Trim();
        var taxIdentifier = SupplierTaxInput.Text.Trim();
        var paymentTerms = SupplierTermsInput.Text.Trim();
        if (name.Length is < 1 or > 200 || phone.Length > 40 || email.Length > 254
            || taxIdentifier.Length > 80 || paymentTerms.Length > 500)
        {
            StatusText.Text = "نام تأمین‌کننده الزامی است؛ طول یکی از فیلدها بیش از حد مجاز است.";
            return;
        }
        if (email.Length > 0 && !System.Net.Mail.MailAddress.TryCreate(email, out _))
        {
            StatusText.Text = "نشانی ایمیل تأمین‌کننده معتبر نیست.";
            return;
        }

        try
        {
            await using var db = new BusinessDbContext(CreateOptions());
            db.Suppliers.Add(new SupplierRecord
            {
                Id = Guid.NewGuid(),
                DisplayName = name,
                Phone = phone,
                Email = email.ToLowerInvariant(),
                Address = string.Empty,
                TaxIdentifier = taxIdentifier,
                PaymentTerms = paymentTerms,
                CreatedAt = DateTimeOffset.UtcNow,
                IsActive = true
            });
            await db.SaveChangesAsync();
            SupplierNameInput.Clear();
            SupplierPhoneInput.Clear();
            SupplierEmailInput.Clear();
            SupplierTaxInput.Clear();
            SupplierTermsInput.Clear();
            StatusText.Text = "تأمین‌کننده ذخیره شد.";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"ذخیره تأمین‌کننده ناموفق بود: {ex.Message}";
        }
    }

    private async void FulfillOrder_Click(object sender, RoutedEventArgs e)
    {
        if (OrdersGrid.SelectedItem is not CustomerOrder selected)
        {
            StatusText.Text = "ابتدا یک سفارش را انتخاب کنید.";
            return;
        }
        if (!ConfirmationDialog.Confirm(this, "ثبت تحویل و صدور فاکتور",
            "با تأیید، فاکتور فروش صادر می‌شود و اسناد دوبل درآمد و بهای تمام‌شده به‌صورت تراکنشی ثبت می‌شوند. دریافت وجه در مرحله جداگانه انجام می‌شود.",
            "صدور فاکتور و تحویل"))
            return;

        try
        {
            await using var db = new BusinessDbContext(CreateOptions());
            var accounting = new SalesAccountingService(db);
            var invoice = await accounting.FulfillOrderAsync(selected.Id, DateOnly.FromDateTime(DateTime.Today));
            StatusText.Text = $"تحویل ثبت شد؛ فاکتور {invoice.InvoiceNumber} و اسناد حسابداری صادر شدند. دریافت وجه هنوز ثبت نشده است.";
            await RefreshAsync();
            await RefreshTrialBalanceAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"ثبت تحویل و صدور فاکتور ناموفق بود: {ex.Message}";
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
                var restoredAt = DateTimeOffset.UtcNow;
                var affected = await db.StoreProducts
                    .Where(x => x.Id == product.Id && x.StockQuantity <= int.MaxValue - line.Quantity)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.StockQuantity, x => x.StockQuantity + line.Quantity)
                        .SetProperty(x => x.UpdatedAt, restoredAt));
                if (affected != 1)
                {
                    StatusText.Text = $"موجودی کالای «{line.ProductName}» هم‌زمان تغییر کرده یا از محدوده مجاز خارج می‌شود؛ لغو انجام نشد.";
                    return;
                }
                db.StockMovements.Add(new StockMovementRecord
                {
                    Id = Guid.NewGuid(),
                    ProductId = product.Id,
                    OccurredAt = DateTimeOffset.UtcNow,
                    MovementType = "WebOrderCancellation",
                    Quantity = line.Quantity,
                    SignedQuantity = line.Quantity,
                    UnitCost = line.UnitCost,
                    CurrencyCode = order.CurrencyCode,
                    Reference = order.OrderNumber,
                    Note = "Stock restored after order cancellation"
                });
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

    private async void CreateBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "ذخیره پشتیبان پایگاه داده",
            Filter = "SQLite backup (*.db)|*.db|All files (*.*)|*.*",
            DefaultExt = ".db",
            AddExtension = true,
            FileName = $"MG-Business-Platform-{DateTime.Now:yyyyMMdd-HHmmss}.db"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var service = new DatabaseMaintenanceService();
            var result = await service.CreateBackupAsync(_databasePath, dialog.FileName);
            StatusText.Text = $"پشتیبان ایجاد و بررسی شد: {result.Path} ({result.SizeBytes:N0} بایت).";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"ایجاد پشتیبان ناموفق بود: {ex.Message}";
        }
    }

    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "انتخاب فایل پشتیبان",
            Filter = "SQLite backup (*.db;*.bak)|*.db;*.bak|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;
        if (!ConfirmationDialog.Confirm(this, "بازیابی پایگاه داده",
            "این کار داده‌های فعلی را با نسخه پشتیبان جایگزین می‌کند. قبل از جایگزینی یک نسخه ایمنی از پایگاه فعلی ساخته می‌شود. ادامه می‌دهید؟",
            "بازیابی و جایگزینی"))
            return;

        _refreshTimer.Stop();
        try
        {
            var caddyStop = await _publicHostingManager.StopAsync();
            if (!caddyStop.Success)
            {
                StatusText.Text = caddyStop.Message;
                return;
            }

            await TryAttachManagedProcessAsync();
            if (_storefrontProcess is { HasExited: false })
            {
                _storefrontProcess.Kill(entireProcessTree: true);
                await _storefrontProcess.WaitForExitAsync();
                _storefrontProcess.Dispose();
                _storefrontProcess = null;
                if (File.Exists(_storefrontPidPath)) File.Delete(_storefrontPidPath);
            }
            else if (await IsStorefrontRunningAsync())
            {
                StatusText.Text = "سرویس وب توسط برنامه دیگری اجرا شده است؛ برای جلوگیری از آسیب به داده‌ها، بازیابی لغو شد.";
                return;
            }

            var service = new DatabaseMaintenanceService();
            var result = await service.RestoreBackupAsync(dialog.FileName, _databasePath);
            StatusText.Text = $"پایگاه داده بازیابی شد. نسخه ایمنی قبلی نیز در همان پوشه نگهداری می‌شود. فایل: {result.Path}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"بازیابی ناموفق بود؛ نسخه فعلی تا جای ممکن حفظ شده است: {ex.Message}";
        }
        finally
        {
            try
            {
                await LoadSettingsAsync();
                if (HasLicensedModule("Storefront") && OnlineOrdersInput.IsChecked == true && !await IsStorefrontRunningAsync())
                {
                    StartServer_Click(this, new RoutedEventArgs());
                    for (var attempt = 0; attempt < 10 && !await IsStorefrontRunningAsync(); attempt++)
                        await Task.Delay(500);
                }

                await using var db = new BusinessDbContext(CreateOptions());
                var settings = await db.StoreSettings.AsNoTracking().SingleAsync(x => x.Id == 1);
                if (HasLicensedModule("Storefront") && settings.PublicPublicationEnabled && !string.IsNullOrWhiteSpace(settings.PublicHostname))
                    await _publicHostingManager.StartAsync(settings.PublicHostname);
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                StatusText.Text += $" راه‌اندازی مجدد سرویس‌ها پس از بازیابی ناموفق بود: {ex.Message}";
            }
            _refreshTimer.Start();
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

    private async void DiagnosePublicHosting_Click(object sender, RoutedEventArgs e)
    {
        if (!HasLicensedModule("Storefront"))
        {
            PublicHostingStatusText.Text = "برای عیب‌یابی انتشار فروشگاه آنلاین، لایسنس معتبر دارای ماژول Storefront لازم است.";
            return;
        }

        try
        {
            var hostname = HostnameInput.Text.Trim();
            var result = await _publicHostingManager.DiagnoseAsync(hostname);
            PublicHostingStatusText.Text = result.Message;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Net.Sockets.SocketException)
        {
            PublicHostingStatusText.Text = $"عیب‌یابی دامنه ناموفق بود: {ex.Message}";
        }
    }

    private async void StartPublicHosting_Click(object sender, RoutedEventArgs e)
    {
        if (!HasLicensedModule("Storefront"))
        {
            PublicHostingStatusText.Text = "برای انتشار فروشگاه آنلاین، لایسنس معتبر دارای ماژول Storefront لازم است.";
            return;
        }

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

    private bool HasLicensedModule(string module)
    {
        var payload = _machineLicenseInfo?.Validation is { IsValid: true, Payload: { } validPayload }
            ? validPayload
            : null;
        return payload?.Modules.Contains(module, StringComparer.Ordinal) == true;
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
        if (!HasLicensedModule("Storefront"))
        {
            StatusText.Text = "برای راه‌اندازی فروشگاه آنلاین، لایسنس معتبر دارای ماژول Storefront لازم است.";
            return;
        }
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
