using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using Microsoft.Win32;
using MG.BusinessPlatform.Licensing;

namespace MG.BusinessPlatform.LicenseManager;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ExpiryDateInput.Text = DateOnly.FromDateTime(DateTime.Today.AddYears(1)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private void BrowsePrivateKey_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "انتخاب کلید خصوصی", Filter = "PEM files (*.pem)|*.pem|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) == true) PrivateKeyPathInput.Text = dialog.FileName;
    }

    private void CopySoftwareSerial_Click(object sender, RoutedEventArgs e) => CopyText(SoftwareSerialInput.Text);
    private void CopyHardwareSerial_Click(object sender, RoutedEventArgs e) => CopyText(HardwareSerialInput.Text);

    private void CopyText(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            Clipboard.SetText(value.Trim());
    }

    private void BrowseLicenseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "ذخیره لایسنس امضاشده",
            FileName = "license.json",
            Filter = "JSON files (*.json)|*.json",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            LicenseOutputPathInput.Text = dialog.FileName;
            LicenseToVerifyPathInput.Text = dialog.FileName;
        }
    }

    private void IssueLicense_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var privateKeyPath = RequirePath(PrivateKeyPathInput.Text, "کلید خصوصی");
            var softwareSerial = RequireText(SoftwareSerialInput.Text, "سریال نرم‌افزار");
            var hardwareSerial = RequireText(HardwareSerialInput.Text, "سریال سخت‌افزار");
            var customerName = RequireText(CustomerNameInput.Text, "نام مشتری");
            var outputPath = Path.GetFullPath(RequireText(LicenseOutputPathInput.Text, "مسیر خروجی لایسنس"));
            var expiryDate = DateOnly.ParseExact(ExpiryDateInput.Text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!int.TryParse(MaxBranchesInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var maxBranches))
                throw new ArgumentException("تعداد شعبه باید عدد صحیح مثبت باشد.");
            if (!int.TryParse(MaxUsersInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var maxUsers))
                throw new ArgumentException("تعداد کاربران باید عدد صحیح مثبت باشد.");

            var modules = ModulesInput.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var expiry = new DateTimeOffset(expiryDate.ToDateTime(new TimeOnly(TimeSpan.TicksPerDay - 1), DateTimeKind.Utc));
            var payload = new LicensePayload
            {
                LicenseId = Guid.NewGuid(),
                CustomerName = customerName,
                SoftwareSerial = softwareSerial,
                HardwareSerial = hardwareSerial,
                IssuedAtUtc = DateTimeOffset.UtcNow,
                ExpiresAtUtc = expiry,
                LicenseType = "Full",
                Modules = modules,
                MaxBranches = maxBranches,
                MaxUsers = maxUsers
            }.Validate();

            var privateKeyPem = File.ReadAllText(privateKeyPath);
            var trustedPublicKeyPem = LicenseCryptography.GetTrustedPublicKeyPem();
            if (!LicenseCryptography.PrivateKeyMatchesPublicKey(privateKeyPem, trustedPublicKeyPem))
                throw new CryptographicException("کلید خصوصی انتخاب‌شده با کلید ناشر تعبیه‌شده در برنامه مطابقت ندارد.");
            var envelope = LicenseCryptography.Sign(payload, privateKeyPem);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            using (var stream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
                writer.Write(LicenseCryptography.SerializeEnvelope(envelope));
            LicenseToVerifyPathInput.Text = outputPath;
            SetStatus($"لایسنس صادر شد. شناسه: {payload.LicenseId}\nمشتری: {payload.CustomerName}\nانقضا (UTC): {payload.ExpiresAtUtc:yyyy-MM-dd}\nماژول‌ها: {string.Join(", ", payload.Modules)}", true);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or FormatException or CryptographicException)
        {
            SetStatus($"صدور لایسنس ناموفق بود: {ex.Message}", false);
        }
    }

    private void BrowseLicenseVerify_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "انتخاب فایل لایسنس", Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) == true) LicenseToVerifyPathInput.Text = dialog.FileName;
    }

    private void VerifyLicense_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var licensePath = RequirePath(LicenseToVerifyPathInput.Text, "فایل لایسنس");
            var trustedPublicKey = LicenseCryptography.GetTrustedPublicKeyPem();
            var envelope = LicenseCryptography.DeserializeEnvelope(File.ReadAllText(licensePath));
            var result = LicenseCryptography.Verify(envelope, trustedPublicKey,
                SoftwareSerialInput.Text.Trim(), HardwareSerialInput.Text.Trim(), DateTimeOffset.UtcNow);
            var details = result.Payload is null ? string.Empty :
                $"\nمشتری: {result.Payload.CustomerName}\nشناسه: {result.Payload.LicenseId}\nانقضا: {result.Payload.ExpiresAtUtc:yyyy-MM-dd} UTC\nماژول‌ها: {string.Join(", ", result.Payload.Modules)}";
            SetStatus($"{result.Status}: {result.Message}{details}", result.IsValid);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or FormatException or CryptographicException or System.Text.Json.JsonException or InvalidOperationException)
        {
            SetStatus($"بررسی لایسنس ناموفق بود: {ex.Message}", false);
        }
    }

    private static string RequireText(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"فیلد «{label}» الزامی است.");
        return value.Trim();
    }

    private static string RequirePath(string value, string label)
    {
        var path = RequireText(value, label);
        if (!File.Exists(path))
            throw new FileNotFoundException($"فایل «{label}» پیدا نشد.", path);
        return Path.GetFullPath(path);
    }

    private void SetStatus(string message, bool success)
    {
        StatusText.Text = message;
        StatusText.Foreground = success ? System.Windows.Media.Brushes.DarkGreen : System.Windows.Media.Brushes.DarkRed;
    }
}
