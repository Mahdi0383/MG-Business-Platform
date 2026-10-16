namespace MG.BusinessPlatform.Infrastructure.Services;

public sealed record DnsZoneTarget(string Zone, string RecordHost);

/// <summary>
/// Resolves a fully-qualified host name to the longest matching managed DNS zone
/// and the provider-relative record host. It does not make network requests.
/// </summary>
public static class DnsZoneResolver
{
    public static DnsZoneTarget Resolve(string hostname, IEnumerable<string> availableZones)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostname);
        ArgumentNullException.ThrowIfNull(availableZones);

        var host = Normalize(hostname);
        if (Uri.CheckHostName(host) != UriHostNameType.Dns
            || host.Any(c => char.IsControl(c) || c is '/' or '\\' or ':' or '@' or '*' or '?' or '#'))
            throw new ArgumentException("Enter a plain DNS hostname without a scheme, port, path or wildcard.", nameof(hostname));

        var zones = availableZones
            .Where(zone => !string.IsNullOrWhiteSpace(zone))
            .Select(Normalize)
            .Where(zone => Uri.CheckHostName(zone) == UriHostNameType.Dns)
            .Where(zone => host.Equals(zone, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + zone, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(zone => zone.Length)
            .ToArray();

        if (zones.Length == 0)
            throw new InvalidOperationException("No managed DNS zone matches this hostname.");

        var selectedZone = zones[0];
        var recordHost = host.Equals(selectedZone, StringComparison.OrdinalIgnoreCase)
            ? "@"
            : host[..^(selectedZone.Length + 1)];

        return new DnsZoneTarget(selectedZone, recordHost);
    }

    private static string Normalize(string value) => value.Trim().TrimEnd('.').ToLowerInvariant();
}
