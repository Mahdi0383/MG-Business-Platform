using System.Text.RegularExpressions;

namespace Mg.Platform.Api.Contracts;

/// <summary>
/// Canonical, URL-safe tenant identifier. Display names remain Unicode; slugs do not.
/// </summary>
public static partial class BusinessSlug
{
    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,78}[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return Pattern().IsMatch(normalized);
    }
}
