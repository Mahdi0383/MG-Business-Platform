using System.Security.Cryptography.X509Certificates;

namespace Mg.Platform.Api.Provisioning;

/// <summary>Non-secret endpoint and mounted client-certificate settings for the isolated agent.</summary>
public sealed class TenantProvisioningAgentOptions
{
    private TenantProvisioningAgentOptions(Uri address, string certificatePath, string certificatePassword)
    {
        Address = address;
        ClientCertificatePath = certificatePath;
        ClientCertificatePassword = certificatePassword;
    }

    public Uri Address { get; }
    public string ClientCertificatePath { get; }
    public string ClientCertificatePassword { get; }

    public static TenantProvisioningAgentOptions? From(IConfiguration configuration)
    {
        var addressValue = configuration["Provisioning:Agent:Address"];
        var certificatePath = configuration["Provisioning:Agent:ClientCertificatePath"];
        var certificatePassword = configuration["Provisioning:Agent:ClientCertificatePassword"];

        if (string.IsNullOrWhiteSpace(addressValue)
            && string.IsNullOrWhiteSpace(certificatePath)
            && string.IsNullOrWhiteSpace(certificatePassword))
            return null;

        if (!Uri.TryCreate(addressValue, UriKind.Absolute, out var address)
            || address.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(address.UserInfo)
            || !string.IsNullOrEmpty(address.Query)
            || !string.IsNullOrEmpty(address.Fragment)
            || string.IsNullOrWhiteSpace(certificatePath)
            || string.IsNullOrWhiteSpace(certificatePassword))
            throw new InvalidOperationException(
                "Provisioning agent requires an HTTPS address and a mounted client certificate with its secret password.");

        var fullPath = Path.GetFullPath(certificatePath);
        if (!File.Exists(fullPath))
            throw new InvalidOperationException("Provisioning agent client certificate file was not found.");

        using var certificate = new X509Certificate2(
            fullPath, certificatePassword, X509KeyStorageFlags.EphemeralKeySet);
        if (!certificate.HasPrivateKey
            || certificate.NotBefore > DateTime.UtcNow
            || certificate.NotAfter <= DateTime.UtcNow)
            throw new InvalidOperationException("Provisioning agent client certificate is invalid or expired.");

        return new TenantProvisioningAgentOptions(address, fullPath, certificatePassword);
    }
}
