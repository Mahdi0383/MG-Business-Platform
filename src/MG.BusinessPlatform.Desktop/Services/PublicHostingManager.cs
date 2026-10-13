using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace MG.BusinessPlatform.Desktop.Services;

public sealed record PublicationResult(bool Success, string Message);

/// <summary>
/// Manages a local Caddy reverse proxy. Public reachability still depends on correct DNS,
/// ISP/router port forwarding and the network allowing inbound HTTP/HTTPS.
/// </summary>
public sealed class PublicHostingManager
{
    private static readonly HttpClient ProbeClient = new() { Timeout = TimeSpan.FromSeconds(4) };
    private readonly string _applicationDirectory;
    private readonly string _dataDirectory;
    private readonly string _pidPath;
    private readonly string _configPath;
    private readonly string _logPath;
    private Process? _process;

    public PublicHostingManager(string applicationDirectory, string dataDirectory)
    {
        _applicationDirectory = Path.GetFullPath(applicationDirectory);
        _dataDirectory = Path.GetFullPath(dataDirectory);
        Directory.CreateDirectory(_dataDirectory);
        _pidPath = Path.Combine(_dataDirectory, "caddy.pid");
        _configPath = Path.Combine(_dataDirectory, "Caddyfile");
        _logPath = Path.Combine(_dataDirectory, "caddy.log");
    }

    public async Task<PublicationResult> StartAsync(string hostname, CancellationToken cancellationToken = default)
    {
        hostname = NormalizeHostname(hostname);
        if (hostname.Length == 0 || Uri.CheckHostName(hostname) != UriHostNameType.Dns
            || hostname.Any(c => char.IsControl(c) || c is '{' or '}' or '"' or '\\'))
            return new(false, "دامنه معتبر نیست. فقط نام میزبان مانند shop.example.com وارد کنید.");

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(hostname, cancellationToken);
            if (addresses.Length == 0)
                return new(false, "برای دامنه هیچ رکورد DNS پیدا نشد.");
            if (addresses.Any(IsNonPublicAddress))
                return new(false, "حداقل یکی از رکوردهای A/AAAA به IP خصوصی، محلی یا رزروشده اشاره می‌کند. دامنه باید به IP عمومی قابل‌دسترسی اشاره کند.");
        }
        catch (SocketException)
        {
            return new(false, "دامنه در DNS پیدا نشد. رکورد A/AAAA را تنظیم کنید و دوباره تلاش کنید.");
        }

        var dnsDiagnostic = string.Empty;
        var dnsIpv4Addresses = addresses.Where(address => address.AddressFamily == AddressFamily.InterNetwork).ToArray();
        if (dnsIpv4Addresses.Length > 0)
        {
            var publicIpv4 = await TryGetPublicIpv4Async(cancellationToken);
            if (publicIpv4 is not null && !dnsIpv4Addresses.Contains(publicIpv4))
                return new(false, $"رکورد A دامنه با IP عمومی فعلی این اتصال هم‌خوان نیست. IP عمومی فعلی: {publicIpv4}؛ رکوردهای دامنه: {string.Join(", ", dnsIpv4Addresses.Select(address => address.ToString()))}. ابتدا DNS را اصلاح کنید.");
            if (publicIpv4 is null)
                dnsDiagnostic = " نتوانستم IP عمومی را خودکار بررسی کنم؛ رکورد A را با IP عمومی فعلی اینترنت خود تطبیق دهید.";
        }

        var caddyPath = FindCaddyExecutable();
        if (caddyPath is null)
            return new(false, "فایل caddy.exe همراه برنامه پیدا نشد؛ بسته انتشار ناقص است.");

        await TryAttachAsync(caddyPath, cancellationToken);
        if (_process is { HasExited: false })
            return new(true, "سرویس انتشار عمومی از قبل در حال اجراست.");
        if (!IsTcpPortAvailable(80) || !IsTcpPortAvailable(443))
            return new(false, "درگاه 80 یا 443 توسط برنامه دیگری اشغال شده است. IIS یا سرویس وب دیگری را بررسی کنید.");

        var safeLogPath = _logPath.Replace('\\', '/');
        var config = $"""
{{
    admin 127.0.0.1:2019
    log {{
        output file "{safeLogPath}"
        format json
    }}
}}

{hostname} {{
    encode zstd gzip
    header {{
        X-Content-Type-Options "nosniff"
        Referrer-Policy "strict-origin-when-cross-origin"
        X-Frame-Options "DENY"
    }}
    reverse_proxy 127.0.0.1:8080
}}
""";
        await File.WriteAllTextAsync(_configPath, config, cancellationToken);

        var validation = await RunCaddyCommandAsync(caddyPath, ["validate", "--config", _configPath, "--adapter", "caddyfile"], cancellationToken);
        if (validation.ExitCode != 0)
            return new(false, $"تنظیمات HTTPS معتبر نیست: {validation.Output}");

        var start = new ProcessStartInfo(caddyPath)
        {
            WorkingDirectory = _dataDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("run");
        start.ArgumentList.Add("--config");
        start.ArgumentList.Add(_configPath);
        start.ArgumentList.Add("--adapter");
        start.ArgumentList.Add("caddyfile");
        start.Environment["XDG_DATA_HOME"] = Path.Combine(_dataDirectory, "caddy-data");
        start.Environment["XDG_CONFIG_HOME"] = Path.Combine(_dataDirectory, "caddy-config");

        try
        {
            _process = Process.Start(start);
            if (_process is null)
                return new(false, "راه‌اندازی Caddy ناموفق بود.");
            await File.WriteAllTextAsync(_pidPath, _process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
            await Task.Delay(700, cancellationToken);
            if (_process.HasExited)
            {
                var log = await ReadLogTailAsync(cancellationToken);
                _process.Dispose();
                _process = null;
                return new(false, $"Caddy بلافاصله متوقف شد. بررسی کنید درگاه‌های 80 و 443 آزاد باشند. {log}");
            }

            return new(true, $"سرویس HTTPS شروع شد؛ دسترسی عمومی هنوز تأیید نشده است.{dnsDiagnostic} DNS و انتقال درگاه‌های TCP 80 و 443 روتر را بررسی کنید.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return new(false, $"راه‌اندازی سرویس HTTPS ناموفق بود: {ex.Message}");
        }
    }

    public async Task<PublicationResult> StopAsync(CancellationToken cancellationToken = default)
    {
        var caddyPath = FindCaddyExecutable();
        if (caddyPath is null)
            return new(false, "فایل Caddy پیدا نشد؛ امکان تأیید مالکیت پردازش وجود ندارد.");

        await TryAttachAsync(caddyPath, cancellationToken);
        if (_process is not { HasExited: false })
        {
            if (File.Exists(_pidPath))
                File.Delete(_pidPath);
            return new(true, "سرویس HTTPS متوقف است.");
        }

        try
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync(cancellationToken);
            _process.Dispose();
            _process = null;
            if (File.Exists(_pidPath))
                File.Delete(_pidPath);
            return new(true, "سرویس HTTPS متوقف شد.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return new(false, $"توقف سرویس HTTPS ناموفق بود: {ex.Message}");
        }
    }

    public async Task<PublicationResult> CheckPublicHttpsAsync(string hostname, CancellationToken cancellationToken = default)
    {
        hostname = NormalizeHostname(hostname);
        if (Uri.CheckHostName(hostname) != UriHostNameType.Dns)
            return new(false, "دامنه معتبر نیست.");

        try
        {
            using var response = await ProbeClient.GetAsync($"https://{hostname}/health/live", cancellationToken);
            if (response.IsSuccessStatusCode)
                return new(true, "پاسخ HTTPS معتبر از دامنه دریافت شد.");
            return new(false, $"دامنه پاسخ داد اما بررسی سلامت موفق نبود (HTTP {(int)response.StatusCode}).");
        }
        catch (HttpRequestException ex)
        {
            return new(false, $"HTTPS از این رایانه تأیید نشد: {ex.Message}. ممکن است انتقال درگاه روتر یا NAT loopback پشتیبانی نشود؛ از اینترنت موبایل نیز آزمایش کنید.");
        }
        catch (TaskCanceledException)
        {
            return new(false, "بررسی HTTPS به پایان زمان مجاز رسید؛ دسترسی عمومی هنوز تأیید نشده است.");
        }
    }

    public async Task<string> ReadLogTailAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_logPath))
            return "هنوز گزارش Caddy ثبت نشده است.";
        var lines = await File.ReadAllLinesAsync(_logPath, cancellationToken);
        return string.Join(Environment.NewLine, lines.TakeLast(25));
    }

    private static async Task<IPAddress?> TryGetPublicIpv4Async(CancellationToken cancellationToken)
    {
        try
        {
            var text = (await ProbeClient.GetStringAsync("https://api.ipify.org", cancellationToken)).Trim();
            return IPAddress.TryParse(text, out var address)
                && address.AddressFamily == AddressFamily.InterNetwork
                && !IsNonPublicAddress(address)
                ? address
                : null;
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
    }

    private static bool IsNonPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            return IsNonPublicAddress(address.MapToIPv4());

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            var a = bytes[0];
            var b = bytes[1];
            var c = bytes[2];
            return a == 0 || a == 10 || a == 127 || a >= 224
                || (a == 100 && b is >= 64 and <= 127)
                || (a == 169 && b == 254)
                || (a == 172 && b is >= 16 and <= 31)
                || (a == 192 && b == 168)
                || (a == 192 && b == 0 && c == 0)
                || (a == 192 && b == 0 && c == 2)
                || (a == 198 && b is 18 or 19)
                || (a == 198 && b == 51 && c == 100)
                || (a == 203 && b == 0 && c == 113);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            var isGlobalUnicast = (bytes[0] & 0xE0) == 0x20;
            var isUniqueLocal = (bytes[0] & 0xFE) == 0xFC;
            return !isGlobalUnicast || isUniqueLocal || address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal || address.IsIPv6Multicast || IPAddress.IsLoopback(address);
        }

        return true;
    }

    private static bool IsTcpPortAvailable(int port)
    {
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            listener?.Stop();
        }
    }

    private string? FindCaddyExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(_applicationDirectory, "caddy", "caddy.exe"),
            Path.Combine(_applicationDirectory, "caddy.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private async Task TryAttachAsync(string expectedExecutable, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_process is { HasExited: false } || !File.Exists(_pidPath))
            return;

        try
        {
            var text = await File.ReadAllTextAsync(_pidPath, cancellationToken);
            if (!int.TryParse(text, out var pid))
            {
                File.Delete(_pidPath);
                return;
            }

            var candidate = Process.GetProcessById(pid);
            var actualPath = candidate.MainModule?.FileName;
            if (actualPath is not null && string.Equals(Path.GetFullPath(actualPath), Path.GetFullPath(expectedExecutable), StringComparison.OrdinalIgnoreCase))
                _process = candidate;
            else
                candidate.Dispose();
        }
        catch (ArgumentException)
        {
            if (File.Exists(_pidPath))
                File.Delete(_pidPath);
        }
        catch (System.ComponentModel.Win32Exception) { }
        catch (InvalidOperationException) { }
    }

    private static async Task<(int ExitCode, string Output)> RunCaddyCommandAsync(
        string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start);
        if (process is null)
            return (-1, "پردازش اجرا نشد.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, $"{await stdout} {await stderr}".Trim());
    }

    private static string NormalizeHostname(string hostname) => hostname.Trim().TrimEnd('.').ToLowerInvariant();
}
