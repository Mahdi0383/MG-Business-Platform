using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.EntityFrameworkCore;
using MG.BusinessPlatform.Infrastructure.Persistence;
using MG.BusinessPlatform.Infrastructure.Services;
using MG.BusinessPlatform.Domain.Finance;
using MG.BusinessPlatform.Domain.GameCafe;
using MG.BusinessPlatform.Licensing;
using MG.BusinessPlatform.Desktop.Services;

namespace MG.BusinessPlatform.Desktop;

public sealed record InvoiceListItem(Guid Id, string InvoiceNumber, string CustomerName, string CustomerPhone,
    DateOnly IssueDate, DateOnly? DueDate, decimal TotalAmount, decimal PaidAmount, decimal OutstandingAmount, string CurrencyCode, string Status);

public sealed record PosCartLineDraft(Guid ProductId, string ProductName, int Quantity, decimal UnitPrice, string CurrencyCode)
{
    public decimal LineTotal => checked(UnitPrice * Quantity);
}

public sealed record PurchaseInvoiceDraftLine(Guid ProductId, string ProductName, int Quantity,
    decimal UnitCost, decimal DiscountPercent, string UnitCode)
{
    public decimal LineTotal => checked(UnitCost * Quantity * (100m - DiscountPercent) / 100m);
}

public sealed record PurchaseInvoiceGridRow(Guid Id, string InvoiceNumber, string SupplierName,
    DateOnly IssueDate, DateOnly? DueDate, decimal TotalAmount, decimal PaidAmount, decimal OutstandingAmount,
    string CurrencyCode, string Status);

public sealed record TrialBalanceRow(string AccountCode, string AccountName, string AccountType,
    decimal DebitTurnover, decimal CreditTurnover, decimal DebitBalance, decimal CreditBalance);

public sealed record FinancialStatementRow(string Statement, string AccountCode, string AccountName,
    string AccountType, decimal Balance);

public sealed record CashMovementGridRow(string AccountCode, string AccountName, decimal OpeningBalance,
    decimal Inflows, decimal Outflows, decimal ClosingBalance);

public sealed record AuditEventGridRow(DateTimeOffset OccurredAtUtc, string Actor, string MachineName,
    string Action, string EntityType, string EntityId, string ChangedFields);

public sealed record InvoiceAgingGridRow(string DocumentType, string DocumentNumber, string CounterpartyName,
    DateOnly IssueDate, DateOnly? DueDate, DateOnly AgingDate, string AgingBasisText, int AgeDays,
    string BucketText, decimal TotalAmount, decimal PaidAmount, decimal OutstandingAmount);

public sealed record GeneralLedgerGridRow(string EntryDate, string EntryNumber, string Description,
    decimal Debit, decimal Credit, decimal RunningBalance);

public sealed record ReservationListRow(Guid Id, string ReservationNumber, string ResourceName, string GuestName,
    string GuestPhone, int GuestCount, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, string Status);

public sealed record GameSessionGridRow(Guid Id, string GameName, string StatusText, string StartedAtText,
    string PlayerNames, decimal ElapsedMinutes, decimal PerPlayerTimeCharge);

public sealed record PaymentTerminalProfileGridRow(Guid Id, string Name, string Vendor, string Model,
    string ConnectionTypeText, bool IsEnabled, bool IsDefault, string LastTestStatus);

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
    private readonly ParsPackDnsService _parsPackDnsService = new();
    private const string ParsPackDnsCredentialTarget = "MG.BusinessPlatform.ParsPackDnsApiToken";
    private PaymentTerminalProfileService _paymentTerminalProfileService = null!;
    private Guid? _editingPaymentProfileId;
    private MachineLicenseInfo? _machineLicenseInfo;
    private readonly DispatcherTimer _refreshTimer;
    private readonly string _storefrontPidPath;
    private readonly PublicHostingManager _publicHostingManager;
    private readonly RouterPortMappingService _routerPortMappingService;
    private readonly ObservableCollection<GameSessionPlayerDraft> _sessionPlayerDrafts = [];
    private readonly ObservableCollection<PosCartLineDraft> _posCartLines = [];
    private bool _isPosCheckoutInProgress;
    private bool _isPurchaseInvoiceInProgress;
    private bool _isSalesPaymentInProgress;
    private bool _isSupplierPaymentInProgress;
    private bool _isLoadingProductAddons;
    private readonly ObservableCollection<PurchaseInvoiceDraftLine> _purchaseDraftLines = [];
    private readonly Guid _sessionBranchId;
    private readonly Guid _sessionDeviceId;
    private Guid? _selectedGameSessionId;
    private Process? _storefrontProcess;
    private static readonly HttpClient HealthClient = new() { Timeout = TimeSpan.FromSeconds(1) };

    public MainWindow()
    {
        InitializeComponent();
        TrialBalanceDateInput.SelectedDate = DateTime.Today;
        GeneralLedgerFromDateInput.SelectedDate = new DateTime(DateTime.Today.Year, 1, 1);
        GeneralLedgerToDateInput.SelectedDate = DateTime.Today;
        FinancialToDateInput.SelectedDate = DateTime.Today;
        FinancialFromDateInput.SelectedDate = new DateTime(DateTime.Today.Year, 1, 1);
        CashMovementToDateInput.SelectedDate = DateTime.Today;
        CashMovementFromDateInput.SelectedDate = new DateTime(DateTime.Today.Year, 1, 1);
        InvoiceAgingAsOfDateInput.SelectedDate = DateTime.Today;
        AuditFromDateInput.SelectedDate = new DateTime(DateTime.Today.Year, 1, 1);
        AuditToDateInput.SelectedDate = DateTime.Today;
        var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MGBusinessPlatform");
        Directory.CreateDirectory(dataDirectory);
        _dataDirectory = dataDirectory;
        _paymentTerminalProfileService = new PaymentTerminalProfileService(Path.Combine(dataDirectory, "payment-terminal-profiles.json"));
        _sessionBranchId = LoadOrCreateIdentity(Path.Combine(dataDirectory, "branch-id.txt"));
        _sessionDeviceId = LoadOrCreateIdentity(Path.Combine(dataDirectory, "device-id.txt"));
        SessionPlayersGrid.ItemsSource = _sessionPlayerDrafts;
        PosCartGrid.ItemsSource = _posCartLines;
        PurchaseDraftGrid.ItemsSource = _purchaseDraftLines;
        PurchaseInvoiceDateInput.SelectedDate = DateTime.Today;
        _sessionPlayerDrafts.Add(new GameSessionPlayerDraft { DisplayName = "بازیکن ۱" });
        UpdatePosCartTotal();
        _databasePath = Path.Combine(dataDirectory, "mg-business-platform.db");
        _storefrontPidPath = Path.Combine(dataDirectory, "storefront.pid");
        _publicHostingManager = new PublicHostingManager(AppContext.BaseDirectory, dataDirectory);
        _routerPortMappingService = new RouterPortMappingService(dataDirectory);
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _refreshTimer.Tick += async (_, _) =>
        {
            RefreshLicenseStatus();
            await RefreshLiveStatusAsync();
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
            try
            {
                await LoadPaymentTerminalProfilesAsync();
            }
            catch (Exception profileException)
            {
                PaymentProfileStatusText.Text = $"فایل پروفایل‌های PC-POS بارگذاری نشد؛ سایر بخش‌های برنامه در دسترس می‌مانند: {profileException.Message}";
            }
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
            if (string.Equals(tab.Header?.ToString(), "خرید و پرداخت", StringComparison.Ordinal))
            {
                tab.IsEnabled = payload.Modules.Contains("Accounting", StringComparer.Ordinal)
                    && payload.Modules.Contains("Inventory", StringComparer.Ordinal);
                continue;
            }

            var module = tab.Header?.ToString() switch
            {
                "نمای کلی" => "Core",
                "افزودنی‌ها" => "Cafe",
                "محصولات" or "گردش انبار" => "Inventory",
                "مشتریان" or "تأمین‌کنندگان" => "Core",
                "فروش حضوری (POS)" => "POS",
                "فاکتورهای فروش" or "تراز آزمایشی" or "صورت‌های مالی" or "دفتر کل" or "گردش نقد و بانک" or "سن مطالبات و بدهی" => "Accounting",
                "سفارش‌های آنلاین" => "Storefront",
                "تنظیمات PC-POS" => "POS",
                "سابقه تغییرات" => "Core",
                "تنظیمات فروشگاه" => null,
                "جلسات بازی" => "GameCafe",
                "رزروها" => "Reservations",
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

    private void ImportLicensePublicKey_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "انتخاب کلید عمومی برای تطبیق با کلید مورد اعتماد برنامه",
            Filter = "PEM public key (*.pem)|*.pem|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var candidate = File.ReadAllText(dialog.FileName);
            var trusted = LicenseCryptography.GetTrustedPublicKeyPem();
            if (!LicenseCryptography.PublicKeysMatch(candidate, trusted))
                throw new ArgumentException("این کلید متعلق به ناشر مورد اعتماد این نسخه نیست. کلید عمومی دلخواه قابل نصب نیست.");

            // Store only a convenience copy. Verification always uses the embedded
            // trust anchor, so editing/deleting this file cannot bypass licensing.
            WriteFileAtomically(Path.Combine(_dataDirectory, "license-public-key.pem"), trusted);
            RefreshLicenseStatus();
            StatusText.Text = "کلید عمومی با کلید ثابت ناشر تطبیق دارد؛ اعتبارسنجی برنامه به این فایل وابسته نیست.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or CryptographicException or InvalidOperationException)
        {
            LicenseStatusText.Text = $"کلید عمومی پذیرفته نشد: {ex.Message}";
        }
    }

    private void ImportLicenseFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "انتخاب لایسنس امضاشده",
            Filter = "License JSON (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var info = _licenseService.LoadOrCreate(_dataDirectory);
            var trustedPublicKey = LicenseCryptography.GetTrustedPublicKeyPem();
            var envelope = LicenseCryptography.DeserializeEnvelope(File.ReadAllText(dialog.FileName));
            var result = LicenseCryptography.Verify(envelope, trustedPublicKey,
                info.SoftwareSerial, info.HardwareSerial, DateTimeOffset.UtcNow);
            if (!result.IsValid)
            {
                LicenseStatusText.Text = $"لایسنس وارد نشد: {result.Message}";
                return;
            }

            WriteFileAtomically(Path.Combine(_dataDirectory, "license.json"), File.ReadAllText(dialog.FileName));
            RefreshLicenseStatus();
            StatusText.Text = $"لایسنس معتبر برای {result.Payload!.CustomerName} وارد شد.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or CryptographicException or System.Text.Json.JsonException)
        {
            LicenseStatusText.Text = $"وارد کردن لایسنس ناموفق بود: {ex.Message}";
        }
    }

    private static void WriteFileAtomically(string destinationPath, string content)
    {
        var temporaryPath = destinationPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporaryPath, content);
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

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
        GameSessionRateInput.ToolTip = $"نرخ ساعتی هر بازیکن ({settings.CurrencyCode})";
        HostnameInput.Text = settings.PublicHostname;
        OnlineOrdersInput.IsChecked = settings.OnlineOrderingEnabled;
        OnlineReservationsInput.IsChecked = settings.OnlineReservationsEnabled;
        UpdatePosCartTotal();
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
            var selectedLedgerAccountCode = GeneralLedgerAccountInput.SelectedValue as string;
            var ledgerAccountsForPicker = await db.LedgerAccounts.AsNoTracking()
                .Where(account => account.IsActive && account.IsPostingAllowed)
                .OrderBy(account => account.Code).ToListAsync();
            GeneralLedgerAccountInput.ItemsSource = ledgerAccountsForPicker;
            if (!string.IsNullOrWhiteSpace(selectedLedgerAccountCode))
                GeneralLedgerAccountInput.SelectedValue = selectedLedgerAccountCode;
            if (GeneralLedgerAccountInput.SelectedItem is null && ledgerAccountsForPicker.Count > 0)
                GeneralLedgerAccountInput.SelectedIndex = 0;

            var products = await db.StoreProducts.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
            ProductsGrid.ItemsSource = products;
            PosProductInput.ItemsSource = products.Where(x => x.IsAvailable && x.StockQuantity > 0).ToList();
            PurchaseProductInput.ItemsSource = products;
            CustomersGrid.ItemsSource = await db.Customers.AsNoTracking()
                .OrderBy(x => x.DisplayName).ToListAsync();
            var suppliers = await db.Suppliers.AsNoTracking().OrderBy(x => x.DisplayName).ToListAsync();
            SuppliersGrid.ItemsSource = suppliers;
            PurchaseSupplierInput.ItemsSource = suppliers.Where(x => x.IsActive).ToList();
            ReservationResourcesGrid.ItemsSource = await db.ReservationResources.AsNoTracking()
                .Where(x => x.BranchId == _sessionBranchId)
                .OrderBy(x => x.Name).ToListAsync();
            var reservationRows = await (
                from reservation in db.CustomerReservations.AsNoTracking()
                join resource in db.ReservationResources.AsNoTracking() on reservation.ResourceId equals resource.Id
                where resource.BranchId == _sessionBranchId
                select new ReservationListRow(reservation.Id, reservation.ReservationNumber, resource.Name,
                    reservation.GuestName, reservation.GuestPhone, reservation.GuestCount,
                    reservation.StartsAtUtc, reservation.EndsAtUtc, reservation.Status)).ToListAsync();
            CustomerReservationsGrid.ItemsSource = reservationRows
                .OrderByDescending(row => row.StartsAtUtc).ToList();
            var stockMovementRows = await (
                from movement in db.StockMovements.AsNoTracking()
                join product in db.StoreProducts.AsNoTracking() on movement.ProductId equals product.Id
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
                }).ToListAsync();
            // EF Core's SQLite provider cannot translate DateTimeOffset ordering.
            // Sort the already-projected local rows in memory to keep refresh reliable.
            StockMovementsGrid.ItemsSource = stockMovementRows
                .OrderByDescending(x => x.OccurredAt).Take(500).ToList();

            var invoiceRecords = await db.SalesInvoices.AsNoTracking().ToListAsync();
            var invoiceRows = invoiceRecords
                .OrderByDescending(x => x.IssueDate)
                .ThenByDescending(x => x.CreatedAt)
                .Take(500)
                .Select(x => new InvoiceListItem(x.Id, x.InvoiceNumber, x.CustomerName, x.CustomerPhone,
                    x.IssueDate, x.DueDate, x.TotalAmount, x.PaidAmount, x.TotalAmount - x.PaidAmount, x.CurrencyCode, x.Status))
                .ToList();
            InvoicesGrid.ItemsSource = invoiceRows;
            PosRecentInvoicesGrid.ItemsSource = invoiceRows.Take(100).ToList();

            var purchaseRows = await (
                from purchase in db.PurchaseInvoices.AsNoTracking()
                join supplier in db.Suppliers.AsNoTracking() on purchase.SupplierId equals supplier.Id
                select new PurchaseInvoiceGridRow(purchase.Id, purchase.InvoiceNumber, supplier.DisplayName,
                    purchase.IssueDate, purchase.DueDate, purchase.TotalAmount, purchase.PaidAmount,
                    purchase.TotalAmount - purchase.PaidAmount, purchase.CurrencyCode, purchase.Status))
                .ToListAsync();
            PurchaseInvoicesGrid.ItemsSource = purchaseRows
                .OrderByDescending(row => row.IssueDate).ThenByDescending(row => row.InvoiceNumber)
                .Take(500).ToList();
            UpdatePurchaseDraftTotal();
            UpdateSupplierPaymentSelection();

            var orderRecords = await db.CustomerOrders.AsNoTracking().ToListAsync();
            var onlineOrders = orderRecords
                .Where(x => x.OrderNumber.StartsWith("WEB-", StringComparison.Ordinal))
                .ToArray();
            OrdersGrid.ItemsSource = onlineOrders
                .OrderByDescending(x => x.CreatedAt).Take(500).ToList();
            await RefreshGameSessionsAsync(db);
            ProductCountText.Text = (await db.StoreProducts.CountAsync(x => x.IsAvailable))
                .ToString("N0", CultureInfo.GetCultureInfo("fa-IR"));
            OrderCountText.Text = onlineOrders.Length
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

    private async Task RefreshLiveStatusAsync()
    {
        try
        {
            await using var db = new BusinessDbContext(CreateOptions());
            await RefreshGameSessionsAsync(db);
            var running = await IsStorefrontRunningAsync();
            ServerStatusText.Text = running ? "در حال اجرا" : "متوقف";
            StartServerButton.IsEnabled = !running;
            StopServerButton.IsEnabled = running;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"خطا در به‌روزرسانی وضعیت زنده: {ex.Message}";
        }
    }

    private async Task LoadPaymentTerminalProfilesAsync()
    {
        var profiles = await _paymentTerminalProfileService.LoadAsync();
        PaymentTerminalProfilesGrid.ItemsSource = profiles.Select(profile => new PaymentTerminalProfileGridRow(
            profile.Id, profile.Name, profile.Vendor, profile.Model, PaymentConnectionTypeTitle(profile.ConnectionType),
            profile.IsEnabled, profile.IsDefault, profile.LastTestStatus)).ToList();
        if (_editingPaymentProfileId is Guid selectedId)
            PaymentTerminalProfilesGrid.SelectedItem = ((IEnumerable<PaymentTerminalProfileGridRow>)PaymentTerminalProfilesGrid.ItemsSource)
                .FirstOrDefault(row => row.Id == selectedId);
    }

    private static string PaymentConnectionTypeTitle(PaymentConnectionType type) => type switch
    {
        PaymentConnectionType.Manual => "دستی / خارجی",
        PaymentConnectionType.TcpIp => "شبکه TCP/IP",
        PaymentConnectionType.Serial => "سریال RS-232",
        PaymentConnectionType.UsbVirtualCom => "USB مجازی COM",
        PaymentConnectionType.VendorLocalApi => "API محلی فروشنده",
        PaymentConnectionType.WindowsService => "سرویس ویندوز",
        PaymentConnectionType.VendorSdk => "SDK اختصاصی",
        _ => "نامشخص"
    };

    private async void PaymentTerminalProfilesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PaymentTerminalProfilesGrid.SelectedItem is not PaymentTerminalProfileGridRow row)
            return;
        try
        {
            var profile = (await _paymentTerminalProfileService.LoadAsync()).SingleOrDefault(item => item.Id == row.Id);
            if (profile is not null)
                ShowPaymentProfile(profile);
        }
        catch (Exception ex)
        {
            PaymentProfileStatusText.Text = $"بارگذاری پروفایل ناموفق بود: {ex.Message}";
        }
    }

    private void NewPaymentProfile_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireLicensedModules(PaymentProfileStatusText, "ساخت پروفایل PC-POS", "POS")) return;

        _editingPaymentProfileId = Guid.NewGuid();
        PaymentProfileNameInput.Clear();
        PaymentProfileVendorInput.Clear();
        PaymentProfileModelInput.Clear();
        PaymentProfileDriverInput.Text = "manual";
        PaymentProfileProtocolInput.Clear();
        PaymentConnectionTypeInput.SelectedValue = nameof(PaymentConnectionType.Manual);
        PaymentHostInput.Clear();
        PaymentPortInput.Text = "5000";
        PaymentSerialPortInput.Text = "COM3";
        PaymentBaudRateInput.Text = "9600";
        PaymentDataBitsInput.Text = "8";
        PaymentParityInput.SelectedValue = "None";
        PaymentStopBitsInput.SelectedValue = "One";
        PaymentFlowControlInput.SelectedValue = "None";
        PaymentConnectTimeoutInput.Text = "3000";
        PaymentResponseTimeoutInput.Text = "5000";
        PaymentCurrencyInput.Text = "IRR";
        PaymentMerchantIdInput.Clear();
        PaymentTerminalIdInput.Clear();
        PaymentEndpointInput.Clear();
        PaymentAuthReferenceInput.Clear();
        PaymentEnabledInput.IsChecked = true;
        PaymentDefaultInput.IsChecked = false;
        PaymentTerminalProfilesGrid.SelectedItem = null;
        PaymentProfileStatusText.Text = "پروفایل جدید؛ نوع اتصال را بر اساس مستندات رسمی دستگاه انتخاب کنید.";
    }

    private PaymentTerminalProfile BuildPaymentProfile()
    {
        if (!Enum.TryParse<PaymentConnectionType>(PaymentConnectionTypeInput.SelectedValue?.ToString(), out var connectionType))
            throw new ArgumentException("نوع اتصال را انتخاب کنید.");

        if (!int.TryParse(PaymentPortInput.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port)
            || !int.TryParse(PaymentBaudRateInput.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var baudRate)
            || !int.TryParse(PaymentDataBitsInput.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var dataBits)
            || !int.TryParse(PaymentConnectTimeoutInput.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var connectTimeout)
            || !int.TryParse(PaymentResponseTimeoutInput.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var responseTimeout))
            throw new ArgumentException("درگاه، Baud rate و مهلت‌های زمانی باید عدد صحیح معتبر باشند.");

        var profile = new PaymentTerminalProfile
        {
            Id = _editingPaymentProfileId ?? Guid.NewGuid(),
            Name = PaymentProfileNameInput.Text,
            Vendor = PaymentProfileVendorInput.Text,
            Model = PaymentProfileModelInput.Text,
            DriverId = PaymentProfileDriverInput.Text,
            ProtocolVersion = PaymentProfileProtocolInput.Text,
            ConnectionType = connectionType,
            Host = PaymentHostInput.Text,
            Port = port,
            SerialPort = PaymentSerialPortInput.Text,
            BaudRate = baudRate,
            DataBits = dataBits,
            Parity = PaymentParityInput.SelectedValue?.ToString() ?? "None",
            StopBits = PaymentStopBitsInput.SelectedValue?.ToString() ?? "One",
            FlowControl = PaymentFlowControlInput.SelectedValue?.ToString() ?? "None",
            ConnectTimeoutMs = connectTimeout,
            ResponseTimeoutMs = responseTimeout,
            CurrencyCode = PaymentCurrencyInput.Text.Trim().ToUpperInvariant(),
            MerchantId = PaymentMerchantIdInput.Text,
            TerminalId = PaymentTerminalIdInput.Text,
            Endpoint = PaymentEndpointInput.Text,
            AuthenticationReference = PaymentAuthReferenceInput.Text,
            IsEnabled = PaymentEnabledInput.IsChecked == true,
            IsDefault = PaymentDefaultInput.IsChecked == true
        };
        PaymentTerminalProfileService.Validate(profile);
        return profile;
    }

    private void ShowPaymentProfile(PaymentTerminalProfile profile)
    {
        _editingPaymentProfileId = profile.Id;
        PaymentProfileNameInput.Text = profile.Name;
        PaymentProfileVendorInput.Text = profile.Vendor;
        PaymentProfileModelInput.Text = profile.Model;
        PaymentProfileDriverInput.Text = profile.DriverId;
        PaymentProfileProtocolInput.Text = profile.ProtocolVersion;
        PaymentConnectionTypeInput.SelectedValue = profile.ConnectionType.ToString();
        PaymentHostInput.Text = profile.Host;
        PaymentPortInput.Text = profile.Port.ToString(CultureInfo.InvariantCulture);
        PaymentSerialPortInput.Text = profile.SerialPort;
        PaymentBaudRateInput.Text = profile.BaudRate.ToString(CultureInfo.InvariantCulture);
        PaymentDataBitsInput.Text = profile.DataBits.ToString(CultureInfo.InvariantCulture);
        PaymentParityInput.SelectedValue = profile.Parity;
        PaymentStopBitsInput.SelectedValue = profile.StopBits;
        PaymentFlowControlInput.SelectedValue = profile.FlowControl;
        PaymentConnectTimeoutInput.Text = profile.ConnectTimeoutMs.ToString(CultureInfo.InvariantCulture);
        PaymentResponseTimeoutInput.Text = profile.ResponseTimeoutMs.ToString(CultureInfo.InvariantCulture);
        PaymentCurrencyInput.Text = profile.CurrencyCode;
        PaymentMerchantIdInput.Text = profile.MerchantId;
        PaymentTerminalIdInput.Text = profile.TerminalId;
        PaymentEndpointInput.Text = profile.Endpoint;
        PaymentAuthReferenceInput.Text = profile.AuthenticationReference;
        PaymentEnabledInput.IsChecked = profile.IsEnabled;
        PaymentDefaultInput.IsChecked = profile.IsDefault;
        PaymentProfileStatusText.Text = string.IsNullOrWhiteSpace(profile.LastTestMessage)
            ? $"پروفایل «{profile.Name}» بارگذاری شد. وضعیت آزمون: {profile.LastTestStatus}."
            : $"پروفایل «{profile.Name}» — {profile.LastTestStatus}: {profile.LastTestMessage}";
    }

    private async void SavePaymentProfile_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireLicensedModules(PaymentProfileStatusText, "ذخیره پروفایل PC-POS", "POS")) return;

        try
        {
            var profile = BuildPaymentProfile();
            await _paymentTerminalProfileService.SaveAsync(profile);
            _editingPaymentProfileId = profile.Id;
            await LoadPaymentTerminalProfilesAsync();
            PaymentProfileStatusText.Text = $"پروفایل «{profile.Name}» ذخیره شد. هیچ تراکنش پرداختی ارسال نشده است.";
        }
        catch (Exception ex)
        {
            PaymentProfileStatusText.Text = $"ذخیره پروفایل ناموفق بود: {ex.Message}";
        }
    }

    private async void TestPaymentProfile_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireLicensedModules(PaymentProfileStatusText, "آزمایش اتصال PC-POS", "POS")) return;

        try
        {
            var profile = BuildPaymentProfile();
            var result = await _paymentTerminalProfileService.TestConnectionAsync(profile);
            PaymentProfileStatusText.Text = $"{result.Status} — {result.Message}";
            if (result.ConfigurationValid)
            {
                profile = profile with
                {
                    LastTestedAtUtc = result.TestedAtUtc,
                    LastTestStatus = result.Status,
                    LastTestMessage = result.Message
                };
                await _paymentTerminalProfileService.SaveAsync(profile);
                _editingPaymentProfileId = profile.Id;
                await LoadPaymentTerminalProfilesAsync();
            }
        }
        catch (Exception ex)
        {
            PaymentProfileStatusText.Text = $"آزمون اتصال انجام نشد: {ex.Message}";
        }
    }

    private async void DeletePaymentProfile_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireLicensedModules(PaymentProfileStatusText, "حذف پروفایل PC-POS", "POS")) return;

        var id = PaymentTerminalProfilesGrid.SelectedItem is PaymentTerminalProfileGridRow selected
            ? selected.Id
            : _editingPaymentProfileId;
        if (id is not Guid profileId)
        {
            PaymentProfileStatusText.Text = "ابتدا یک پروفایل را انتخاب کنید.";
            return;
        }
        if (!ConfirmationDialog.Confirm(this, "حذف پروفایل PC-POS",
            "این کار فقط تنظیمات ذخیره‌شده را حذف می‌کند و هیچ تراکنش بانکی را لغو یا بازپرداخت نمی‌کند.",
            "حذف پروفایل"))
            return;

        try
        {
            await _paymentTerminalProfileService.DeleteAsync(profileId);
            NewPaymentProfile_Click(this, new RoutedEventArgs());
            await LoadPaymentTerminalProfilesAsync();
            PaymentProfileStatusText.Text = "پروفایل حذف شد.";
        }
        catch (Exception ex)
        {
            PaymentProfileStatusText.Text = $"حذف پروفایل ناموفق بود: {ex.Message}";
        }
    }

    private async void RefreshPaymentProfiles_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireLicensedModules(PaymentProfileStatusText, "نمایش پروفایل‌های PC-POS", "POS")) return;
        try
        {
            await LoadPaymentTerminalProfilesAsync();
            PaymentProfileStatusText.Text = "فهرست پروفایل‌ها به‌روزرسانی شد.";
        }
        catch (Exception ex)
        {
            PaymentProfileStatusText.Text = $"بارگذاری پروفایل‌ها ناموفق بود: {ex.Message}";
        }
    }

    private async void ExportPaymentProfiles_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireLicensedModules(PaymentProfileStatusText, "خروجی پروفایل‌های PC-POS", "POS")) return;

        var dialog = new SaveFileDialog
        {
            Title = "ذخیره پشتیبان پروفایل‌های PC-POS",
            Filter = "PC-POS profiles (*.json)|*.json",
            DefaultExt = ".json",
            AddExtension = true,
            FileName = $"MG-PC-POS-Profiles-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            await _paymentTerminalProfileService.ExportAsync(dialog.FileName);
            PaymentProfileStatusText.Text = $"پشتیبان پروفایل‌ها ذخیره شد: {dialog.FileName}. این فایل شامل رمز یا اطلاعات کارت نیست.";
        }
        catch (Exception ex)
        {
            PaymentProfileStatusText.Text = $"خروجی پروفایل‌ها ناموفق بود: {ex.Message}";
        }
    }

    private async void ImportPaymentProfiles_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireLicensedModules(PaymentProfileStatusText, "ورود پروفایل‌های PC-POS", "POS")) return;

        var dialog = new OpenFileDialog
        {
            Title = "انتخاب پشتیبان پروفایل‌های PC-POS",
            Filter = "PC-POS profiles (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;
        if (!ConfirmationDialog.Confirm(this, "ورود پشتیبان PC-POS",
            "فهرست فعلی پروفایل‌ها با فایل انتخابی جایگزین می‌شود. این کار هیچ پرداخت، برگشت وجه یا تراکنش بانکی انجام نمی‌دهد.",
            "جایگزینی پروفایل‌ها"))
            return;
        try
        {
            await _paymentTerminalProfileService.ImportAsync(dialog.FileName);
            _editingPaymentProfileId = null;
            await LoadPaymentTerminalProfilesAsync();
            PaymentProfileStatusText.Text = "پروفایل‌ها از پشتیبان وارد شدند. درایور و سازگاری دستگاه باید جداگانه بررسی شوند.";
        }
        catch (Exception ex)
        {
            PaymentProfileStatusText.Text = $"ورود پروفایل‌ها ناموفق بود؛ تنظیمات قبلی حفظ شدند: {ex.Message}";
        }
    }

    private async Task RefreshGameSessionsAsync(BusinessDbContext db)
    {
        var sessions = await new GameSessionRepository(db).FindRecentAsync();
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
            ? "جلسه بازی‌ای ثبت نشده است."
            : $"تعداد جلسات اخیر: {rows.Count} — جلسه پایان‌یافته را انتخاب کنید تا فاکتور آن صادر شود؛ هزینه زمان بر اساس نرخ ساعتی و گردکردن ۱۵ دقیقه‌ای محاسبه می‌شود.";
    }

    private void AddSessionPlayer_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireLicensedModules(GameSessionStatusText, "افزودن بازیکن جلسه بازی", "GameCafe")) return;

        SessionPlayersGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        SessionPlayersGrid.CommitEdit(DataGridEditingUnit.Row, true);
        _sessionPlayerDrafts.Add(new GameSessionPlayerDraft
        {
            DisplayName = $"بازیکن {_sessionPlayerDrafts.Count + 1}"
        });
    }

    private async void StartGameSession_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireLicensedModules(GameSessionStatusText, "شروع جلسه بازی", "GameCafe")) return;

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
            await using var db = new BusinessDbContext(CreateOptions());
            var currencyCode = (await db.StoreSettings.AsNoTracking()
                .Where(settings => settings.Id == 1)
                .Select(settings => settings.CurrencyCode)
                .SingleAsync()).Trim().ToUpperInvariant();
            _ = Money.Zero(currencyCode);

            var players = drafts.Select(player =>
            {
                if (player.EntryAmount < 0m || player.TimeCharge < 0m || player.AdditionalAmount < 0m)
                    throw new ArgumentOutOfRangeException(nameof(player), "مبالغ بازیکنان نمی‌توانند منفی باشند.");
                return new GameSessionPlayer(Guid.NewGuid(), player.DisplayName,
                    new Money(player.EntryAmount, currencyCode),
                    new Money(player.TimeCharge, currencyCode),
                    new Money(player.AdditionalAmount, currencyCode));
            }).ToArray();

            var session = new GameSession(Guid.NewGuid(), _sessionBranchId, _sessionDeviceId,
                gameName, DateTimeOffset.UtcNow, new Money(hourlyRate, currencyCode), players);

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

    private async void RefreshGameSessions_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireLicensedModules(GameSessionStatusText, "نمایش جلسات بازی", "GameCafe")) return;
        await RefreshAsync();
    }

    private async void CreateGameSessionInvoice_Click(object sender, RoutedEventArgs e)
    {
        if (!HasLicensedModule("GameCafe") || !HasLicensedModule("Accounting"))
        {
            GameSessionStatusText.Text = "برای صدور فاکتور جلسه، لایسنس معتبر دارای ماژول‌های GameCafe و Accounting لازم است.";
            return;
        }
        if (GameSessionsGrid.SelectedItem is not GameSessionGridRow row)
        {
            GameSessionStatusText.Text = "ابتدا جلسه پایان‌یافته را از فهرست انتخاب کنید.";
            return;
        }

        if (!ConfirmationDialog.Confirm(this, "صدور فاکتور جلسه بازی",
            $"برای جلسه «{row.GameName}» فاکتور از مبالغ نهایی بازیکنان صادر می‌شود. دریافت وجه در مرحله جداگانه ثبت خواهد شد.",
            "صدور فاکتور"))
            return;

        try
        {
            await using var db = new BusinessDbContext(CreateOptions());
            var invoice = await new SalesAccountingService(db).CreateGameSessionInvoiceAsync(
                row.Id, DateOnly.FromDateTime(DateTime.Today));
            GameSessionStatusText.Text =
                $"فاکتور {invoice.InvoiceNumber} با مبلغ {invoice.TotalAmount:N2} {invoice.CurrencyCode} صادر شد. برای ثبت دریافت وجه، فاکتور را در بخش فاکتورها انتخاب کنید.";
            await RefreshAsync();
            await RefreshTrialBalanceAsync();
        }
        catch (Exception ex)
        {
            GameSessionStatusText.Text = $"صدور فاکتور جلسه ناموفق بود: {ex.Message}";
        }
    }

    private async Task ChangeSelectedGameSessionAsync(GameSessionTransition transition)
    {
        if (!RequireLicensedModules(GameSessionStatusText, "مدیریت جلسه بازی", "GameCafe")) return;
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

    private async void ImportProductsCsv_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireLicensedModules("ورود کالاها از CSV", "Inventory"))
            return;

        var dialog = new OpenFileDialog
        {
            Title = "انتخاب فایل CSV کالاها",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            if (new FileInfo(dialog.FileName).Length > 10L * 1024 * 1024)
            {
                ProductCsvStatusText.Text = "فایل CSV بیش از ۱۰ مگابایت است؛ فایل کوچک‌تری انتخاب کنید.";
                return;
            }

            var csv = await File.ReadAllTextAsync(dialog.FileName);
            await using var db = new BusinessDbContext(CreateOptions());
            var result = await new ProductCsvImportService(db).ImportAsync(csv);
            if (result.Issues.Count > 0)
            {
                var details = string.Join(Environment.NewLine, result.Issues.Take(10)
                    .Select(issue => $"ردیف {issue.RowNumber}: {issue.Message}"));
                ProductCsvStatusText.Text = $"ورود لغو شد؛ هیچ کالایی ثبت نشد. تعداد خطاها: {result.Issues.Count}. " +
                    (result.Issues.Count > 10 ? "فقط ۱۰ خطای نخست نمایش داده شده است." : string.Empty) +
                    Environment.NewLine + details;
                StatusText.Text = "ورود CSV کالاها به‌دلیل خطای اعتبارسنجی انجام نشد.";
                return;
            }

            ProductCsvStatusText.Text = $"ورود کالاها انجام شد: {result.ImportedCount} کالای جدید، " +
                $"{result.SkippedExistingCount} کد تکراری رد شد.";
            StatusText.Text = ProductCsvStatusText.Text;
            try
            {
                await RefreshAsync();
            }
            catch (Exception refreshException)
            {
                ProductCsvStatusText.Text = $"ورود کالاها انجام شد ({result.ImportedCount} مورد)، اما تازه‌سازی فهرست ناموفق بود: {refreshException.Message}";
                StatusText.Text = ProductCsvStatusText.Text;
            }
        }
        catch (Exception ex)
        {
            ProductCsvStatusText.Text = $"ورود CSV کالاها ناموفق بود: {ex.Message}";
            StatusText.Text = ProductCsvStatusText.Text;
        }
    }

    private async void ExportProductsCsv_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireLicensedModules("خروجی کالاها به CSV", "Inventory"))
            return;

        var dialog = new SaveFileDialog
        {
            Title = "ذخیره فهرست کالاها",
            FileName = $"MG-Products-{DateTime.Today:yyyyMMdd}.csv",
            Filter = "CSV files (*.csv)|*.csv",
            DefaultExt = ".csv",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        string? temporaryPath = null;
        try
        {
            await using var db = new BusinessDbContext(CreateOptions());
            var products = await db.StoreProducts.AsNoTracking()
                .OrderBy(product => product.Sku)
                .Take(100001)
                .ToListAsync();
            var truncated = products.Count > 100000;
            if (truncated)
                products = products.Take(100000).ToList();

            var csv = new System.Text.StringBuilder();
            csv.AppendLine("SKU,Name,Description,UnitPrice,UnitCost,CurrencyCode,StockQuantity,IsAvailable");
            foreach (var product in products)
            {
                var fields = new[]
                {
                    AuditCsvFormatter.EscapeField(product.Sku),
                    AuditCsvFormatter.EscapeField(product.Name),
                    AuditCsvFormatter.EscapeField(product.Description),
                    product.UnitPrice.ToString(CultureInfo.InvariantCulture),
                    product.UnitCost.ToString(CultureInfo.InvariantCulture),
                    AuditCsvFormatter.EscapeField(product.CurrencyCode),
                    product.StockQuantity.ToString(CultureInfo.InvariantCulture),
                    product.IsAvailable.ToString().ToLowerInvariant()
                };
                csv.AppendLine(string.Join(",", fields));
            }

            var outputPath = Path.GetFullPath(dialog.FileName);
            temporaryPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, csv.ToString(),
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            File.Move(temporaryPath, outputPath, overwrite: true);
            temporaryPath = null;
            ProductCsvStatusText.Text = $"خروجی کالاها ذخیره شد: {products.Count} کالا، {outputPath}" +
                (truncated ? "؛ به‌دلیل محدودیت ایمنی فقط ۱۰۰٬۰۰۰ کالا صادر شد." : ".");
            StatusText.Text = ProductCsvStatusText.Text;
        }
        catch (Exception ex)
        {
            ProductCsvStatusText.Text = $"خروجی CSV کالاها ناموفق بود: {ex.Message}";
            StatusText.Text = ProductCsvStatusText.Text;
        }
        finally
        {
            if (temporaryPath is not null && File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private async void AddProduct_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireLicensedModules("افزودن کالا", "Inventory")) return;

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

    private void SaveParsPackDnsToken_Click(object sender, RoutedEventArgs e)
    {
        if (!HasLicensedModule("Storefront"))
        {
            DnsSyncStatusText.Text = "برای مدیریت DNS فروشگاه آنلاین، لایسنس معتبر دارای ماژول Storefront لازم است.";
            return;
        }

        try
        {
            var token = ParsPackDnsTokenInput.Password.Trim();
            if (token.Length == 0)
            {
                DnsSyncStatusText.Text = "ابتدا توکن API پارس‌پک را وارد کنید.";
                return;
            }

            WindowsCredentialStore.Save(ParsPackDnsCredentialTarget, token);
            ParsPackDnsTokenInput.Clear();
            DnsSyncStatusText.Text = "توکن در Windows Credential Manager برای همین حساب ویندوز ذخیره شد؛ در فایل تنظیمات برنامه ذخیره نمی‌شود.";
        }
        catch (Exception ex)
        {
            DnsSyncStatusText.Text = $"ذخیره امن توکن ناموفق بود: {ex.Message}";
        }
    }

    private async void SyncParsPackDns_Click(object sender, RoutedEventArgs e)
    {
        if (!HasLicensedModule("Storefront"))
        {
            DnsSyncStatusText.Text = "برای مدیریت DNS فروشگاه آنلاین، لایسنس معتبر دارای ماژول Storefront لازم است.";
            return;
        }

        var hostname = HostnameInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(hostname))
        {
            DnsSyncStatusText.Text = "ابتدا دامنه فروشگاه را وارد کنید.";
            return;
        }

        try
        {
            var enteredToken = ParsPackDnsTokenInput.Password.Trim();
            if (enteredToken.Length > 0)
            {
                WindowsCredentialStore.Save(ParsPackDnsCredentialTarget, enteredToken);
                ParsPackDnsTokenInput.Clear();
            }

            var token = WindowsCredentialStore.Read(ParsPackDnsCredentialTarget);
            if (string.IsNullOrWhiteSpace(token))
            {
                DnsSyncStatusText.Text = "برای همگام‌سازی خودکار، توکن API پارس‌پک را وارد و ذخیره کنید.";
                return;
            }

            var result = await _parsPackDnsService.SyncARecordAsync(hostname, token);
            if (result.Status == ParsPackDnsSyncStatus.ConfirmationRequired)
            {
                if (!ConfirmationDialog.Confirm(this, "تغییر رکورد DNS پارس‌پک", result.Message))
                {
                    DnsSyncStatusText.Text = "رکورد DNS تغییری نکرد.";
                    return;
                }

                result = await _parsPackDnsService.SyncARecordAsync(hostname, token, replaceExisting: true);
            }

            DnsSyncStatusText.Text = result.Message;
        }
        catch (Exception ex)
        {
            DnsSyncStatusText.Text = $"همگام‌سازی DNS پارس‌پک ناموفق بود: {ex.Message}";
        }
    }

    private void DeleteParsPackDnsToken_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireLicensedModules("حذف اعتبارنامه DNS پارس‌پک", "Storefront")) return;
        if (!ConfirmationDialog.Confirm(this, "حذف توکن DNS پارس‌پک",
            "توکن ذخیره‌شده از Windows Credential Manager این حساب حذف شود؟"))
            return;

        try
        {
            WindowsCredentialStore.Delete(ParsPackDnsCredentialTarget);
            ParsPackDnsTokenInput.Clear();
            DnsSyncStatusText.Text = "توکن ذخیره‌شده حذف شد.";
        }
        catch (Exception ex)
        {
            DnsSyncStatusText.Text = $"حذف توکن ناموفق بود: {ex.Message}";
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

        if (OnlineOrdersInput.IsChecked == true
            && (!HasLicensedModule("Storefront")
                || !HasLicensedModule("Accounting")
                || !HasLicensedModule("Inventory")))
        {
            StatusText.Text = "برای فعال‌سازی سفارش آنلاین، لایسنس معتبر دارای ماژول‌های Storefront، Accounting و Inventory لازم است.";
            return;
        }
        if (OnlineReservationsInput.IsChecked == true
            && (!HasLicensedModule("Storefront") || !HasLicensedModule("Reservations")))
        {
            StatusText.Text = "برای فعال‌سازی رزرو آنلاین، لایسنس معتبر دارای هر دو ماژول Storefront و Reservations لازم است.";
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
            if (settings.PublicPublicationEnabled && OnlineOrdersInput.IsChecked != true && OnlineReservationsInput.IsChecked != true)
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
            settings.OnlineReservationsEnabled = OnlineReservationsInput.IsChecked == true;
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
        if (!RequireLicensedModules("ویرایش کالا", "Inventory")) return;

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
        if (!RequireLicensedModules("اصلاح موجودی", "Inventory")) return;

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

    private void AddPosCartItem_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireLicensedModules("افزودن کالا به سبد فروش", "POS", "Inventory")) return;

        if (PosProductInput.SelectedItem is not StoreProduct product)
        {
            StatusText.Text = "ابتدا کالا را انتخاب کنید.";
            return;
        }
        var text = PosQuantityInput.Text.Trim();
        if ((!int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var quantity)
            && !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out quantity))
            || quantity < 1 || quantity > 10000)
        {
            StatusText.Text = "تعداد باید عدد صحیح بین ۱ تا ۱۰۰۰۰ باشد.";
            return;
        }
        if (!product.IsAvailable || product.StockQuantity <= 0)
        {
            StatusText.Text = "این کالا موجود یا قابل فروش نیست.";
            return;
        }
        if (_posCartLines.Count > 0 && _posCartLines[0].CurrencyCode != product.CurrencyCode)
        {
            StatusText.Text = "واحد پول کالا با سبد فعلی متفاوت است.";
            return;
        }
        var existing = _posCartLines.FirstOrDefault(x => x.ProductId == product.Id);
        var combined = checked((existing?.Quantity ?? 0) + quantity);
        if (combined > product.StockQuantity || combined > 10000)
        {
            StatusText.Text = $"موجودی «{product.Name}» کافی نیست؛ موجودی {product.StockQuantity} عدد است.";
            return;
        }
        if (existing is not null) _posCartLines.Remove(existing);
        _posCartLines.Add(new PosCartLineDraft(product.Id, product.Name, combined, product.UnitPrice, product.CurrencyCode));
        UpdatePosCartTotal();
        StatusText.Text = $"«{product.Name}» به سبد اضافه شد.";
    }

    private void RemovePosCartItem_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireLicensedModules("حذف کالا از سبد فروش", "POS", "Inventory")) return;

        if (PosCartGrid.SelectedItem is not PosCartLineDraft selected)
        {
            StatusText.Text = "برای حذف، یک ردیف از سبد را انتخاب کنید.";
            return;
        }
        _posCartLines.Remove(selected);
        UpdatePosCartTotal();
    }

    private void UpdatePosCartTotal()
    {
        try
        {
            var total = _posCartLines.Aggregate(0m, (sum, line) => checked(sum + line.LineTotal));
            var currency = _posCartLines.FirstOrDefault()?.CurrencyCode ?? CurrencyInput.Text.Trim();
            PosCartTotalText.Text = $"مجموع سبد: {total:N2} {currency}";
        }
        catch (OverflowException)
        {
            PosCartTotalText.Text = "مجموع سبد خارج از محدوده مجاز است.";
        }
    }

    private async void CompletePosSale_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireLicensedModules("ثبت فروش حضوری", "POS", "Accounting", "Inventory")) return;
        if (_isPosCheckoutInProgress) return;

        _isPosCheckoutInProgress = true;
        CompletePosSaleButton.IsEnabled = false;
        try
        {
        var cart = _posCartLines.ToArray();
        if (cart.Length == 0)
        {
            StatusText.Text = "سبد فروش خالی است؛ ابتدا کالا اضافه کنید.";
            return;
        }
        if (PosPaymentMethodInput.SelectedItem is not ComboBoxItem methodItem || methodItem.Tag is not string paymentMethod)
        {
            StatusText.Text = "روش دریافت وجه را انتخاب کنید.";
            return;
        }
        var customerName = string.IsNullOrWhiteSpace(PosCustomerNameInput.Text) ? "مشتری حضوری" : PosCustomerNameInput.Text.Trim();
        var customerPhone = PosCustomerPhoneInput.Text.Trim();
        if (customerName.Length > 160 || customerPhone.Length > 40)
        {
            StatusText.Text = "طول نام مشتری یا شماره تماس بیش از حد مجاز است.";
            return;
        }

        decimal total;
        try { total = cart.Aggregate(0m, (sum, line) => checked(sum + line.LineTotal)); }
        catch (OverflowException)
        {
            StatusText.Text = "مجموع سبد خارج از محدوده مجاز است.";
            return;
        }
        var summary = string.Join(Environment.NewLine, cart.Select(x => $"{x.ProductName} × {x.Quantity} = {x.LineTotal:N2} {x.CurrencyCode}"));
        if (!ConfirmationDialog.Confirm(this, "ثبت فروش حضوری",
            $"{summary}{Environment.NewLine}مجموع: {total:N2} {cart[0].CurrencyCode}{Environment.NewLine}موجودی، فاکتور و دریافت وجه به‌صورت تراکنشی ثبت می‌شوند.",
            "ثبت فروش"))
            return;

        SalesInvoiceRecord invoice;
        try
        {
            await using var db = new BusinessDbContext(CreateOptions());
            var accounting = new SalesAccountingService(db);
            invoice = await accounting.CreatePosSaleAsync(
                cart.Select(x => new PosSaleLineRequest(x.ProductId, x.Quantity, x.UnitPrice)).ToArray(),
                customerName, customerPhone, paymentMethod, string.Empty, "POS checkout",
                DateOnly.FromDateTime(DateTime.Today));
        }
        catch (Exception ex)
        {
            StatusText.Text = $"ثبت فروش حضوری ناموفق بود: {ex.Message}";
            return;
        }

        _posCartLines.Clear();
        UpdatePosCartTotal();
        StatusText.Text = $"فروش حضوری ثبت شد: فاکتور {invoice.InvoiceNumber}، مبلغ {invoice.TotalAmount:N2} {invoice.CurrencyCode}، وضعیت {invoice.Status}.";
        try
        {
            await RefreshAsync();
            await RefreshTrialBalanceAsync();
        }
        catch (Exception ex)
        {
            // The transaction has already committed. Never tell the operator the
            // sale failed, or encourage a retry, just because a follow-up refresh failed.
            StatusText.Text = $"فروش ثبت شد (فاکتور {invoice.InvoiceNumber})، اما تازه‌سازی فهرست/گزارش ناموفق بود: {ex.Message}";
        }
        }
        finally
        {
            _isPosCheckoutInProgress = false;
            CompletePosSaleButton.IsEnabled = true;
        }
    }

    private async void PrintSelectedInvoice_Click(object sender, RoutedEventArgs e)
    {
        if (!HasLicensedModule("Accounting"))
        {
            StatusText.Text = "برای چاپ فاکتور، لایسنس معتبر دارای ماژول Accounting لازم است.";
            return;
        }

        if (InvoicesGrid.SelectedItem is not InvoiceListItem selected)
        {
            StatusText.Text = "ابتدا فاکتور موردنظر را انتخاب کنید.";
            return;
        }

        try
        {
            await using var db = new BusinessDbContext(CreateOptions());
            var invoice = await db.SalesInvoices.AsNoTracking()
                .Include(item => item.Lines)
                .SingleOrDefaultAsync(item => item.Id == selected.Id);
            if (invoice is null)
            {
                StatusText.Text = "فاکتور پیدا نشد؛ فهرست را به‌روزرسانی کنید.";
                return;
            }

            var store = await db.StoreSettings.AsNoTracking().SingleAsync(item => item.Id == 1);
            var receipt = BuildReceiptVisual(store.StoreName, invoice);
            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true)
                return;

            // 205 DIP is approximately 54 mm of printable content, leaving small margins
            // for common 58 mm thermal printers. Paper size remains controlled by the
            // selected Windows printer driver so unsupported custom media is not forced.
            receipt.Measure(new Size(205, 10000));
            receipt.Arrange(new Rect(0, 0, 205, receipt.DesiredSize.Height));
            receipt.UpdateLayout();
            dialog.PrintVisual(receipt, $"MG receipt {invoice.InvoiceNumber}");
            StatusText.Text = $"رسید فاکتور {invoice.InvoiceNumber} به چاپگر انتخاب‌شده ارسال شد.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"چاپ رسید ناموفق بود: {ex.Message}";
        }
    }

    private static StackPanel BuildReceiptVisual(string storeName, SalesInvoiceRecord invoice)
    {
        var receipt = new StackPanel
        {
            Width = 205,
            Background = Brushes.White,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Thickness(0)
        };

        void AddText(string text, double fontSize = 10, bool bold = false, TextAlignment alignment = TextAlignment.Right)
        {
            receipt.Children.Add(new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Tahoma"),
                FontSize = fontSize,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                TextAlignment = alignment,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.Black,
                Margin = new Thickness(2, 3, 2, 3)
            });
        }

        void AddDivider()
        {
            receipt.Children.Add(new Border
            {
                Height = 1,
                Background = Brushes.Black,
                Margin = new Thickness(2, 4, 2, 4)
            });
        }

        AddText(string.IsNullOrWhiteSpace(storeName) ? "فروشگاه" : storeName.Trim(), 13, true, TextAlignment.Center);
        AddText("رسید فروش", 11, true, TextAlignment.Center);
        AddDivider();
        AddText($"شماره فاکتور: {invoice.InvoiceNumber}", 9);
        AddText($"تاریخ: {invoice.IssueDate.ToString("yyyy/MM/dd", CultureInfo.GetCultureInfo("fa-IR"))}", 9);
        AddText($"مشتری: {invoice.CustomerName}", 9);
        if (!string.IsNullOrWhiteSpace(invoice.CustomerPhone))
            AddText($"تلفن: {invoice.CustomerPhone}", 9);
        AddDivider();

        foreach (var line in invoice.Lines.OrderBy(item => item.ProductName, StringComparer.Ordinal))
        {
            AddText($"{line.ProductName} × {line.Quantity}", 9, true);
            AddText($"هر واحد: {FormatReceiptAmount(line.UnitPrice, invoice.CurrencyCode)}", 8);
            AddText($"جمع: {FormatReceiptAmount(line.LineTotal, invoice.CurrencyCode)}", 9, false);
        }

        AddDivider();
        AddText($"جمع کل: {FormatReceiptAmount(invoice.TotalAmount, invoice.CurrencyCode)}", 11, true);
        AddText($"پرداخت‌شده: {FormatReceiptAmount(invoice.PaidAmount, invoice.CurrencyCode)}", 9);
        AddText($"مانده: {FormatReceiptAmount(checked(invoice.TotalAmount - invoice.PaidAmount), invoice.CurrencyCode)}", 10, true);
        var statusText = invoice.Status switch
        {
            "Issued" => "صادرشده",
            "PartiallyPaid" => "پرداخت بخشی",
            "Paid" => "تسویه‌شده",
            "Voided" => "باطل‌شده",
            _ => invoice.Status
        };
        AddText($"وضعیت: {statusText}", 9);
        AddDivider();
        AddText("با تشکر از خرید شما", 10, true, TextAlignment.Center);
        return receipt;
    }

    private static string FormatReceiptAmount(decimal amount, string currencyCode)
    {
        var digits = string.Equals(currencyCode, "IRR", StringComparison.OrdinalIgnoreCase) ? "N0" : "N2";
        return $"{amount.ToString(digits, CultureInfo.GetCultureInfo("fa-IR"))} {currencyCode}";
    }

    private async void RecordPayment_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireLicensedModules("ثبت دریافت وجه", "Accounting")) return;
        if (_isSalesPaymentInProgress) return;

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

        _isSalesPaymentInProgress = true;
        RecordPaymentButton.IsEnabled = false;
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
        finally
        {
            _isSalesPaymentInProgress = false;
            RecordPaymentButton.IsEnabled = true;
        }
    }

    private async void LoadGeneralLedger_Click(object sender, RoutedEventArgs e)
    {
        if (!HasLicensedModule("Accounting"))
        {
            GeneralLedgerTotalsText.Text = "برای مشاهده دفتر کل، لایسنس معتبر دارای ماژول Accounting لازم است.";
            return;
        }
        if (GeneralLedgerAccountInput.SelectedItem is not LedgerAccountRecord selectedAccount)
        {
            GeneralLedgerTotalsText.Text = "یک حساب قابل ثبت را انتخاب کنید.";
            return;
        }

        var fromDate = DateOnly.FromDateTime(GeneralLedgerFromDateInput.SelectedDate
            ?? new DateTime(DateTime.Today.Year, 1, 1));
        var toDate = DateOnly.FromDateTime(GeneralLedgerToDateInput.SelectedDate ?? DateTime.Today);
        if (fromDate > toDate)
        {
            GeneralLedgerTotalsText.Text = "تاریخ شروع نمی‌تواند بعد از تاریخ پایان باشد.";
            return;
        }

        try
        {
            await using var db = new BusinessDbContext(CreateOptions());
            var settings = await db.StoreSettings.AsNoTracking().SingleAsync(x => x.Id == 1);
            var ledgerAccounts = await db.LedgerAccounts.AsNoTracking().ToListAsync();
            var journalRecords = await db.JournalEntries.AsNoTracking()
                .Include(entry => entry.Lines)
                .Where(entry => entry.EntryDate <= toDate && entry.CurrencyCode == settings.CurrencyCode)
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

            var report = MG.BusinessPlatform.Domain.Finance.GeneralLedgerCalculator.Calculate(
                accounts, entries, selectedAccount.Code, fromDate, toDate, settings.CurrencyCode);
            var entryNumbers = journalRecords.ToDictionary(entry => entry.Id, entry => entry.EntryNumber);
            GeneralLedgerGrid.ItemsSource = report.Lines.Select(line => new GeneralLedgerGridRow(
                line.EntryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                entryNumbers.TryGetValue(line.EntryId, out var entryNumber) ? entryNumber : line.EntryId.ToString("N")[..8],
                line.Description, line.Debit.Amount, line.Credit.Amount, line.RunningBalance.Amount)).ToList();
            GeneralLedgerTotalsText.Text =
                $"حساب {report.AccountCode} - {report.AccountName} | مانده اول دوره: {report.OpeningBalance.Amount:N2} {report.CurrencyCode} | " +
                $"گردش بدهکار: {report.PeriodDebits.Amount:N2} | گردش بستانکار: {report.PeriodCredits.Amount:N2} | " +
                $"مانده پایان دوره: {report.ClosingBalance.Amount:N2} {report.CurrencyCode}";
            StatusText.Text = $"دفتر کل حساب {report.AccountCode} بارگذاری شد.";
        }
        catch (Exception ex)
        {
            GeneralLedgerTotalsText.Text = $"بارگذاری دفتر کل ناموفق بود: {ex.Message}";
        }
    }

    private async void CalculateFinancialStatements_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireLicensedModules("محاسبه صورت‌های مالی", "Accounting")) return;
        await CalculateFinancialStatementsAsync();
    }

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

    private async void CalculateCashMovement_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireLicensedModules("گزارش گردش نقد و بانک", "Accounting")) return;
        await CalculateCashMovementAsync();
    }

    private async Task CalculateCashMovementAsync()
    {
        try
        {
            var fromDate = DateOnly.FromDateTime(CashMovementFromDateInput.SelectedDate ?? new DateTime(DateTime.Today.Year, 1, 1));
            var toDate = DateOnly.FromDateTime(CashMovementToDateInput.SelectedDate ?? DateTime.Today);
            if (fromDate > toDate)
            {
                CashMovementTotalsText.Text = "تاریخ شروع نمی‌تواند بعد از تاریخ پایان باشد.";
                return;
            }

            await using var db = new BusinessDbContext(CreateOptions());
            var settings = await db.StoreSettings.AsNoTracking().SingleAsync(x => x.Id == 1);
            var ledgerAccounts = await db.LedgerAccounts.AsNoTracking().ToListAsync();
            var cashCodes = new[] { "1000", "1010", "1020" }
                .Where(code => ledgerAccounts.Any(account => account.Code == code
                    && account.AccountType == nameof(MG.BusinessPlatform.Domain.Finance.AccountType.Asset)
                    && account.IsPostingAllowed))
                .ToArray();
            if (cashCodes.Length == 0)
            {
                CashMovementGrid.ItemsSource = Array.Empty<CashMovementGridRow>();
                CashMovementTotalsText.Text = "حساب نقد، کارت یا بانک قابل ثبت در دفتر کل پیدا نشد.";
                return;
            }

            var journalRecords = await db.JournalEntries.AsNoTracking()
                .Include(entry => entry.Lines)
                .Where(entry => entry.EntryDate <= toDate && entry.CurrencyCode == settings.CurrencyCode)
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

            var report = MG.BusinessPlatform.Domain.Finance.CashMovementReportCalculator.Calculate(
                accounts, entries, fromDate, toDate, settings.CurrencyCode, cashCodes);
            CashMovementGrid.ItemsSource = report.Lines.Select(line => new CashMovementGridRow(
                line.AccountCode, line.AccountName, line.OpeningBalance.Amount, line.Inflows.Amount,
                line.Outflows.Amount, line.ClosingBalance.Amount)).ToArray();
            CashMovementTotalsText.Text =
                $"گردش نقد و بانک ({fromDate:yyyy-MM-dd} تا {toDate:yyyy-MM-dd}) | " +
                $"مانده اول دوره: {report.OpeningBalance.Amount:N2} | ورودی: {report.TotalInflows.Amount:N2} | " +
                $"خروجی: {report.TotalOutflows.Amount:N2} | خالص تغییر: {report.NetMovement.Amount:N2} | " +
                $"مانده پایان دوره: {report.ClosingBalance.Amount:N2} {report.CurrencyCode}. " +
                "این گزارش گردش حساب‌های نقد و بانک است و جایگزین صورت جریان وجوه نقد طبقه‌بندی‌شده نیست.";
        }
        catch (Exception ex)
        {
            CashMovementTotalsText.Text = $"محاسبه گردش نقد و بانک ناموفق بود: {ex.Message}";
        }
    }

    private async void RefreshAuditEvents_Click(object sender, RoutedEventArgs e) =>
        await RefreshAuditEventsAsync();

    private async Task RefreshAuditEventsAsync()
    {
        if (!HasLicensedModule("Core"))
        {
            AuditEventsStatusText.Text = "برای مشاهده سابقه تغییرات، لایسنس معتبر لازم است.";
            return;
        }

        try
        {
            var fromDate = DateOnly.FromDateTime(AuditFromDateInput.SelectedDate ?? new DateTime(DateTime.Today.Year, 1, 1));
            var toDate = DateOnly.FromDateTime(AuditToDateInput.SelectedDate ?? DateTime.Today);
            if (fromDate > toDate)
            {
                AuditEventsStatusText.Text = "تاریخ شروع نمی‌تواند بعد از تاریخ پایان باشد.";
                return;
            }

            var fromUtcMilliseconds = new DateTimeOffset(fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local))
                .ToUniversalTime().ToUnixTimeMilliseconds();
            var toUtcExclusiveMilliseconds = new DateTimeOffset(
                toDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local))
                .ToUniversalTime().ToUnixTimeMilliseconds();
            await using var db = new BusinessDbContext(CreateOptions());
            var events = await db.AuditEvents.AsNoTracking()
                .Where(item => item.OccurredAtUnixMilliseconds >= fromUtcMilliseconds
                    && item.OccurredAtUnixMilliseconds < toUtcExclusiveMilliseconds)
                .OrderByDescending(item => item.OccurredAtUnixMilliseconds)
                .Take(1000)
                .ToListAsync();

            AuditEventsGrid.ItemsSource = events.Select(item => new AuditEventGridRow(
                DateTimeOffset.FromUnixTimeMilliseconds(item.OccurredAtUnixMilliseconds),
                item.Actor, item.MachineName, item.Action, item.EntityType, item.EntityId, item.ChangedFields)).ToArray();
            AuditEventsStatusText.Text = $"رویدادهای {fromDate:yyyy-MM-dd} تا {toDate:yyyy-MM-dd}: {events.Count}. " +
                "نمایش حداکثر ۱۰۰۰ رویداد؛ برای کاهش نگهداری داده حساس فقط نام فیلدهای تغییرکرده ثبت می‌شود، نه مقادیر قبل و بعد.";
        }
        catch (Exception ex)
        {
            AuditEventsStatusText.Text = $"بارگذاری سابقه تغییرات ناموفق بود: {ex.Message}";
        }
    }

    private async void ExportAuditEvents_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireLicensedModules(AuditEventsStatusText, "خروجی سابقه تغییرات", "Core"))
            return;

        var dialog = new SaveFileDialog
        {
            Title = "ذخیره سابقه تغییرات",
            FileName = $"MG-Audit-{DateTime.Today:yyyyMMdd}.csv",
            Filter = "CSV files (*.csv)|*.csv",
            DefaultExt = ".csv",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        string? temporaryPath = null;
        try
        {
            var fromDate = DateOnly.FromDateTime(
                AuditFromDateInput.SelectedDate ?? new DateTime(DateTime.Today.Year, 1, 1));
            var toDate = DateOnly.FromDateTime(AuditToDateInput.SelectedDate ?? DateTime.Today);
            if (fromDate > toDate)
            {
                AuditEventsStatusText.Text = "تاریخ شروع نمی‌تواند بعد از تاریخ پایان باشد.";
                return;
            }

            var fromUtcMilliseconds = new DateTimeOffset(
                fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local))
                .ToUniversalTime().ToUnixTimeMilliseconds();
            var toUtcExclusiveMilliseconds = new DateTimeOffset(
                toDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local))
                .ToUniversalTime().ToUnixTimeMilliseconds();

            await using var db = new BusinessDbContext(CreateOptions());
            var events = await db.AuditEvents.AsNoTracking()
                .Where(item => item.OccurredAtUnixMilliseconds >= fromUtcMilliseconds
                    && item.OccurredAtUnixMilliseconds < toUtcExclusiveMilliseconds)
                .OrderBy(item => item.OccurredAtUnixMilliseconds)
                .Take(100001)
                .ToListAsync();

            var truncated = events.Count > 100000;
            if (truncated)
                events = events.Take(100000).ToList();

            var csv = new System.Text.StringBuilder();
            csv.AppendLine(string.Join(",", new[]
            {
                "زمان UTC", "کاربر", "رایانه", "عملیات", "نوع داده", "شناسه رکورد", "فیلدهای تغییرکرده"
            }.Select(AuditCsvFormatter.EscapeField)));

            foreach (var item in events)
            {
                csv.AppendLine(string.Join(",", new[]
                {
                    DateTimeOffset.FromUnixTimeMilliseconds(item.OccurredAtUnixMilliseconds)
                        .ToString("O", CultureInfo.InvariantCulture),
                    item.Actor,
                    item.MachineName,
                    item.Action,
                    item.EntityType,
                    item.EntityId,
                    item.ChangedFields
                }.Select(AuditCsvFormatter.EscapeField)));
            }

            var outputPath = Path.GetFullPath(dialog.FileName);
            temporaryPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, csv.ToString(),
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            File.Move(temporaryPath, outputPath, overwrite: true);
            temporaryPath = null;

            AuditEventsStatusText.Text = $"خروجی CSV ذخیره شد: {events.Count} رویداد، {outputPath}" +
                (truncated ? "؛ به‌دلیل محدودیت ایمنی، فقط ۱۰۰٬۰۰۰ رویداد نخست صادر شد." : ".");
        }
        catch (Exception ex)
        {
            AuditEventsStatusText.Text = $"خروجی CSV ناموفق بود: {ex.Message}";
        }
        finally
        {
            if (temporaryPath is not null && File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private async void CalculateInvoiceAging_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireLicensedModules("گزارش سن مطالبات و بدهی", "Accounting")) return;
        await CalculateInvoiceAgingAsync();
    }

    private async Task CalculateInvoiceAgingAsync()
    {
        try
        {
            var asOfDate = DateOnly.FromDateTime(InvoiceAgingAsOfDateInput.SelectedDate ?? DateTime.Today);
            await using var db = new BusinessDbContext(CreateOptions());
            var settings = await db.StoreSettings.AsNoTracking().SingleAsync(x => x.Id == 1);
            var salesInvoices = await db.SalesInvoices.AsNoTracking()
                .Where(invoice => invoice.CurrencyCode == settings.CurrencyCode && invoice.Status != "Voided")
                .ToListAsync();
            var purchaseInvoices = await db.PurchaseInvoices.AsNoTracking()
                .Where(invoice => invoice.CurrencyCode == settings.CurrencyCode && invoice.Status != "Voided")
                .ToListAsync();
            var suppliers = await db.Suppliers.AsNoTracking().ToDictionaryAsync(supplier => supplier.Id);
            var documents = new List<MG.BusinessPlatform.Domain.Finance.AgingDocument>(
                salesInvoices.Count + purchaseInvoices.Count);
            documents.AddRange(salesInvoices.Select(invoice => new MG.BusinessPlatform.Domain.Finance.AgingDocument(
                "مطالبات مشتریان", invoice.InvoiceNumber, invoice.CustomerName, invoice.IssueDate,
                invoice.TotalAmount, invoice.PaidAmount, invoice.CurrencyCode, invoice.Status, invoice.DueDate)));
            documents.AddRange(purchaseInvoices.Select(invoice => new MG.BusinessPlatform.Domain.Finance.AgingDocument(
                "بدهی تأمین‌کنندگان", invoice.InvoiceNumber,
                suppliers.TryGetValue(invoice.SupplierId, out var supplier) ? supplier.DisplayName : "تأمین‌کننده نامشخص",
                invoice.IssueDate, invoice.TotalAmount, invoice.PaidAmount, invoice.CurrencyCode, invoice.Status, invoice.DueDate)));

            var report = MG.BusinessPlatform.Domain.Finance.InvoiceAgingCalculator.Calculate(documents, asOfDate, settings.CurrencyCode);
            InvoiceAgingGrid.ItemsSource = report.Lines.Select(line => new InvoiceAgingGridRow(
                line.DocumentType, line.DocumentNumber, line.CounterpartyName, line.IssueDate,
                line.DueDate, line.AgingDate, line.UsesIssueDateFallback ? "تاریخ صدور (جایگزین)" : "سررسید ثبت‌شده", line.AgeDays,
                InvoiceAgeBucketText(line.Bucket), line.TotalAmount.Amount, line.PaidAmount.Amount,
                line.OutstandingAmount.Amount)).ToArray();
            InvoiceAgingTotalsText.Text =
                $"مانده باز تا {asOfDate:yyyy-MM-dd} ({report.CurrencyCode}) | آینده‌تاریخ‌دار: {report.FutureDated.Amount:N2} | " +
                $"۰ تا ۳۰ روز: {report.Current0To30Days.Amount:N2} | ۳۱ تا ۶۰ روز: {report.Days31To60.Amount:N2} | " +
                $"۶۱ تا ۹۰ روز: {report.Days61To90.Amount:N2} | بیش از ۹۰ روز: {report.Over90Days.Amount:N2} | " +
                $"مجموع: {report.TotalOutstanding.Amount:N2}. سن بر اساس سررسید ثبت‌شده محاسبه می‌شود؛ اسنادی که سررسید ندارند بر اساس تاریخ صدور محاسبه و در ستون مبنای سن مشخص می‌شوند.";
        }
        catch (Exception ex)
        {
            InvoiceAgingTotalsText.Text = $"محاسبه سن مطالبات و بدهی ناموفق بود: {ex.Message}";
        }
    }

    private static string InvoiceAgeBucketText(MG.BusinessPlatform.Domain.Finance.InvoiceAgeBucket bucket) => bucket switch
    {
        MG.BusinessPlatform.Domain.Finance.InvoiceAgeBucket.FutureDated => "تاریخ آینده",
        MG.BusinessPlatform.Domain.Finance.InvoiceAgeBucket.Current0To30Days => "۰ تا ۳۰ روز",
        MG.BusinessPlatform.Domain.Finance.InvoiceAgeBucket.Days31To60 => "۳۱ تا ۶۰ روز",
        MG.BusinessPlatform.Domain.Finance.InvoiceAgeBucket.Days61To90 => "۶۱ تا ۹۰ روز",
        MG.BusinessPlatform.Domain.Finance.InvoiceAgeBucket.Over90Days => "بیش از ۹۰ روز",
        _ => "نامشخص"
    };

    private async void RefreshTrialBalance_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireLicensedModules("نمایش تراز آزمایشی", "Accounting")) return;
        await RefreshTrialBalanceAsync();
    }

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

    private async void RefreshReservations_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireLicensedModules("نمایش رزروها", "Reservations")) return;
        await RefreshAsync();
    }

    private async void AddReservationResource_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireLicensedModules("افزودن منبع رزرو", "Reservations")) return;

        var name = ReservationResourceNameInput.Text.Trim();
        if (name.Length is < 1 or > 160)
        {
            StatusText.Text = "نام منبع رزرو باید بین ۱ تا ۱۶۰ نویسه باشد.";
            return;
        }
        if (ReservationResourceTypeInput.SelectedItem is not ComboBoxItem typeItem
            || typeItem.Tag is not string resourceType)
        {
            StatusText.Text = "نوع منبع رزرو را انتخاب کنید.";
            return;
        }
        if (!int.TryParse(ReservationResourceCapacityInput.Text.Trim(), NumberStyles.None,
                CultureInfo.InvariantCulture, out var capacity) || capacity is < 1 or > 1000)
        {
            StatusText.Text = "ظرفیت باید عددی بین ۱ تا ۱۰۰۰ باشد.";
            return;
        }

        try
        {
            await using var db = new BusinessDbContext(CreateOptions());
            if (await db.ReservationResources.AnyAsync(x => x.BranchId == _sessionBranchId && x.Name == name))
            {
                StatusText.Text = "منبعی با این نام در این شعبه وجود دارد.";
                return;
            }
            db.ReservationResources.Add(new ReservationResourceRecord
            {
                Id = Guid.NewGuid(),
                BranchId = _sessionBranchId,
                Name = name,
                ResourceType = resourceType,
                Capacity = capacity,
                IsAvailable = true
            });
            await db.SaveChangesAsync();
            ReservationResourceNameInput.Clear();
            StatusText.Text = "منبع رزرو اضافه شد.";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"افزودن منبع رزرو ناموفق بود: {ex.Message}";
        }
    }

    private async void ToggleReservationResource_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireLicensedModules("تغییر وضعیت منبع رزرو", "Reservations")) return;

        if (ReservationResourcesGrid.SelectedItem is not ReservationResourceRecord selected)
        {
            StatusText.Text = "ابتدا یک منبع رزرو را انتخاب کنید.";
            return;
        }
        try
        {
            await using var db = new BusinessDbContext(CreateOptions());
            var resource = await db.ReservationResources.SingleOrDefaultAsync(x =>
                x.Id == selected.Id && x.BranchId == _sessionBranchId);
            if (resource is null)
            {
                StatusText.Text = "منبع رزرو انتخاب‌شده پیدا نشد؛ فهرست را تازه کنید.";
                return;
            }
            resource.IsAvailable = !resource.IsAvailable;
            await db.SaveChangesAsync();
            StatusText.Text = resource.IsAvailable ? "منبع رزرو دوباره فعال شد." : "منبع رزرو غیرفعال شد؛ رزروهای ثبت‌شده حفظ می‌شوند.";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"تغییر وضعیت منبع رزرو ناموفق بود: {ex.Message}";
        }
    }

    private async void ConfirmReservation_Click(object sender, RoutedEventArgs e) =>
        await UpdateReservationStatusAsync("Confirmed");

    private async void CancelReservation_Click(object sender, RoutedEventArgs e) =>
        await UpdateReservationStatusAsync("Cancelled");

    private async Task UpdateReservationStatusAsync(string status)
    {
        if (!RequireLicensedModules("تغییر وضعیت رزرو", "Reservations")) return;
        if (CustomerReservationsGrid.SelectedItem is not ReservationListRow selected)
        {
            StatusText.Text = "ابتدا یک درخواست رزرو را انتخاب کنید.";
            return;
        }
        try
        {
            await using var db = new BusinessDbContext(CreateOptions());
            var reservation = await db.CustomerReservations.SingleOrDefaultAsync(x => x.Id == selected.Id);
            if (reservation is null)
            {
                StatusText.Text = "رزرو انتخاب‌شده پیدا نشد؛ فهرست را تازه کنید.";
                return;
            }
            if (reservation.Status is not ("Requested" or "Confirmed"))
            {
                StatusText.Text = "وضعیت این رزرو دیگر قابل تغییر نیست.";
                return;
            }
            reservation.Status = status;
            await db.SaveChangesAsync();
            StatusText.Text = status == "Confirmed" ? "رزرو تأیید شد." : "رزرو لغو شد.";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"به‌روزرسانی وضعیت رزرو ناموفق بود: {ex.Message}";
        }
    }

    private async void AddCustomer_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireLicensedModules("افزودن مشتری", "Core")) return;

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

        _isSupplierPaymentInProgress = true;
        RecordSupplierPaymentButton.IsEnabled = false;
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

    private void AddPurchaseLine_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireLicensedModules("افزودن ردیف خرید", "Accounting", "Inventory")) return;

        if (PurchaseProductInput.SelectedItem is not StoreProduct product)
        {
            StatusText.Text = "برای افزودن ردیف خرید، یک کالا انتخاب کنید.";
            return;
        }

        if (!int.TryParse(PurchaseQuantityInput.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var quantity)
            && !int.TryParse(PurchaseQuantityInput.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out quantity))
        {
            StatusText.Text = "تعداد باید عدد صحیح معتبر باشد.";
            return;
        }

        if (!TryParseAmount(PurchaseUnitCostInput.Text, out var unitCost) || unitCost < 0m
            || !TryParseAmount(PurchaseDiscountInput.Text, out var discountPercent)
            || discountPercent is < 0m or > 100m)
        {
            StatusText.Text = "قیمت واحد باید نامنفی و تخفیف باید بین صفر تا صد درصد باشد.";
            return;
        }

        var unitCode = PurchaseUnitCodeInput.Text.Trim().ToUpperInvariant();
        if (quantity is < 1 or > 10000 || unitCode.Length is < 1 or > 20)
        {
            StatusText.Text = "تعداد باید بین ۱ تا ۱۰۰۰۰ و واحد کالا حداکثر ۲۰ نویسه باشد.";
            return;
        }

        try
        {
            var line = new PurchaseInvoiceDraftLine(product.Id, product.Name, quantity, unitCost, discountPercent, unitCode);
            _ = line.LineTotal;
            _purchaseDraftLines.Add(line);
            UpdatePurchaseDraftTotal();
            StatusText.Text = $"ردیف خرید «{product.Name}» اضافه شد.";
        }
        catch (OverflowException)
        {
            StatusText.Text = "مبلغ این ردیف از محدوده قابل پشتیبانی خارج است.";
        }
    }

    private void RemovePurchaseLine_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireLicensedModules("ویرایش پیش‌نویس خرید", "Accounting", "Inventory")) return;
        if (PurchaseDraftGrid.SelectedItem is not PurchaseInvoiceDraftLine selected)
        {
            StatusText.Text = "ابتدا یک ردیف پیش‌نویس خرید را انتخاب کنید.";
            return;
        }

        _purchaseDraftLines.Remove(selected);
        UpdatePurchaseDraftTotal();
    }

    private void UpdatePurchaseDraftTotal()
    {
        try
        {
            var total = _purchaseDraftLines.Aggregate(0m, (sum, line) => checked(sum + line.LineTotal));
            PurchaseDraftTotalText.Text = $"جمع فاکتور پیش‌نویس: {total:N2}";
        }
        catch (OverflowException)
        {
            PurchaseDraftTotalText.Text = "جمع پیش‌نویس از محدوده قابل پشتیبانی خارج است.";
        }
    }

    private async void CreatePurchaseInvoice_Click(object sender, RoutedEventArgs e)
    {
        if (_isPurchaseInvoiceInProgress) return;
        if (!HasLicensedModule("Accounting") || !HasLicensedModule("Inventory"))
        {
            StatusText.Text = "ثبت خرید به لایسنس معتبر دارای ماژول‌های Accounting و Inventory نیاز دارد.";
            return;
        }
        if (PurchaseSupplierInput.SelectedItem is not SupplierRecord supplier || !supplier.IsActive)
        {
            StatusText.Text = "یک تأمین‌کننده فعال انتخاب کنید.";
            return;
        }
        if (_purchaseDraftLines.Count == 0)
        {
            StatusText.Text = "حداقل یک ردیف به پیش‌نویس خرید اضافه کنید.";
            return;
        }

        var issueDate = DateOnly.FromDateTime(PurchaseInvoiceDateInput.SelectedDate ?? DateTime.Today);
        var dueDate = PurchaseDueDateInput.SelectedDate is DateTime selectedDueDate
            ? DateOnly.FromDateTime(selectedDueDate)
            : (DateOnly?)null;
        if (dueDate is DateOnly actualDueDate && actualDueDate < issueDate)
        {
            StatusText.Text = "سررسید فاکتور خرید نمی‌تواند قبل از تاریخ صدور باشد.";
            return;
        }
        var lines = _purchaseDraftLines.Select(line => new PurchaseInvoiceLineRequest(
            line.ProductId, line.Quantity, line.UnitCost, line.DiscountPercent / 100m, line.UnitCode)).ToArray();

        _isPurchaseInvoiceInProgress = true;
        CreatePurchaseInvoiceButton.IsEnabled = false;
        try
        {
            await using var db = new BusinessDbContext(CreateOptions());
            var invoice = await new PurchasingAccountingService(db).CreatePurchaseInvoiceAsync(
                supplier.Id, _sessionBranchId, lines, issueDate, dueDate: dueDate);
            _purchaseDraftLines.Clear();
            UpdatePurchaseDraftTotal();
            StatusText.Text = $"فاکتور خرید {invoice.InvoiceNumber} ثبت شد؛ موجودی، میانگین بهای تمام‌شده و دفتر حسابداری به‌روزرسانی شد.";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"ثبت فاکتور خرید ناموفق بود: {ex.Message}";
        }
        finally
        {
            _isPurchaseInvoiceInProgress = false;
            CreatePurchaseInvoiceButton.IsEnabled = true;
        }
    }

    private async void RefreshPurchases_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void InvoicesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        InvoiceDueDateInput.SelectedDate = InvoicesGrid.SelectedItem is InvoiceListItem selected
            ? selected.DueDate?.ToDateTime(TimeOnly.MinValue)
            : null;
    }

    private async void UpdateSelectedInvoiceDueDate_Click(object sender, RoutedEventArgs e)
    {
        if (!HasLicensedModule("Accounting"))
        {
            StatusText.Text = "برای تغییر سررسید، لایسنس معتبر دارای ماژول Accounting لازم است.";
            return;
        }
        if (InvoicesGrid.SelectedItem is not InvoiceListItem selected)
        {
            StatusText.Text = "ابتدا فاکتور فروش موردنظر را انتخاب کنید.";
            return;
        }

        var dueDate = InvoiceDueDateInput.SelectedDate is DateTime selectedDate
            ? DateOnly.FromDateTime(selectedDate)
            : (DateOnly?)null;
        if (dueDate is DateOnly actualDueDate && actualDueDate < selected.IssueDate)
        {
            StatusText.Text = "سررسید نمی‌تواند قبل از تاریخ صدور فاکتور باشد.";
            return;
        }

        try
        {
            await using var db = new BusinessDbContext(CreateOptions());
            var invoice = await db.SalesInvoices.SingleOrDefaultAsync(x => x.Id == selected.Id)
                ?? throw new KeyNotFoundException("فاکتور فروش پیدا نشد.");
            invoice.DueDate = dueDate;
            await db.SaveChangesAsync();
            StatusText.Text = dueDate is null
                ? $"سررسید فاکتور {invoice.InvoiceNumber} پاک شد؛ گزارش از تاریخ صدور به‌عنوان مبنا استفاده می‌کند."
                : $"سررسید فاکتور {invoice.InvoiceNumber} به {dueDate:yyyy-MM-dd} تغییر کرد.";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"تغییر سررسید فاکتور ناموفق بود: {ex.Message}";
        }
    }

    private void PurchaseInvoicesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateSupplierPaymentSelection();

    private void UpdateSupplierPaymentSelection()
    {
        if (PurchaseInvoicesGrid.SelectedItem is not PurchaseInvoiceGridRow selected)
        {
            SupplierPaymentSelectionText.Text = "ابتدا یک فاکتور خرید را انتخاب کنید.";
            PurchaseDueDateInput.SelectedDate = null;
            return;
        }

        PurchaseDueDateInput.SelectedDate = selected.DueDate?.ToDateTime(TimeOnly.MinValue);

        SupplierPaymentSelectionText.Text =
            $"{selected.InvoiceNumber} | مانده: {selected.OutstandingAmount:N2} {selected.CurrencyCode}";
        SupplierPaymentAmountInput.Text = selected.OutstandingAmount.ToString(CultureInfo.CurrentCulture);
    }

    private async void UpdateSelectedPurchaseDueDate_Click(object sender, RoutedEventArgs e)
    {
        if (!HasLicensedModule("Accounting") || !HasLicensedModule("Inventory"))
        {
            StatusText.Text = "برای تغییر سررسید خرید، لایسنس معتبر دارای ماژول‌های Accounting و Inventory لازم است.";
            return;
        }
        if (PurchaseInvoicesGrid.SelectedItem is not PurchaseInvoiceGridRow selected)
        {
            StatusText.Text = "ابتدا فاکتور خرید موردنظر را انتخاب کنید.";
            return;
        }

        var dueDate = PurchaseDueDateInput.SelectedDate is DateTime selectedDate
            ? DateOnly.FromDateTime(selectedDate)
            : (DateOnly?)null;
        if (dueDate is DateOnly actualDueDate && actualDueDate < selected.IssueDate)
        {
            StatusText.Text = "سررسید نمی‌تواند قبل از تاریخ صدور فاکتور خرید باشد.";
            return;
        }

        try
        {
            await using var db = new BusinessDbContext(CreateOptions());
            var invoice = await db.PurchaseInvoices.SingleOrDefaultAsync(x => x.Id == selected.Id)
                ?? throw new KeyNotFoundException("فاکتور خرید پیدا نشد.");
            invoice.DueDate = dueDate;
            await db.SaveChangesAsync();
            StatusText.Text = dueDate is null
                ? $"سررسید فاکتور خرید {invoice.InvoiceNumber} پاک شد؛ گزارش از تاریخ صدور به‌عنوان مبنا استفاده می‌کند."
                : $"سررسید فاکتور خرید {invoice.InvoiceNumber} به {dueDate:yyyy-MM-dd} تغییر کرد.";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"تغییر سررسید فاکتور خرید ناموفق بود: {ex.Message}";
        }
    }

    private async void RecordSupplierPayment_Click(object sender, RoutedEventArgs e)
    {
        if (_isSupplierPaymentInProgress) return;
        if (!HasLicensedModule("Accounting") || !HasLicensedModule("Inventory"))
        {
            StatusText.Text = "ثبت پرداخت تأمین‌کننده به لایسنس معتبر دارای ماژول‌های Accounting و Inventory نیاز دارد.";
            return;
        }
        if (PurchaseInvoicesGrid.SelectedItem is not PurchaseInvoiceGridRow selected)
        {
            StatusText.Text = "ابتدا فاکتور خرید موردنظر را انتخاب کنید.";
            return;
        }
        if (!TryParseAmount(SupplierPaymentAmountInput.Text, out var amount) || amount <= 0m)
        {
            StatusText.Text = "مبلغ پرداخت باید عددی معتبر و بزرگ‌تر از صفر باشد.";
            return;
        }
        if (SupplierPaymentMethodInput.SelectedItem is not ComboBoxItem methodItem
            || methodItem.Tag is not string method)
        {
            StatusText.Text = "روش پرداخت معتبر را انتخاب کنید.";
            return;
        }

        try
        {
            await using var db = new BusinessDbContext(CreateOptions());
            var payment = await new PurchasingAccountingService(db).RecordSupplierPaymentAsync(
                selected.Id, amount, method, SupplierPaymentReferenceInput.Text.Trim(),
                SupplierPaymentNotesInput.Text.Trim());
            SupplierPaymentReferenceInput.Clear();
            SupplierPaymentNotesInput.Clear();
            StatusText.Text = $"پرداخت {payment.Amount:N2} {payment.CurrencyCode} ثبت شد و مانده بدهی تأمین‌کننده به‌روزرسانی شد.";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"ثبت پرداخت تأمین‌کننده ناموفق بود: {ex.Message}";
        }
        finally
        {
            _isSupplierPaymentInProgress = false;
            RecordSupplierPaymentButton.IsEnabled = true;
        }
    }

    private async void AddSupplier_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireLicensedModules("افزودن تأمین‌کننده", "Core")) return;

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
        if (!RequireLicensedModules("تحویل سفارش آنلاین", "Storefront", "Accounting", "Inventory")) return;

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
        if (!RequireLicensedModules("لغو سفارش آنلاین", "Storefront")) return;

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
                if (HasLicensedOnlineFeature(OnlineOrdersInput.IsChecked == true, OnlineReservationsInput.IsChecked == true) && !await IsStorefrontRunningAsync())
                {
                    StartServer_Click(this, new RoutedEventArgs());
                    for (var attempt = 0; attempt < 10 && !await IsStorefrontRunningAsync(); attempt++)
                        await Task.Delay(500);
                }

                await using var db = new BusinessDbContext(CreateOptions());
                var settings = await db.StoreSettings.AsNoTracking().SingleAsync(x => x.Id == 1);
                if (settings.PublicPublicationEnabled
                    && HasLicensedOnlineFeature(settings.OnlineOrderingEnabled, settings.OnlineReservationsEnabled)
                    && !string.IsNullOrWhiteSpace(settings.PublicHostname))
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
        if (!RequireLicensedModules(PublicHostingStatusText, "تنظیم فایروال انتشار عمومی", "Storefront")) return;

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

    private async void RemoveFirewall_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmationDialog.Confirm(this, "حذف قانون فایروال برنامه",
            "قانون ورودی MG Business Platform برای TCP 80 و 443 حذف می‌شود. قوانین هم‌نامی که تنظیماتشان با قانون دقیق برنامه مطابقت نداشته باشد دست‌نخورده می‌مانند. ادامه می‌دهید؟",
            "حذف قانون"))
            return;

        var scriptPath = Path.Combine(AppContext.BaseDirectory, "configure-firewall.ps1");
        if (!File.Exists(scriptPath))
        {
            PublicHostingStatusText.Text = "اسکریپت مدیریت فایروال در بسته نصب پیدا نشد.";
            return;
        }

        try
        {
            PublicHostingStatusText.Text = "برای حذف قانون فایروال، تأیید دسترسی مدیر ویندوز لازم است.";
            var start = new ProcessStartInfo("powershell.exe")
            {
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -Remove",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var process = Process.Start(start);
            if (process is null)
            {
                PublicHostingStatusText.Text = "راه‌اندازی حذف قانون فایروال ناموفق بود.";
                return;
            }

            await process.WaitForExitAsync();
            PublicHostingStatusText.Text = process.ExitCode == 0
                ? "درخواست حذف قانون فایروال پایان یافت. فقط قانون دقیق برنامه قابل حذف بود."
                : $"حذف قانون فایروال با کد {process.ExitCode} ناموفق بود؛ قوانین نامنطبق حذف نشدند.";
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            PublicHostingStatusText.Text = "درخواست دسترسی مدیر لغو شد؛ فایروال تغییر نکرد.";
        }
        catch (Exception ex)
        {
            PublicHostingStatusText.Text = $"حذف قانون فایروال ناموفق بود: {ex.Message}";
        }
    }

    private async Task<(string Url, byte[] Png)> BuildStorefrontQrAsync()
    {
        if (!HasLicensedModule("Storefront"))
            throw new InvalidOperationException("برای ساخت QR فروشگاه، لایسنس معتبر دارای ماژول Storefront لازم است.");

        await using var db = new BusinessDbContext(CreateOptions());
        var settings = await db.StoreSettings.AsNoTracking().SingleAsync(x => x.Id == 1);
        if (!settings.OnlineOrderingEnabled)
            throw new InvalidOperationException("ابتدا ثبت سفارش آنلاین را فعال و تنظیمات را ذخیره کنید.");

        var hostname = settings.PublicHostname.Trim().TrimEnd('.').ToLowerInvariant();
        if (hostname.Length == 0 || Uri.CheckHostName(hostname) != UriHostNameType.Dns
            || hostname.Any(c => char.IsControl(c) || c is '{' or '}' or '"' or '\\'))
            throw new InvalidOperationException("ابتدا یک دامنه معتبر را در تنظیمات ذخیره کنید.");

        var url = $"https://{hostname}/";
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(12);
        return (url, png);
    }

    private async void GenerateStorefrontQr_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireLicensedModules(PublicHostingStatusText, "ساخت QR فروشگاه آنلاین", "Storefront")) return;

        try
        {
            var qr = await BuildStorefrontQrAsync();
            StorefrontUrlInput.Text = qr.Url;
            using var stream = new MemoryStream(qr.Png);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            StorefrontQrImage.Source = bitmap;
            StorefrontQrImage.Visibility = Visibility.Visible;
            PublicHostingStatusText.Text = "QR ساخته شد. قبل از چاپ، دامنه و HTTPS را با اینترنت موبایل آزمایش کنید.";
        }
        catch (Exception ex)
        {
            PublicHostingStatusText.Text = $"ساخت QR ناموفق بود: {ex.Message}";
        }
    }

    private async void SaveStorefrontQr_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireLicensedModules(PublicHostingStatusText, "ذخیره QR فروشگاه آنلاین", "Storefront")) return;
        try
        {
            var qr = await BuildStorefrontQrAsync();
            var dialog = new SaveFileDialog
            {
                Title = "ذخیره QR سفارش آنلاین",
                Filter = "PNG image (*.png)|*.png",
                DefaultExt = ".png",
                AddExtension = true,
                FileName = "MG-Storefront-QR.png"
            };
            if (dialog.ShowDialog(this) != true)
                return;

            await File.WriteAllBytesAsync(dialog.FileName, qr.Png);
            StorefrontUrlInput.Text = qr.Url;
            PublicHostingStatusText.Text = $"QR ذخیره شد: {dialog.FileName}. قبل از چاپ، دسترسی دامنه را با اینترنت موبایل تأیید کنید.";
        }
        catch (Exception ex)
        {
            PublicHostingStatusText.Text = $"ذخیره QR ناموفق بود: {ex.Message}";
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
                if (!settings.OnlineOrderingEnabled && !settings.OnlineReservationsEnabled)
                {
                    PublicHostingStatusText.Text = "ابتدا سفارش آنلاین یا رزرو آنلاین را در تنظیمات فعال و ذخیره کنید.";
                    return;
                }
                if (!HasLicensedOnlineFeature(settings.OnlineOrderingEnabled, settings.OnlineReservationsEnabled))
                {
                    PublicHostingStatusText.Text = "قابلیت‌های آنلاین فعال‌شده با ماژول‌های لایسنس سازگار نیستند؛ وابستگی‌های سفارش و رزرو را بررسی کنید.";
                    return;
                }
                if (!string.Equals(settings.PublicHostname, hostname, StringComparison.OrdinalIgnoreCase))
                {
                    PublicHostingStatusText.Text = "ابتدا تنظیمات فروشگاه را ذخیره کنید.";
                    return;
                }
            }

            if (_routerPortMappingService.HasSavedMappings)
            {
                try
                {
                    var routerResult = await _routerPortMappingService.ConfigureWebPortsAsync();
                    RouterPortStatusText.Text = routerResult.Message;
                }
                catch (Exception routerException)
                {
                    RouterPortStatusText.Text = $"به‌روزرسانی انتقال درگاه‌های روتر ناموفق بود؛ بررسی دستی ادامه می‌یابد: {routerException.Message}";
                }
            }

            string? dnsToken = null;
            try
            {
                var enteredDnsToken = ParsPackDnsTokenInput.Password.Trim();
                if (enteredDnsToken.Length > 0)
                {
                    WindowsCredentialStore.Save(ParsPackDnsCredentialTarget, enteredDnsToken);
                    ParsPackDnsTokenInput.Clear();
                }
                dnsToken = WindowsCredentialStore.Read(ParsPackDnsCredentialTarget);
            }
            catch (Exception credentialException)
            {
                DnsSyncStatusText.Text = $"توکن DNS قابل خواندن یا ذخیره نبود؛ بررسی دستی ادامه می‌یابد: {credentialException.Message}";
            }

            if (!string.IsNullOrWhiteSpace(dnsToken))
            {
                try
                {
                    var dnsResult = await _parsPackDnsService.SyncARecordAsync(hostname, dnsToken);
                    if (dnsResult.Status == ParsPackDnsSyncStatus.ConfirmationRequired)
                    {
                        if (!ConfirmationDialog.Confirm(this, "تغییر رکورد DNS پارس‌پک", dnsResult.Message))
                        {
                            PublicHostingStatusText.Text = "انتشار شروع نشد؛ رکورد DNS بدون تغییر باقی ماند.";
                            return;
                        }

                        dnsResult = await _parsPackDnsService.SyncARecordAsync(hostname, dnsToken, replaceExisting: true);
                    }

                    DnsSyncStatusText.Text = dnsResult.Message;
                }
                catch (Exception dnsException)
                {
                    // Continue to the normal DNS/public-IP diagnostics. Manual DNS may
                    // already be correct even if the saved provider token has expired.
                    DnsSyncStatusText.Text = $"همگام‌سازی خودکار DNS انجام نشد؛ بررسی دستی ادامه می‌یابد: {dnsException.Message}";
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

            if (_routerPortMappingService.HasSavedMappings)
            {
                try
                {
                    var routerResult = await _routerPortMappingService.RemoveWebPortsAsync();
                    RouterPortStatusText.Text = routerResult.Message;
                }
                catch (Exception routerException)
                {
                    RouterPortStatusText.Text = $"انتقال درگاه‌های روتر حذف نشد: {routerException.Message}";
                }
            }

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

    private async void ConfigureRouterPorts_Click(object sender, RoutedEventArgs e)
    {
        if (!HasLicensedModule("Storefront"))
        {
            RouterPortStatusText.Text = "برای تنظیم انتقال درگاه‌های روتر، لایسنس معتبر دارای ماژول Storefront لازم است.";
            return;
        }

        if (!ConfirmationDialog.Confirm(this, "تنظیم خودکار روتر",
            "در صورت پشتیبانی روتر از UPnP، انتقال TCP 80 و 443 به این رایانه ایجاد می‌شود و این درگاه‌ها از اینترنت قابل دسترسی خواهند بود. فقط اگر به روتر و شبکه اعتماد دارید ادامه دهید."))
            return;

        try
        {
            var result = await _routerPortMappingService.ConfigureWebPortsAsync();
            RouterPortStatusText.Text = result.Message;
        }
        catch (Exception ex)
        {
            RouterPortStatusText.Text = $"تنظیم خودکار روتر ناموفق بود: {ex.Message}";
        }
    }

    private async void RemoveRouterPorts_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmationDialog.Confirm(this, "حذف انتقال‌های روتر",
            "فقط انتقال‌هایی حذف می‌شوند که برنامه بتواند مالکیت آن‌ها را تأیید کند. انتقال‌های دستی یا متعلق به برنامه‌های دیگر دست‌نخورده می‌مانند. ادامه می‌دهید؟"))
            return;

        try
        {
            var result = await _routerPortMappingService.RemoveWebPortsAsync();
            RouterPortStatusText.Text = result.Message;
        }
        catch (Exception ex)
        {
            RouterPortStatusText.Text = $"حذف انتقال‌های روتر ناموفق بود: {ex.Message}";
        }
    }

    private bool HasLicensedModule(string module)
    {
        var payload = _machineLicenseInfo?.Validation is { IsValid: true, Payload: { } validPayload }
            ? validPayload
            : null;
        return payload?.Modules.Contains(module, StringComparer.Ordinal) == true;
    }

    private bool HasLicensedOnlineFeature(bool onlineOrderingEnabled, bool onlineReservationsEnabled)
    {
        var canServeOrders = onlineOrderingEnabled
            && HasLicensedModule("Storefront")
            && HasLicensedModule("Accounting")
            && HasLicensedModule("Inventory");
        var canServeReservations = onlineReservationsEnabled
            && HasLicensedModule("Storefront")
            && HasLicensedModule("Reservations");
        return canServeOrders || canServeReservations;
    }

    private bool RequireLicensedModules(string operation, params string[] modules) =>
        RequireLicensedModules(StatusText, operation, modules);

    private bool RequireLicensedModules(TextBlock target, string operation, params string[] modules)
    {
        var missing = modules.Where(module => !HasLicensedModule(module)).ToArray();
        if (missing.Length == 0)
            return true;

        target.Text = $"برای {operation}، لایسنس معتبر دارای ماژول‌های {string.Join("، ", missing)} لازم است.";
        return false;
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
        if (!HasLicensedOnlineFeature(OnlineOrdersInput.IsChecked == true, OnlineReservationsInput.IsChecked == true))
        {
            StatusText.Text = "هیچ قابلیت آنلاین فعال‌شده‌ای با ماژول‌های موجود در لایسنس سازگار نیست؛ وابستگی‌های سفارش و رزرو را در تنظیمات بررسی کنید.";
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
            start.Environment["MG_BUSINESS_DATA_PATH"] = _dataDirectory;
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
