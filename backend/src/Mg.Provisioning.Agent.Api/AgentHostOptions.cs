using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace Mg.Provisioning.Agent.Api;

public sealed class AgentHostOptions : IDisposable
{
    private static readonly Regex ThumbprintPattern = new("^[A-Fa-f0-9]{40}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Oid ClientAuthenticationOid = new("1.3.6.1.5.5.7.3.2");
    private static readonly Oid ServerAuthenticationOid = new("1.3.6.1.5.5.7.3.1");

    private readonly HashSet<string> allowedClientThumbprints;

    private AgentHostOptions(int port, X509Certificate2 serverCertificate, HashSet<string> allowedThumbprints)
    {
        Port = port;
        ServerCertificate = serverCertificate;
        allowedClientThumbprints = allowedThumbprints;
    }

    public int Port { get; }
    public X509Certificate2 ServerCertificate { get; }

    public static AgentHostOptions From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var portValue = configuration["Agent:ListenPort"];
        var certificatePath = configuration["Agent:ServerCertificatePath"];
        var certificatePassword = configuration["Agent:ServerCertificatePassword"];
        var thumbprintsValue = configuration["Agent:AllowedClientThumbprints"];

        if (!int.TryParse(portValue, out var port) || port is < 1024 or > 65535)
            throw new InvalidOperationException("Agent listen port must be between 1024 and 65535.");
        if (string.IsNullOrWhiteSpace(certificatePath) || string.IsNullOrWhiteSpace(certificatePassword))
            throw new InvalidOperationException("Agent HTTPS server certificate and its runtime password are required.");
        if (string.IsNullOrWhiteSpace(thumbprintsValue))
            throw new InvalidOperationException("At least one explicit mTLS client certificate thumbprint must be configured.");

        var rawThumbprints = thumbprintsValue
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var allowed = rawThumbprints.Select(NormalizeThumbprint).ToArray();
        if (allowed.Length == 0 || allowed.Any(value => value is null))
            throw new InvalidOperationException("The mTLS client certificate allowlist contains an invalid thumbprint.");
        var allowedSet = allowed.Select(value => value!).ToHashSet(StringComparer.Ordinal);

        var path = Path.GetFullPath(certificatePath);
        if (!File.Exists(path))
            throw new InvalidOperationException("Agent HTTPS server certificate file was not found.");

        var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            path, certificatePassword, X509KeyStorageFlags.EphemeralKeySet);
        if (!IsValidServerCertificate(certificate, DateTime.UtcNow))
        {
            certificate.Dispose();
            throw new InvalidOperationException("Agent HTTPS server certificate is invalid, expired or lacks required extensions.");
        }

        return new AgentHostOptions(port, certificate, allowedSet);
    }

    public bool ValidateClientCertificate(
        X509Certificate2 certificate,
        X509Chain? chain,
        SslPolicyErrors policyErrors)
    {
        if (policyErrors != SslPolicyErrors.None || chain is null)
            return false;

        if (chain.ChainStatus.Any(status => status.Status != X509ChainStatusFlags.NoError))
            return false;

        var thumbprint = NormalizeThumbprint(certificate.Thumbprint);
        return thumbprint is not null
            && allowedClientThumbprints.Contains(thumbprint)
            && IsValidIdentityCertificate(certificate, ClientAuthenticationOid, DateTime.UtcNow);
    }

    private static bool IsValidServerCertificate(X509Certificate2 certificate, DateTime utcNow) =>
        certificate.HasPrivateKey
        && IsValidIdentityCertificate(certificate, ServerAuthenticationOid, utcNow);

    private static bool IsValidIdentityCertificate(X509Certificate2 certificate, Oid requiredEku, DateTime utcNow)
    {
        var hasRequiredEku = certificate.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .Any(extension => extension.EnhancedKeyUsages.Cast<Oid>().Any(oid => oid.Value == requiredEku.Value));
        var isNonCa = certificate.Extensions
            .OfType<X509BasicConstraintsExtension>()
            .Any(extension => !extension.CertificateAuthority);
        var hasDigitalSignature = certificate.Extensions
            .OfType<X509KeyUsageExtension>()
            .Any(extension => extension.KeyUsages.HasFlag(X509KeyUsageFlags.DigitalSignature));

        return hasRequiredEku && isNonCa && hasDigitalSignature
            && certificate.NotBefore.ToUniversalTime() <= utcNow
            && certificate.NotAfter.ToUniversalTime() > utcNow;
    }

    private static string? NormalizeThumbprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        return ThumbprintPattern.IsMatch(normalized) ? normalized : null;
    }

    public void Dispose() => ServerCertificate.Dispose();
}
