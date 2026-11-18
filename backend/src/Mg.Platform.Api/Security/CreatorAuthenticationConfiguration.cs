namespace Mg.Platform.Api.Security;

/// <summary>
/// Validated OIDC settings for the MG Creator control plane.
/// Production tokens are accepted only from an HTTPS issuer and for the configured API audience.
/// </summary>
public sealed record CreatorAuthenticationConfiguration(Uri Authority, string Audience)
{
    public static CreatorAuthenticationConfiguration From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var authorityValue = configuration["Authentication:Authority"];
        var audience = configuration["Authentication:Audience"];

        if (!Uri.TryCreate(authorityValue, UriKind.Absolute, out var authority)
            || authority.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(authority.Host)
            || !string.IsNullOrEmpty(authority.UserInfo)
            || !string.IsNullOrEmpty(authority.Query)
            || !string.IsNullOrEmpty(authority.Fragment))
        {
            throw new InvalidOperationException(
                "Authentication:Authority must be an absolute HTTPS OIDC issuer URL.");
        }

        if (string.IsNullOrWhiteSpace(audience)
            || audience.Length > 200
            || audience.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
        {
            throw new InvalidOperationException(
                "Authentication:Audience must be configured and contain no more than 200 characters.");
        }

        return new CreatorAuthenticationConfiguration(authority, audience.Trim());
    }
}
