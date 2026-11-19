using Mg.Platform.Api.Security;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Mg.Platform.Api.Tests;

public sealed class CreatorAuthenticationConfigurationTests
{
    [Fact]
    public void RejectsMissingIssuer()
    {
        var configuration = new ConfigurationBuilder().Build();
        Assert.Throws<InvalidOperationException>(() =>
            CreatorAuthenticationConfiguration.From(configuration));
    }

    [Theory]
    [InlineData("http://issuer.example.com")]
    [InlineData("/relative/issuer")]
    [InlineData("not-a-url")]
    [InlineData("https://user:password@issuer.example.com")]
    [InlineData("https://issuer.example.com?redirect=https://evil.example")]
    [InlineData("https://issuer.example.com#fragment")]
    public void RejectsNonHttpsOrInvalidIssuer(string authority)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Authority"] = authority,
                ["Authentication:Audience"] = "mg-platform-api"
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            CreatorAuthenticationConfiguration.From(configuration));
    }

    [Fact]
    public void RequiresAnAudience()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Authority"] = "https://identity.example.com/"
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            CreatorAuthenticationConfiguration.From(configuration));
    }

    [Theory]
    [InlineData("mg platform")]
    [InlineData("mg\nplatform")]
    [InlineData("mg\tplatform")]
    public void RejectsAudienceWithWhitespaceOrControlCharacters(string audience)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Authority"] = "https://identity.example.com/",
                ["Authentication:Audience"] = audience
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            CreatorAuthenticationConfiguration.From(configuration));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("mg platform")]
    public void RejectsBlankOrMalformedAudience(string audience)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Authority"] = "https://identity.example.com/",
                ["Authentication:Audience"] = audience
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            CreatorAuthenticationConfiguration.From(configuration));
    }

    [Fact]
    public void RejectsAudienceLongerThanTwoHundredCharacters()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Authority"] = "https://identity.example.com/",
                ["Authentication:Audience"] = new string('a', 201)
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            CreatorAuthenticationConfiguration.From(configuration));
    }

    [Fact]
    public void AcceptsAudienceAtTheTwoHundredCharacterLimit()
    {
        var audience = new string('a', 200);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Authority"] = "https://identity.example.com/",
                ["Authentication:Audience"] = audience
            })
            .Build();

        var result = CreatorAuthenticationConfiguration.From(configuration);

        Assert.Equal(audience, result.Audience);
    }

    [Fact]
    public void NormalizesValidHttpsIssuerAndAudience()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Authority"] = "https://identity.example.com/",
                ["Authentication:Audience"] = "  mg-platform-api  "
            })
            .Build();

        var result = CreatorAuthenticationConfiguration.From(configuration);

        Assert.Equal("https://identity.example.com/", result.Authority.AbsoluteUri);
        Assert.Equal("mg-platform-api", result.Audience);
    }
}
