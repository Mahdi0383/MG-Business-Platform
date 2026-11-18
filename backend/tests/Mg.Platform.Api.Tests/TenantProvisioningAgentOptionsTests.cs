using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Mg.Platform.Api.Provisioning;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Mg.Platform.Api.Tests;

public sealed class TenantProvisioningAgentOptionsTests
{
    [Fact]
    public void ReturnsNullWhenAgentIsNotConfigured()
    {
        var configuration = new ConfigurationBuilder().Build();
        Assert.Null(TenantProvisioningAgentOptions.From(configuration));
    }

    [Fact]
    public void RejectsNonHttpsAgentAddress()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Provisioning:Agent:Address"] = "http://provisioner.internal/",
                ["Provisioning:Agent:ClientCertificatePath"] = "/not/read.pfx",
                ["Provisioning:Agent:ClientCertificatePassword"] = "not-a-real-secret"
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            TenantProvisioningAgentOptions.From(configuration));
    }

    [Fact]
    public void RejectsCertificateWithoutClientAuthenticationEku()
    {
        const string password = "test-only-pfx-password";
        var path = Path.Combine(Path.GetTempPath(), $"mg-agent-server-cert-{Guid.NewGuid():N}.pfx");
        CreateClientCertificate(path, password, "1.3.6.1.5.5.7.3.1");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Provisioning:Agent:Address"] = "https://provisioner.internal/",
                    ["Provisioning:Agent:ClientCertificatePath"] = path,
                    ["Provisioning:Agent:ClientCertificatePassword"] = password
                })
                .Build();

            Assert.Throws<InvalidOperationException>(() =>
                TenantProvisioningAgentOptions.From(configuration));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void CreateClientCertificate(string path, string password, string ekuOid)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=mg-agent-test-client", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        var usages = new OidCollection();
        usages.Add(new Oid(ekuOid));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, critical: true));
        using var generated = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        File.WriteAllBytes(path, generated.Export(X509ContentType.Pfx, password));
    }

    [Fact]
    public void LoadsValidMountedPkcs12ClientCertificate()
    {
        const string password = "test-only-pfx-password";
        var path = Path.Combine(Path.GetTempPath(), $"mg-agent-{Guid.NewGuid():N}.pfx");
        CreateClientCertificate(path, password, "1.3.6.1.5.5.7.3.2");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Provisioning:Agent:Address"] = "https://provisioner.internal/",
                    ["Provisioning:Agent:ClientCertificatePath"] = path,
                    ["Provisioning:Agent:ClientCertificatePassword"] = password
                })
                .Build();

            using var options = TenantProvisioningAgentOptions.From(configuration);

            Assert.NotNull(options);
            Assert.True(options.ClientCertificate.HasPrivateKey);
            Assert.Equal("https://provisioner.internal/", options.Address.AbsoluteUri);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
