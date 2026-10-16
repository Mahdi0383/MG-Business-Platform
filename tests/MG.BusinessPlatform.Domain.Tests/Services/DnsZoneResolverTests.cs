using MG.BusinessPlatform.Infrastructure.Services;

namespace MG.BusinessPlatform.Domain.Tests.Services;

public sealed class DnsZoneResolverTests
{
    [Fact]
    public void Resolve_RootHostnameUsesAtRecord()
    {
        var result = DnsZoneResolver.Resolve("mgalexander.cam.", ["mgalexander.cam"]);

        Assert.Equal(new DnsZoneTarget("mgalexander.cam", "@"), result);
    }

    [Fact]
    public void Resolve_SubdomainUsesProviderRelativeHost()
    {
        var result = DnsZoneResolver.Resolve("shop.example.com", ["example.com", "com"]);

        Assert.Equal(new DnsZoneTarget("example.com", "shop"), result);
    }

    [Fact]
    public void Resolve_UsesLongestMatchingZoneForNestedZones()
    {
        var result = DnsZoneResolver.Resolve("pos.shop.example.com", ["com", "example.com", "shop.example.com"]);

        Assert.Equal(new DnsZoneTarget("shop.example.com", "pos"), result);
    }

    [Fact]
    public void Resolve_DoesNotMatchHostnameSuffixWithoutLabelBoundary()
    {
        Assert.Throws<InvalidOperationException>(() =>
            DnsZoneResolver.Resolve("notexample.com", ["example.com"]));
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("example.com:8080")]
    [InlineData("example.com/path")]
    [InlineData("*.example.com")]
    public void Resolve_RejectsUrlPortPathAndWildcard(string hostname)
    {
        Assert.Throws<ArgumentException>(() => DnsZoneResolver.Resolve(hostname, ["example.com"]));
    }
}
