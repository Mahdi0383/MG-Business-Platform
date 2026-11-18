using System.Security.Cryptography;
using System.Text;

namespace Mg.Platform.Api.Security;

// Temporary bootstrap guard for the first control-plane slice.
// Replace with OIDC + creator-role authorization before public production use.
public sealed class PlatformKeyAuthenticator(IConfiguration configuration)
{
    public bool IsConfigured => IsStronglyConfigured(configuration["Security:BootstrapPlatformKey"]);

    private static bool IsStronglyConfigured(string? key) =>
        !string.IsNullOrWhiteSpace(key) && Encoding.UTF8.GetByteCount(key) >= 32;

    public bool IsAuthorized(string? suppliedKey)
    {
        var expected = configuration["Security:BootstrapPlatformKey"];
        if (!IsStronglyConfigured(expected) || string.IsNullOrWhiteSpace(suppliedKey))
            return false;

        var expectedBytes = Encoding.UTF8.GetBytes(expected!);
        var suppliedBytes = Encoding.UTF8.GetBytes(suppliedKey);
        return expectedBytes.Length == suppliedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
}
