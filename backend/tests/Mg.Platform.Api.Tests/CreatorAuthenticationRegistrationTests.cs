using Mg.Platform.Api.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Mg.Platform.Api.Tests;

public sealed class CreatorAuthenticationRegistrationTests
{
    [Fact]
    public async Task RegistersHttpsAuthorityAudienceAndStrictMetadataSettings()
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
        Assert.True(options.TokenValidationParameters.ValidateIssuer);
        Assert.True(options.TokenValidationParameters.ValidateAudience);
        Assert.True(options.TokenValidationParameters.ValidateLifetime);
        Assert.True(options.TokenValidationParameters.RequireSignedTokens);
        Assert.True(options.TokenValidationParameters.RequireExpirationTime);

        var defaultScheme = await provider.GetRequiredService<IAuthenticationSchemeProvider>()
            .GetDefaultAuthenticateSchemeAsync();
        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, defaultScheme?.Name);
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
