using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MG.BusinessPlatform.Infrastructure.Services;

public enum PaymentConnectionType
{
    Manual = 0,
    TcpIp = 1,
    Serial = 2,
    UsbVirtualCom = 3,
    VendorLocalApi = 4,
    WindowsService = 5,
    VendorSdk = 6
}

public sealed record PaymentTerminalProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public string Vendor { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string DriverId { get; init; } = "manual";
    public string ProtocolVersion { get; init; } = string.Empty;
    public PaymentConnectionType ConnectionType { get; init; } = PaymentConnectionType.Manual;
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 0;
    public string SerialPort { get; init; } = string.Empty;
    public int BaudRate { get; init; } = 9600;
    public int DataBits { get; init; } = 8;
    public string Parity { get; init; } = "None";
    public string StopBits { get; init; } = "One";
    public int ConnectTimeoutMs { get; init; } = 3000;
    public int ResponseTimeoutMs { get; init; } = 5000;
    public string CurrencyCode { get; init; } = "IRR";
    public string MerchantId { get; init; } = string.Empty;
    public string TerminalId { get; init; } = string.Empty;
    public string Endpoint { get; init; } = string.Empty;
    public string AuthenticationReference { get; init; } = string.Empty;
    public bool IsEnabled { get; init; } = true;
    public bool IsDefault { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastTestedAtUtc { get; init; }
    public string LastTestStatus { get; init; } = "NotTested";
    public string LastTestMessage { get; init; } = string.Empty;
}

public sealed record PaymentConnectionTestResult(
    bool ConfigurationValid,
    bool TransportConnected,
    string Status,
    string Message,
    DateTimeOffset TestedAtUtc);

/// <summary>
/// Stores non-secret terminal profiles locally. Credentials are represented only by an
/// opaque Windows Credential Manager/DPAPI reference; secret material must never be stored here.
/// Connection tests only validate configuration or open a TCP socket and never initiate a payment.
/// </summary>
public sealed class PaymentTerminalProfileService(string profileFilePath)
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly string _profileFilePath = Path.GetFullPath(profileFilePath);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<PaymentTerminalProfile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_profileFilePath))
                return [];
            if (new FileInfo(_profileFilePath).Length > 5 * 1024 * 1024)
                throw new InvalidDataException("The local PC-POS profile file exceeds the 5 MB safety limit.");
            await using var stream = new FileStream(_profileFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var profiles = await JsonSerializer.DeserializeAsync<List<PaymentTerminalProfile>>(stream, JsonOptions, cancellationToken)
                ?? [];
            if (profiles.Any(profile => profile.Id == Guid.Empty)
                || profiles.Select(profile => profile.Id).Distinct().Count() != profiles.Count)
                throw new InvalidDataException("A payment-terminal profile has an invalid or duplicate identifier.");
            foreach (var profile in profiles)
            {
                try { Validate(profile); }
                catch (ArgumentException ex) { throw new InvalidDataException($"Stored payment-terminal profile '{profile.Name}' is invalid: {ex.Message}", ex); }
            }
            return profiles.OrderByDescending(profile => profile.IsDefault).ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(PaymentTerminalProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Validate(profile);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var profiles = new List<PaymentTerminalProfile>();
            if (File.Exists(_profileFilePath))
            {
                await using var source = new FileStream(_profileFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                profiles = await JsonSerializer.DeserializeAsync<List<PaymentTerminalProfile>>(source, JsonOptions, cancellationToken) ?? [];
            }

            if (profiles.Any(existing => existing.Id != profile.Id
                && string.Equals(existing.Name.Trim(), profile.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("A payment-terminal profile with this name already exists.");

            var now = DateTimeOffset.UtcNow;
            var prior = profiles.SingleOrDefault(existing => existing.Id == profile.Id);
            var normalized = profile with
            {
                Name = profile.Name.Trim(),
                Vendor = profile.Vendor.Trim(),
                Model = profile.Model.Trim(),
                DriverId = profile.DriverId.Trim(),
                ProtocolVersion = profile.ProtocolVersion.Trim(),
                Host = profile.Host.Trim(),
                SerialPort = profile.SerialPort.Trim().ToUpperInvariant(),
                CurrencyCode = profile.CurrencyCode.Trim().ToUpperInvariant(),
                MerchantId = profile.MerchantId.Trim(),
                TerminalId = profile.TerminalId.Trim(),
                Endpoint = profile.Endpoint.Trim(),
                AuthenticationReference = profile.AuthenticationReference.Trim(),
                CreatedAtUtc = prior?.CreatedAtUtc ?? profile.CreatedAtUtc,
                UpdatedAtUtc = now
            };

            if (normalized.IsDefault)
                profiles = profiles.Select(existing => existing with { IsDefault = false }).ToList();

            var index = profiles.FindIndex(existing => existing.Id == normalized.Id);
            if (index >= 0)
                profiles[index] = normalized;
            else
            {
                if (profiles.Count >= 200)
                    throw new InvalidOperationException("A maximum of 200 PC-POS profiles can be saved.");
                profiles.Add(normalized);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_profileFilePath)!);
            var temporaryPath = _profileFilePath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
                {
                    await JsonSerializer.SerializeAsync(destination, profiles, JsonOptions, cancellationToken);
                    await destination.FlushAsync(cancellationToken);
                }
                File.Move(temporaryPath, _profileFilePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        if (profileId == Guid.Empty)
            throw new ArgumentException("Profile ID is required.", nameof(profileId));
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_profileFilePath))
                return;
            await using var source = new FileStream(_profileFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var profiles = await JsonSerializer.DeserializeAsync<List<PaymentTerminalProfile>>(source, JsonOptions, cancellationToken) ?? [];
            profiles.RemoveAll(profile => profile.Id == profileId);
            var temporaryPath = _profileFilePath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
                {
                    await JsonSerializer.SerializeAsync(destination, profiles, JsonOptions, cancellationToken);
                    await destination.FlushAsync(cancellationToken);
                }
                File.Move(temporaryPath, _profileFilePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ExportAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var destination = Path.GetFullPath(destinationPath);
        if (string.Equals(destination, _profileFilePath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Export destination must differ from the active profile file.", nameof(destinationPath));

        var profiles = await LoadAsync(cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporaryPath = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, profiles, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporaryPath, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    /// <summary>
    /// Replaces the local profile list after validating the complete import file. Profile files
    /// contain no credentials; only opaque references to separately managed secrets are allowed.
    /// </summary>
    public async Task ImportAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var source = Path.GetFullPath(sourcePath);
        if (string.Equals(source, _profileFilePath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Import source must differ from the active profile file.", nameof(sourcePath));
        if (!File.Exists(source))
            throw new FileNotFoundException("PC-POS profile file was not found.", source);
        if (new FileInfo(source).Length > 5 * 1024 * 1024)
            throw new InvalidDataException("The imported PC-POS profile file exceeds the 5 MB safety limit.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            List<PaymentTerminalProfile> profiles;
            await using (var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                profiles = await JsonSerializer.DeserializeAsync<List<PaymentTerminalProfile>>(stream, JsonOptions, cancellationToken)
                    ?? throw new InvalidDataException("The imported PC-POS profile file is empty or invalid.");

            if (profiles.Count is < 1 or > 200)
                throw new InvalidDataException("An import file must contain between 1 and 200 PC-POS profiles.");
            foreach (var profile in profiles)
                Validate(profile);
            if (profiles.Select(profile => profile.Id).Distinct().Count() != profiles.Count)
                throw new InvalidDataException("The imported profile file contains duplicate identifiers.");
            if (profiles.Select(profile => profile.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != profiles.Count)
                throw new InvalidDataException("The imported profile file contains duplicate profile names.");
            if (profiles.Count(profile => profile.IsDefault) > 1)
                throw new InvalidDataException("Only one profile can be marked as default.");

            var temporaryPath = _profileFilePath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_profileFilePath)!);
                await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
                {
                    await JsonSerializer.SerializeAsync(destination, profiles, JsonOptions, cancellationToken);
                    await destination.FlushAsync(cancellationToken);
                }
                File.Move(temporaryPath, _profileFilePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PaymentConnectionTestResult> TestConnectionAsync(
        PaymentTerminalProfile profile, CancellationToken cancellationToken = default)
    {
        var testedAt = DateTimeOffset.UtcNow;
        try
        {
            Validate(profile);
        }
        catch (ArgumentException ex)
        {
            return new(false, false, "InvalidConfiguration", ex.Message, testedAt);
        }

        if (profile.ConnectionType == PaymentConnectionType.TcpIp)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(profile.ConnectTimeoutMs);
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(profile.Host, profile.Port, timeout.Token);
                return new(true, true, "TransportConnected",
                    "اتصال TCP برقرار شد؛ این نتیجه فقط دسترسی شبکه را تأیید می‌کند، نه پشتیبانی پروتکل، تأیید پرداخت یا آماده‌بودن کارت‌خوان.",
                    DateTimeOffset.UtcNow);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                var message = ex is OperationCanceledException && !cancellationToken.IsCancellationRequested
                    ? "مهلت اتصال شبکه تمام شد. هیچ پرداختی ارسال نشده است."
                    : $"اتصال شبکه برقرار نشد: {ex.Message}";
                return new(true, false, ex is OperationCanceledException ? "Timeout" : "ConnectionFailed", message, DateTimeOffset.UtcNow);
            }
        }

        var guidance = profile.ConnectionType switch
        {
            PaymentConnectionType.Manual => "پروفایل دستی معتبر است؛ هیچ ارتباط سخت‌افزاری آزمایش نشد و ثبت پرداخت نیازمند تأیید اپراتور است.",
            PaymentConnectionType.Serial or PaymentConnectionType.UsbVirtualCom =>
                $"نام درگاه {profile.SerialPort} و تنظیمات سریال معتبر است؛ تبادل داده انجام نشد. برای ارتباط واقعی باید درایور/پروتکل مستند همان مدل اضافه شود.",
            PaymentConnectionType.VendorLocalApi =>
                "نشانی و پیکربندی API معتبر است؛ درخواست شبکه ارسال نشد تا از اجرای ناخواسته عملیات وابسته به فروشنده جلوگیری شود.",
            PaymentConnectionType.WindowsService =>
                "پروفایل سرویس ویندوز ذخیره‌پذیر است؛ وجود سرویس و پروتکل آن بدون اتصال‌دهنده اختصاصی تأیید نشده است.",
            PaymentConnectionType.VendorSdk =>
                "پروفایل SDK فروشنده ذخیره‌پذیر است؛ نصب SDK و سازگاری مدل تا زمان افزودن آداپتور اختصاصی تأیید نشده است.",
            _ => "نوع اتصال ناشناخته است."
        };
        return new(true, false, "DriverRequired", guidance, DateTimeOffset.UtcNow);
    }

    public static void Validate(PaymentTerminalProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Id == Guid.Empty) throw new ArgumentException("Profile ID is required.", nameof(profile));
        RequireLength(profile.Name, 1, 100, nameof(profile.Name));
        RequireLength(profile.Vendor, 0, 100, nameof(profile.Vendor));
        RequireLength(profile.Model, 0, 100, nameof(profile.Model));
        RequireLength(profile.DriverId, 1, 100, nameof(profile.DriverId));
        RequireLength(profile.ProtocolVersion, 0, 80, nameof(profile.ProtocolVersion));
        RequireLength(profile.MerchantId, 0, 100, nameof(profile.MerchantId));
        RequireLength(profile.TerminalId, 0, 100, nameof(profile.TerminalId));
        RequireLength(profile.AuthenticationReference, 0, 200, nameof(profile.AuthenticationReference));
        RequireLength(profile.Host, 0, 253, nameof(profile.Host));
        RequireLength(profile.SerialPort, 0, 20, nameof(profile.SerialPort));
        RequireLength(profile.Endpoint, 0, 2048, nameof(profile.Endpoint));
        RequireLength(profile.Parity, 1, 20, nameof(profile.Parity));
        RequireLength(profile.StopBits, 1, 20, nameof(profile.StopBits));
        RequireLength(profile.LastTestStatus, 1, 40, nameof(profile.LastTestStatus));
        RequireLength(profile.LastTestMessage, 0, 1000, nameof(profile.LastTestMessage));
        if (!Enum.IsDefined(profile.ConnectionType)) throw new ArgumentOutOfRangeException(nameof(profile.ConnectionType));
        if (string.IsNullOrWhiteSpace(profile.CurrencyCode) || profile.CurrencyCode.Length != 3
            || profile.CurrencyCode.Any(c => c is < 'A' or > 'Z'))
            throw new ArgumentException("Currency must be a three-letter uppercase code.", nameof(profile.CurrencyCode));
        if (profile.ConnectTimeoutMs is < 250 or > 120000) throw new ArgumentOutOfRangeException(nameof(profile.ConnectTimeoutMs));
        if (profile.ResponseTimeoutMs is < 250 or > 120000) throw new ArgumentOutOfRangeException(nameof(profile.ResponseTimeoutMs));
        if (profile.Port is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(profile.Port));
        if (profile.BaudRate is < 300 or > 4000000) throw new ArgumentOutOfRangeException(nameof(profile.BaudRate));
        if (profile.DataBits is < 5 or > 8) throw new ArgumentOutOfRangeException(nameof(profile.DataBits));
        if (profile.Parity is not ("None" or "Even" or "Odd" or "Mark" or "Space"))
            throw new ArgumentException("Unsupported serial parity.", nameof(profile.Parity));
        if (profile.StopBits is not ("One" or "OnePointFive" or "Two"))
            throw new ArgumentException("Unsupported serial stop bits.", nameof(profile.StopBits));

        if (profile.ConnectionType == PaymentConnectionType.TcpIp)
        {
            RequireLength(profile.Host, 1, 253, nameof(profile.Host));
            if (Uri.CheckHostName(profile.Host) == UriHostNameType.Unknown && !IPAddress.TryParse(profile.Host, out _))
                throw new ArgumentException("TCP host must be a valid DNS name or IP address.", nameof(profile.Host));
            if (profile.Port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(profile.Port));
        }
        if (profile.ConnectionType is PaymentConnectionType.Serial or PaymentConnectionType.UsbVirtualCom
            && (string.IsNullOrWhiteSpace(profile.SerialPort)
                || !System.Text.RegularExpressions.Regex.IsMatch(profile.SerialPort, @"^COM[1-9][0-9]{0,3}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
            throw new ArgumentException("Serial port must use a name such as COM3.", nameof(profile.SerialPort));
        if (profile.ConnectionType == PaymentConnectionType.VendorLocalApi)
        {
            if (!Uri.TryCreate(profile.Endpoint, UriKind.Absolute, out var endpoint)
                || endpoint.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(endpoint.UserInfo))
                throw new ArgumentException("Vendor API endpoint must be an absolute HTTP(S) URL without embedded credentials.", nameof(profile.Endpoint));
        }
    }

    private static void RequireLength(string? value, int minimum, int maximum, string parameterName)
    {
        if (value is null || value.Trim().Length < minimum || value.Trim().Length > maximum)
            throw new ArgumentException($"Value must contain between {minimum} and {maximum} characters.", parameterName);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
