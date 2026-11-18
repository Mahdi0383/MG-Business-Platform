using System.Security.Cryptography.X509Certificates;

namespace Mg.Platform.Api.Provisioning;

/// <summary>Validated endpoint and mounted client certificate for the isolated agent.</summary>
public sealed class TenantProvisioningAgentOptions : IDisposable
{
    private TenantProvisioningAgentOptions(Uri address, X509Certificate2 clientCertificate)
    {
        Address = address;
        ClientCertificate = clientCertificate;
    }

    public Uri Address { get; }
    public X509Certificate2 ClientCertificate { get; }

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

        var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            fullPath, certificatePassword, X509KeyStorageFlags.EphemeralKeySet);
        if (!certificate.HasPrivateKey
            || certificate.NotBefore > DateTime.UtcNow
            || certificate.NotAfter <= DateTime.UtcNow)
        {
            certificate.Dispose();
            throw new InvalidOperationException("Provisioning agent client certificate is invalid or expired.");
        }

        return new TenantProvisioningAgentOptions(address, certificate);
    }

    public void Dispose() => ClientCertificate.Dispose();
}
