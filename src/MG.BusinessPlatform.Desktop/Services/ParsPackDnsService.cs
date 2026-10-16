using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MG.BusinessPlatform.Infrastructure.Services;

namespace MG.BusinessPlatform.Desktop.Services;

public enum ParsPackDnsSyncStatus
{
    Updated,
    AlreadyCurrent,
    ConfirmationRequired
}

public sealed record ParsPackDnsSyncResult(
    ParsPackDnsSyncStatus Status,
    string Message,
    string Zone,
    string RecordHost,
    string DesiredIpv4,
    string? ExistingIpv4 = null);

/// <summary>
/// Manages a single A record through the official ParsPack CDN DNS API.
/// It never logs or returns the API token and never changes a record without
/// explicit confirmation when the existing address differs.
/// </summary>
public sealed class ParsPackDnsService
{
    private static readonly Uri BaseUri = new("https://my.parspack.com/cdnapi/");
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<ParsPackDnsSyncResult> SyncARecordAsync(
        string hostname,
        string apiToken,
        bool replaceExisting = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiToken);
        if (apiToken.Trim().Length > 1200)
            throw new ArgumentException("ParsPack API token is longer than the supported limit.", nameof(apiToken));

        var normalizedHostname = hostname.Trim().TrimEnd('.').ToLowerInvariant();
        var publicIp = await GetPublicIpv4Async(cancellationToken);
        var zonesResponse = await SendAsync<ZonesResponse>(
            HttpMethod.Get, "external/api/v1/zones", apiToken.Trim(), null, cancellationToken);
        var zones = zonesResponse.Data
            .Where(zone => !string.IsNullOrWhiteSpace(zone.TargetDomain) && !string.IsNullOrWhiteSpace(zone.Uuid))
            .ToArray();

        var target = DnsZoneResolver.Resolve(normalizedHostname, zones.Select(zone => zone.TargetDomain));
        var zone = zones.First(item => string.Equals(
            item.TargetDomain.Trim().TrimEnd('.'), target.Zone, StringComparison.OrdinalIgnoreCase));
        var recordsResponse = await SendAsync<DnsRecordsResponse>(
            HttpMethod.Get,
            $"external/api/v2/zones/{Uri.EscapeDataString(zone.Uuid)}/dns-records",
            apiToken.Trim(), null, cancellationToken);

        var hostRecords = recordsResponse.Data
            .Where(record => string.Equals(record.Host.Trim().TrimEnd('.'), target.RecordHost, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var existing = hostRecords
            .Where(record => string.Equals(record.Type, "A", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (existing.Length == 0)
        {
            if (hostRecords.Any(record => string.Equals(record.Type, "CNAME", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("A CNAME record already exists for this host. DNS does not allow an A record and CNAME at the same name; review the record manually before changing it.");
            await CreateARecordAsync(zone.Uuid, target.RecordHost, publicIp.ToString(), apiToken.Trim(), 300, cancellationToken);
            return new(ParsPackDnsSyncStatus.Updated,
                $"رکورد A برای {normalizedHostname} در پارس‌پک ساخته شد و به {publicIp} اشاره می‌کند. انتشار DNS ممکن است زمان ببرد.",
                target.Zone, target.RecordHost, publicIp.ToString());
        }

        if (existing.Length != 1 || existing[0].Records.Count != 1)
            throw new InvalidOperationException(
                "برای این نام بیش از یک رکورد A یا چند مقدار وجود دارد. برای جلوگیری از حذف تنظیمات توازن بار، رکوردها را دستی بررسی کنید.");

        var currentRecord = existing[0];
        var currentValue = currentRecord.Records[0];
        if (IPAddress.TryParse(currentValue.Content, out var currentIp)
            && currentIp.Equals(publicIp)
            && string.Equals(currentRecord.Proxy, "direct", StringComparison.OrdinalIgnoreCase))
        {
            return new(ParsPackDnsSyncStatus.AlreadyCurrent,
                $"رکورد A دامنه {normalizedHostname} از قبل به IP عمومی فعلی ({publicIp}) اشاره می‌کند.",
                target.Zone, target.RecordHost, publicIp.ToString(), currentValue.Content);
        }

        var oldIp = currentValue.Content;
        if (!replaceExisting)
        {
            return new(ParsPackDnsSyncStatus.ConfirmationRequired,
                $"رکورد فعلی {target.RecordHost}.{target.Zone} به {oldIp} اشاره می‌کند و IP فعلی این اتصال {publicIp} است. با تأیید شما رکورد A جایگزین و حالت Proxy روی direct تنظیم می‌شود.",
                target.Zone, target.RecordHost, publicIp.ToString(), oldIp);
        }

        await DeleteARecordAsync(zone.Uuid, currentRecord, currentValue, apiToken.Trim(), cancellationToken);
        try
        {
            await CreateARecordAsync(zone.Uuid, target.RecordHost, publicIp.ToString(), apiToken.Trim(), 300, cancellationToken);
        }
        catch (Exception updateException)
        {
            try
            {
                await CreateARecordAsync(zone.Uuid, target.RecordHost, oldIp, apiToken.Trim(),
                    currentRecord.Ttl is >= 60 and <= 86400 ? currentRecord.Ttl : 300,
                    cancellationToken, currentRecord.Proxy, currentValue);
            }
            catch (Exception rollbackException)
            {
                throw new InvalidOperationException(
                    $"ParsPack rejected the new DNS record and the previous record could not be restored automatically. New record error: {updateException.Message}; restore error: {rollbackException.Message}",
                    updateException);
            }

            throw new InvalidOperationException(
                $"ParsPack rejected the new DNS record; the previous record was restored. {updateException.Message}",
                updateException);
        }

        return new(ParsPackDnsSyncStatus.Updated,
            $"رکورد A دامنه {normalizedHostname} به IP فعلی ({publicIp}) به‌روزرسانی شد. انتشار DNS ممکن است زمان ببرد.",
            target.Zone, target.RecordHost, publicIp.ToString(), oldIp);
    }

    private static async Task<IPAddress> GetPublicIpv4Async(CancellationToken cancellationToken)
    {
        var response = (await Http.GetStringAsync("https://api.ipify.org", cancellationToken)).Trim();
        if (!IPAddress.TryParse(response, out var address)
            || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
            || IsNonPublicIpv4(address))
            throw new InvalidOperationException("نتوانستم یک IP عمومی IPv4 معتبر برای این اتصال تشخیص بدهم.");

        return address;
    }

    private static bool IsNonPublicIpv4(IPAddress address)
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
            || (a == 192 && b == 0 && c is 0 or 2)
            || (a == 198 && b is 18 or 19)
            || (a == 198 && b == 51 && c == 100)
            || (a == 203 && b == 0 && c == 113);
    }

    private static Task<ApiResponse> DeleteARecordAsync(
        string zoneUuid, DnsRecordDto record, DnsRecordValueDto value, string token, CancellationToken cancellationToken)
    {
        var body = new
        {
            host = record.Host,
            type = record.Type,
            record = new
            {
                content = value.Content,
                port = value.Port,
                weight = value.Weight,
                priority = value.Priority,
                flags = value.Flags,
                tag = value.Tag
            }
        };
        return SendAsync<ApiResponse>(
            HttpMethod.Delete,
            $"external/api/v2/zones/{Uri.EscapeDataString(zoneUuid)}/dns-records",
            token, body, cancellationToken);
    }

    private static Task<ApiResponse> CreateARecordAsync(
        string zoneUuid, string host, string address, string token, int ttl,
        CancellationToken cancellationToken, string proxy = "direct", DnsRecordValueDto? priorValue = null)
    {
        var body = new
        {
            host,
            type = "A",
            ttl,
            proxy,
            load_balance_id = (string?)null,
            record = new
            {
                content = address,
                port = priorValue?.Port,
                weight = priorValue?.Weight,
                priority = priorValue?.Priority,
                flags = priorValue?.Flags,
                tag = priorValue?.Tag
            }
        };
        return SendAsync<ApiResponse>(
            HttpMethod.Post,
            $"external/api/v2/zones/{Uri.EscapeDataString(zoneUuid)}/dns-records",
            token, body, cancellationToken);
    }

    private static async Task<TResponse> SendAsync<TResponse>(
        HttpMethod method, string relativePath, string token, object? body, CancellationToken cancellationToken)
        where TResponse : ApiResponse
    {
        using var request = new HttpRequestMessage(method, new Uri(BaseUri, relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
            request.Content = JsonContent.Create(body, options: JsonOptions);

        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"ParsPack DNS API returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");

        var parsed = JsonSerializer.Deserialize<TResponse>(json, JsonOptions)
            ?? throw new InvalidDataException("ParsPack DNS API returned an empty response.");
        if (!parsed.Success)
        {
            var message = string.IsNullOrWhiteSpace(parsed.Message) ? "No error details were provided." : parsed.Message.Trim();
            if (message.Length > 300)
                message = message[..300];
            throw new InvalidOperationException($"ParsPack DNS API rejected the request: {message}");
        }

        return parsed;
    }

    private class ApiResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    private sealed class ZonesResponse : ApiResponse
    {
        public List<ZoneDto> Data { get; set; } = [];
    }

    private sealed class DnsRecordsResponse : ApiResponse
    {
        public List<DnsRecordDto> Data { get; set; } = [];
    }

    private sealed class ZoneDto
    {
        public string Uuid { get; set; } = string.Empty;
        [JsonPropertyName("target_domain")]
        public string TargetDomain { get; set; } = string.Empty;
    }

    private sealed class DnsRecordDto
    {
        public string Host { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Proxy { get; set; } = string.Empty;
        public int Ttl { get; set; }
        public List<DnsRecordValueDto> Records { get; set; } = [];
    }

    private sealed class DnsRecordValueDto
    {
        public string Content { get; set; } = string.Empty;
        public int? Port { get; set; }
        public int? Weight { get; set; }
        public int? Priority { get; set; }
        public int? Flags { get; set; }
        public string? Tag { get; set; }
    }
}
