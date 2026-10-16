using System.Xml;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace MG.BusinessPlatform.Infrastructure.Services;

public sealed record RouterPortMappingResult(bool Success, string Message);

/// <summary>
/// Best-effort UPnP IGD port forwarding for TCP 80/443. The user must explicitly
/// approve opening these ports. The service only removes mappings it can verify
/// were created for this application and this PC.
/// </summary>
public sealed class RouterPortMappingService
{
    private const string MappingDescription = "MG Business Platform";
    private const int DiscoveryPort = 1900;
    private static readonly IPAddress SsdpMulticastAddress = IPAddress.Parse("239.255.255.250");
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(5)
    };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly string _statePath;

    public RouterPortMappingService(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        Directory.CreateDirectory(dataDirectory);
        _statePath = Path.Combine(Path.GetFullPath(dataDirectory), "upnp-port-mappings.json");
    }

    public bool HasSavedMappings => File.Exists(_statePath);

    public async Task<RouterPortMappingResult> ConfigureWebPortsAsync(CancellationToken cancellationToken = default)
    {
        var network = GetDefaultGatewayAndLocalAddress();
        if (network is null)
            return new(false, "درگاه پیش‌فرض و IP محلی این رایانه پیدا نشد؛ انتقال درگاه روتر را دستی تنظیم کنید.");

        RouterContext router;
        try
        {
            router = await DiscoverRouterAsync(network, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or InvalidOperationException or IOException or System.Xml.XmlException or OperationCanceledException)
        {
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
                throw;
            return new(false, $"روتر سازگار با UPnP پیدا نشد یا پاسخ نداد: {ex.Message}. امکان تنظیم دستی انتقال TCP 80 و 443 همچنان وجود دارد.");
        }

        var state = new RouterMappingState
        {
            GatewayAddress = network.Gateway.ToString(),
            LocalAddress = network.LocalAddress.ToString(),
            ServiceType = router.ServiceType,
            ControlUrl = router.ControlUrl.ToString()
        };
        var createdThisRun = new List<int>();

        foreach (var port in new[] { 80, 443 })
        {
            try
            {
                var existing = await GetSpecificMappingAsync(router, port, cancellationToken);
                if (existing is not null)
                {
                    if (!string.Equals(existing.InternalClient, network.LocalAddress.ToString(), StringComparison.OrdinalIgnoreCase)
                        || existing.InternalPort != port)
                    {
                        await RollbackCreatedMappingsAsync(router, network.LocalAddress, createdThisRun, cancellationToken);
                        return new(false, $"درگاه TCP {port} از قبل به دستگاه دیگری هدایت شده است. برای جلوگیری از تغییر تنظیمات موجود، هیچ تغییری روی آن انجام نشد.");
                    }

                    var owned = string.Equals(existing.Description, MappingDescription, StringComparison.Ordinal);
                    state.Ports.Add(new RouterPortState { Port = port, OwnedByApplication = owned });
                    continue;
                }

                var addResult = await AddMappingAsync(router, network.LocalAddress, port, leaseDuration: 0, cancellationToken);
                if (!addResult.Success && addResult.ErrorCode == "718")
                {
                    // Another client may have created the mapping between our lookup and AddPortMapping.
                    var racedMapping = await GetSpecificMappingAsync(router, port, cancellationToken);
                    if (racedMapping is not null
                        && string.Equals(racedMapping.InternalClient, network.LocalAddress.ToString(), StringComparison.OrdinalIgnoreCase)
                        && racedMapping.InternalPort == port)
                    {
                        state.Ports.Add(new RouterPortState
                        {
                            Port = port,
                            OwnedByApplication = string.Equals(racedMapping.Description, MappingDescription, StringComparison.Ordinal)
                        });
                        continue;
                    }
                }

                if (!addResult.Success)
                {
                    await RollbackCreatedMappingsAsync(router, network.LocalAddress, createdThisRun, cancellationToken);
                    var detail = string.IsNullOrWhiteSpace(addResult.ErrorDescription)
                        ? $"UPnP error {addResult.ErrorCode ?? "unknown"}"
                        : addResult.ErrorDescription;
                    return new(false, $"تنظیم انتقال TCP {port} ناموفق بود: {detail}. ممکن است UPnP در روتر غیرفعال باشد یا روتر به انتقال دائمی اجازه ندهد.");
                }

                createdThisRun.Add(port);
                state.Ports.Add(new RouterPortState { Port = port, OwnedByApplication = true });
            }
            catch (Exception ex) when (ex is HttpRequestException or SocketException or InvalidOperationException or IOException or System.Xml.XmlException)
            {
                await RollbackCreatedMappingsAsync(router, network.LocalAddress, createdThisRun, cancellationToken);
                return new(false, $"تنظیم انتقال درگاه‌های روتر ناموفق بود: {ex.Message}");
            }
        }

        await File.WriteAllTextAsync(_statePath, JsonSerializer.Serialize(state, JsonOptions), cancellationToken);
        var manual = state.Ports.Any(port => !port.OwnedByApplication);
        return new(true, manual
            ? "درگاه‌های TCP 80 و 443 به این رایانه هدایت می‌شوند؛ انتقال‌هایی که قبلاً دستی ساخته شده بودند، تحت مالکیت برنامه قرار نگرفتند."
            : "انتقال خودکار TCP 80 و 443 با UPnP تنظیم شد. دسترسی عمومی همچنان به DNS، محدودیت‌های ISP و نبود CGNAT وابسته است.");
    }

    public async Task<RouterPortMappingResult> RemoveWebPortsAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_statePath))
            return new(true, "هیچ انتقال درگاهی که توسط برنامه ثبت شده باشد پیدا نشد.");

        RouterMappingState? state;
        try
        {
            state = JsonSerializer.Deserialize<RouterMappingState>(
                await File.ReadAllTextAsync(_statePath, cancellationToken), JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new(false, $"فایل وضعیت انتقال درگاه‌ها قابل خواندن نیست و برای ایمنی حذف نشد: {ex.Message}");
        }

        if (state is null || state.Ports is null
            || state.Ports.Any(port => port is null || port.Port is not (80 or 443))
            || !IPAddress.TryParse(state.GatewayAddress, out var savedGateway)
            || !IPAddress.TryParse(state.LocalAddress, out var savedLocalAddress))
            return new(false, "فایل وضعیت انتقال درگاه‌ها معتبر نیست؛ برای جلوگیری از حذف اشتباه، تنظیمات روتر دست‌نخورده ماند.");

        var network = GetDefaultGatewayAndLocalAddress();
        if (network is null || !network.Gateway.Equals(savedGateway) || !network.LocalAddress.Equals(savedLocalAddress))
            return new(false, "شبکه یا IP محلی از زمان ایجاد انتقال‌ها تغییر کرده است. انتقال‌ها به‌صورت خودکار حذف نشدند تا تنظیمات دستگاه اشتباهی تغییر نکند.");

        RouterContext router;
        try
        {
            router = await DiscoverRouterAsync(network, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or InvalidOperationException or IOException or System.Xml.XmlException or OperationCanceledException)
        {
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
                throw;
            return new(false, $"روتر در دسترس نیست؛ انتقال‌های ثبت‌شده حذف نشدند: {ex.Message}");
        }

        if (!string.Equals(router.ServiceType, state.ServiceType, StringComparison.Ordinal))
            return new(false, "سرویس UPnP روتر از زمان ایجاد انتقال‌ها تغییر کرده است؛ برای ایمنی انتقال‌ها حذف نشدند.");

        foreach (var portState in state.Ports.Where(port => port.OwnedByApplication))
        {
            var existing = await GetSpecificMappingAsync(router, portState.Port, cancellationToken);
            if (existing is null)
                continue;
            if (!string.Equals(existing.Description, MappingDescription, StringComparison.Ordinal)
                || !string.Equals(existing.InternalClient, savedLocalAddress.ToString(), StringComparison.OrdinalIgnoreCase)
                || existing.InternalPort != portState.Port)
                return new(false, $"مالکیت انتقال TCP {portState.Port} دیگر قابل تأیید نیست؛ برنامه آن را حذف نکرده است.");

            var result = await DeleteMappingAsync(router, portState.Port, cancellationToken);
            if (!result.Success)
                return new(false, $"حذف انتقال TCP {portState.Port} ناموفق بود: {result.ErrorDescription ?? result.ErrorCode ?? "خطای نامشخص"}");
        }

        File.Delete(_statePath);
        return new(true, "انتقال‌های UPnP که توسط MG Business Platform ایجاد شده بودند حذف شدند؛ انتقال‌های دستی یا متعلق به برنامه‌های دیگر دست‌نخورده ماندند.");
    }

    private static NetworkInfo? GetDefaultGatewayAndLocalAddress()
    {
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up)
                continue;

            var properties = adapter.GetIPProperties();
            var gateways = properties.GatewayAddresses
                .Select(item => item.Address)
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork
                    && !IPAddress.IsLoopback(address)
                    && IsPrivateNetworkAddress(address))
                .ToArray();
            var local = properties.UnicastAddresses
                .Select(item => item.Address)
                .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork
                    && !IPAddress.IsLoopback(address)
                    && !IsAutomaticPrivateAddress(address));
            if (gateways.Length > 0 && local is not null)
                return new NetworkInfo(gateways[0], local);
        }

        return null;
    }

    private static bool IsAutomaticPrivateAddress(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 169 && bytes[1] == 254;
    }

    private static bool IsPrivateNetworkAddress(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168)
            || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127);
    }

    private static async Task<RouterContext> DiscoverRouterAsync(NetworkInfo network, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        var request = Encoding.ASCII.GetBytes(
            "M-SEARCH * HTTP/1.1\r\n" +
            "HOST: 239.255.255.250:1900\r\n" +
            "MAN: \"ssdp:discover\"\r\n" +
            "MX: 2\r\n" +
            "ST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n\r\n");
        await udp.SendAsync(request, new IPEndPoint(SsdpMulticastAddress, DiscoveryPort), timeout.Token);

        while (!timeout.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await udp.ReceiveAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (!received.RemoteEndPoint.Address.Equals(network.Gateway))
                continue;

            var responseText = Encoding.ASCII.GetString(received.Buffer);
            var locationLine = responseText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(line => line.StartsWith("LOCATION:", StringComparison.OrdinalIgnoreCase));
            if (locationLine is null)
                continue;

            var locationText = locationLine["LOCATION:".Length..].Trim();
            if (!Uri.TryCreate(locationText, UriKind.Absolute, out var location)
                || location.Scheme != Uri.UriSchemeHttp
                || !IPAddress.TryParse(location.Host, out var locationAddress)
                || !locationAddress.Equals(network.Gateway))
                continue;

            var description = await Http.GetStringAsync(location, timeout.Token);
            var document = ParseXmlSafely(description);
            foreach (var service in document.Descendants().Where(element => element.Name.LocalName == "service"))
            {
                var serviceType = service.Elements().FirstOrDefault(element => element.Name.LocalName == "serviceType")?.Value;
                var controlUrl = service.Elements().FirstOrDefault(element => element.Name.LocalName == "controlURL")?.Value;
                if (!IsSupportedServiceType(serviceType) || string.IsNullOrWhiteSpace(controlUrl))
                    continue;

                var controlUri = new Uri(location, controlUrl);
                if (controlUri.Scheme != Uri.UriSchemeHttp
                    || !IPAddress.TryParse(controlUri.Host, out var controlAddress)
                    || !controlAddress.Equals(network.Gateway))
                    continue;

                return new RouterContext(network.Gateway, network.LocalAddress, serviceType!, controlUri);
            }
        }

        throw new InvalidOperationException("The router did not advertise a compatible WANIPConnection/WANPPPConnection service.");
    }

    private static bool IsSupportedServiceType(string? serviceType) =>
        serviceType is not null
        && new[]
        {
            "urn:schemas-upnp-org:service:WANIPConnection:1",
            "urn:schemas-upnp-org:service:WANIPConnection:2",
            "urn:schemas-upnp-org:service:WANIPConnection:3",
            "urn:schemas-upnp-org:service:WANPPPConnection:1",
            "urn:schemas-upnp-org:service:WANPPPConnection:2",
            "urn:schemas-upnp-org:service:WANPPPConnection:3"
        }.Contains(serviceType, StringComparer.OrdinalIgnoreCase);

    private static XDocument ParseXmlSafely(string content)
    {
        using var stringReader = new StringReader(content);
        using var reader = XmlReader.Create(stringReader, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 1_000_000
        });
        return XDocument.Load(reader);
    }

    private static async Task<PortMappingInfo?> GetSpecificMappingAsync(RouterContext router, int port, CancellationToken cancellationToken)
    {
        var result = await SendSoapAsync(router, "GetSpecificPortMappingEntry",
        [
            ("NewRemoteHost", string.Empty),
            ("NewExternalPort", port.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("NewProtocol", "TCP")
        ], cancellationToken);

        if (!result.Success && result.ErrorCode == "714")
            return null;
        if (!result.Success)
            throw new InvalidOperationException(result.ErrorDescription ?? $"UPnP error {result.ErrorCode ?? "unknown"}.");

        var document = result.Document
            ?? throw new InvalidOperationException("The router returned an empty port-mapping response.");
        var internalPortText = ReadElement(document, "NewInternalPort");
        var internalClient = ReadElement(document, "NewInternalClient");
        if (!int.TryParse(internalPortText, out var internalPort) || string.IsNullOrWhiteSpace(internalClient))
            throw new InvalidDataException("The router returned an invalid port-mapping entry.");

        return new PortMappingInfo(internalPort, internalClient, ReadElement(document, "NewPortMappingDescription") ?? string.Empty);
    }

    private static Task<SoapResult> AddMappingAsync(
        RouterContext router, IPAddress localAddress, int port, int leaseDuration, CancellationToken cancellationToken) =>
        SendSoapAsync(router, "AddPortMapping",
        [
            ("NewRemoteHost", string.Empty),
            ("NewExternalPort", port.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("NewProtocol", "TCP"),
            ("NewInternalPort", port.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("NewInternalClient", localAddress.ToString()),
            ("NewEnabled", "1"),
            ("NewPortMappingDescription", MappingDescription),
            ("NewLeaseDuration", leaseDuration.ToString(System.Globalization.CultureInfo.InvariantCulture))
        ], cancellationToken);

    private static Task<SoapResult> DeleteMappingAsync(RouterContext router, int port, CancellationToken cancellationToken) =>
        SendSoapAsync(router, "DeletePortMapping",
        [
            ("NewRemoteHost", string.Empty),
            ("NewExternalPort", port.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("NewProtocol", "TCP")
        ], cancellationToken);

    private static async Task RollbackCreatedMappingsAsync(
        RouterContext router, IPAddress localAddress, IEnumerable<int> ports, CancellationToken cancellationToken)
    {
        foreach (var port in ports.Reverse())
        {
            try
            {
                var mapping = await GetSpecificMappingAsync(router, port, cancellationToken);
                if (mapping is not null
                    && mapping.InternalPort == port
                    && string.Equals(mapping.InternalClient, localAddress.ToString(), StringComparison.OrdinalIgnoreCase)
                    && string.Equals(mapping.Description, MappingDescription, StringComparison.Ordinal))
                    await DeleteMappingAsync(router, port, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or SocketException or InvalidOperationException or IOException or System.Xml.XmlException)
            {
                // Preserve the original failure; the UI still offers manual router management.
            }
        }
    }

    private static async Task<SoapResult> SendSoapAsync(
        RouterContext router, string action, IReadOnlyList<(string Name, string Value)> arguments, CancellationToken cancellationToken)
    {
        XNamespace soap = "http://schemas.xmlsoap.org/soap/envelope/";
        XNamespace service = router.ServiceType;
        var envelope = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(soap + "Envelope",
                new XAttribute(soap + "encodingStyle", "http://schemas.xmlsoap.org/soap/encoding/"),
                new XElement(soap + "Body",
                    new XElement(service + action,
                        arguments.Select(argument => new XElement(service + argument.Name, argument.Value))))));

        using var request = new HttpRequestMessage(HttpMethod.Post, router.ControlUrl);
        request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{router.ServiceType}#{action}\"");
        request.Content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml");
        using var response = await Http.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        XDocument? document = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(content))
                document = ParseXmlSafely(content);
        }
        catch (System.Xml.XmlException)
        {
            if (response.IsSuccessStatusCode)
                return new SoapResult(true, null, null, null);
            return new SoapResult(false, null, response.ReasonPhrase, null);
        }

        var errorCode = document?.Descendants().FirstOrDefault(element => element.Name.LocalName == "errorCode")?.Value;
        var errorDescription = document?.Descendants().FirstOrDefault(element => element.Name.LocalName == "errorDescription")?.Value;
        var success = response.IsSuccessStatusCode && string.IsNullOrWhiteSpace(errorCode);
        return new SoapResult(success, errorCode, errorDescription, document);
    }

    private static string? ReadElement(XDocument document, string localName) =>
        document.Descendants().FirstOrDefault(element => element.Name.LocalName == localName)?.Value;

    private sealed record NetworkInfo(IPAddress Gateway, IPAddress LocalAddress);
    private sealed record RouterContext(IPAddress Gateway, IPAddress LocalAddress, string ServiceType, Uri ControlUrl);
    private sealed record PortMappingInfo(int InternalPort, string InternalClient, string Description);
    private sealed record SoapResult(bool Success, string? ErrorCode, string? ErrorDescription, XDocument? Document);

    private sealed class RouterMappingState
    {
        public RouterMappingState() { }
        public string GatewayAddress { get; set; } = string.Empty;
        public string LocalAddress { get; set; } = string.Empty;
        public string ServiceType { get; set; } = string.Empty;
        public string ControlUrl { get; set; } = string.Empty;
        public List<RouterPortState> Ports { get; set; } = [];
    }

    private sealed class RouterPortState
    {
        public RouterPortState() { }
        public int Port { get; set; }
        public bool OwnedByApplication { get; set; }
    }
}
