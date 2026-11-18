using Mg.Platform.Api.Security;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Mg.Platform.Api.Tests;

public sealed class PlatformKeyAuthenticatorTests
{
    [Fact]
    public void IsUnauthorizedWhenBootstrapKeyIsNotConfigured()
    {
        var configuration = new ConfigurationBuilder().Build();
        var authenticator = new PlatformKeyAuthenticator(configuration);

        Assert.False(authenticator.IsConfigured);
        Assert.False(authenticator.IsAuthorized("anything"));
    }

    [Fact]
    public void AcceptsOnlyExactConfiguredKey()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:BootstrapPlatformKey"] = "a-long-test-only-key-with-32-bytes-minimum-123"
            })
            .Build();
        var authenticator = new PlatformKeyAuthenticator(configuration);

        Assert.True(authenticator.IsConfigured);
        Assert.True(authenticator.IsAuthorized("a-long-test-only-key-with-32-bytes-minimum-123"));
        Assert.False(authenticator.IsAuthorized("a-long-test-only-key-with-32-bytes-minimum-123 "));
        Assert.False(authenticator.IsAuthorized(null));
        Assert.False(authenticator.IsAuthorized("wrong"));
    }
}
