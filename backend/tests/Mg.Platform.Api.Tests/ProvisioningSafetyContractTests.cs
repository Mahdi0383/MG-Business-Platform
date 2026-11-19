using Mg.Platform.Api.Provisioning;
using Mg.Platform.Api.Secrets;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Mg.Platform.Api.Tests;

public sealed class ProvisioningSafetyContractTests
{
    [Fact]
    public void ProvisioningExceptionAcceptsOnlySafeStableCodes()
    {
        var exception = new TenantProvisioningException("DATABASE_CREATE_DENIED", permanent: true);
        Assert.Equal("DATABASE_CREATE_DENIED", exception.SafeErrorCode);
        Assert.True(exception.Permanent);
        Assert.DoesNotContain("DATABASE_CREATE_DENIED", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("contains spaces")]
    [InlineData("lowercase")]
    [InlineData("token=secret")]
    public void ProvisioningExceptionRejectsUnsafeCodes(string code)
    {
        Assert.Throws<ArgumentException>(() => new TenantProvisioningException(code));
    }

    [Fact]
    public void VaultOptionsAcceptHttpsOriginAndDefaultMount()
    {
        var configuration = Configuration("https://vault.example.test", "vault-token", null);
        var options = VaultSecretStoreOptions.From(configuration);

        Assert.Equal("https://vault.example.test/", options.Address.AbsoluteUri);
        Assert.Equal("secret", options.Mount);
    }

    [Theory]
    [InlineData("http://vault.example.test")]
    [InlineData("https://user:pass@vault.example.test")]
    [InlineData("https://vault.example.test/extra")]
    [InlineData("https://vault.example.test?debug=true")]
    public void VaultOptionsRejectUnsafeAddress(string address)
    {
        Assert.Throws<InvalidOperationException>(() =>
            VaultSecretStoreOptions.From(Configuration(address, "vault-token", "secret")));
    }

    [Fact]
    public void VaultOptionsRejectMissingToken()
    {
        Assert.Throws<InvalidOperationException>(() =>
            VaultSecretStoreOptions.From(Configuration("https://vault.example.test", "", "secret")));
    }

    private static IConfiguration Configuration(string address, string token, string? mount) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Secrets:Vault:Address"] = address,
            ["Secrets:Vault:Token"] = token,
            ["Secrets:Vault:Mount"] = mount
        }).Build();
}
