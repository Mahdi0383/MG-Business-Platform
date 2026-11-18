using System.Security.Cryptography;
using System.Text;

namespace Mg.Platform.Api.Security;

// Temporary bootstrap guard for the first control-plane slice.
// Replace with OIDC + creator-role authorization before public production use.
public sealed class PlatformKeyAuthenticator(IConfiguration configuration)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["Security:BootstrapPlatformKey"]);

    public bool IsAuthorized(string? suppliedKey)
    {
        var expected = configuration["Security:BootstrapPlatformKey"];
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(suppliedKey))
            return false;

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var suppliedBytes = Encoding.UTF8.GetBytes(suppliedKey);
        return expectedBytes.Length == suppliedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
}
