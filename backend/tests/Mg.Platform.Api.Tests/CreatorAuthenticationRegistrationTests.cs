using Mg.Platform.Api.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Mg.Platform.Api.Tests;

public sealed class CreatorAuthenticationRegistrationTests
{
    [Fact]
    public void RegistersHttpsAuthorityAudienceAndStrictMetadataSettings()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Authority"] = "https://identity.example.test/realms/mg/",
                ["Authentication:Audience"] = "mg-platform-api"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddCreatorAuthentication(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal("https://identity.example.test/realms/mg", options.Authority);
        Assert.Equal("mg-platform-api", options.Audience);
        Assert.True(options.RequireHttpsMetadata);
        Assert.False(options.MapInboundClaims);
    }

    [Fact]
    public void RejectsInvalidOidcConfigurationBeforeRegisteringAuthentication()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Authority"] = "http://identity.example.test/realms/mg",
                ["Authentication:Audience"] = "mg-platform-api"
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddCreatorAuthentication(configuration));
    }
}
