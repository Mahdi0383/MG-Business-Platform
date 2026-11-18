using System.Text.RegularExpressions;

namespace Mg.Platform.Api.Secrets;

public sealed record VaultSecretStoreOptions(Uri Address, string Token, string Mount)
{
    private static readonly Regex MountPattern = new("^[A-Za-z0-9_-]{1,64}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static VaultSecretStoreOptions From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var addressValue = configuration["Secrets:Vault:Address"];
        var token = configuration["Secrets:Vault:Token"];
        var mount = configuration["Secrets:Vault:Mount"] ?? "secret";

        if (!Uri.TryCreate(addressValue, UriKind.Absolute, out var address)
            || address.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(address.Host)
            || address.UserInfo.Length != 0
            || address.Query.Length != 0
            || address.Fragment.Length != 0
            || address.AbsolutePath != "/")
        {
            throw new InvalidOperationException(
                "Secrets:Vault:Address must be an HTTPS origin without credentials, path, query or fragment.");
        }

        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096
            || token.Any(char.IsControl))
        {
            throw new InvalidOperationException("Secrets:Vault:Token must be configured securely.");
        }

        if (!MountPattern.IsMatch(mount))
            throw new InvalidOperationException("Secrets:Vault:Mount contains invalid characters.");

        return new VaultSecretStoreOptions(address, token, mount);
    }
}
