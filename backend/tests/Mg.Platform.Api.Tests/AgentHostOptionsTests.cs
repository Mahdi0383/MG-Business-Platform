using Mg.Provisioning.Agent.Api;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Mg.Platform.Api.Tests;

public sealed class AgentHostOptionsTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("80")]
    [InlineData("65536")]
    [InlineData("not-a-port")]
    public void RejectsInvalidListenPort(string port)
    {
        var configuration = Configuration(port, "AA");
        Assert.Throws<InvalidOperationException>(() => AgentHostOptions.From(configuration));
    }

    [Fact]
    public void RejectsMissingExplicitClientCertificateAllowlist()
    {
        var configuration = Configuration("9443", string.Empty);
        Assert.Throws<InvalidOperationException>(() => AgentHostOptions.From(configuration));
    }

    [Theory]
    [InlineData("not-a-thumbprint")]
    [InlineData("0123456789")]
    [InlineData("GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG")]
    public void RejectsMalformedClientCertificateThumbprints(string thumbprints)
    {
        var configuration = Configuration("9443", thumbprints);
        Assert.Throws<InvalidOperationException>(() => AgentHostOptions.From(configuration));
    }

    [Fact]
    public void RejectsMissingServerCertificateConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:ListenPort"] = "9443",
                ["Agent:AllowedClientThumbprints"] = new string('A', 40)
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() => AgentHostOptions.From(configuration));
    }

    private static IConfiguration Configuration(string port, string thumbprints) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:ListenPort"] = port,
                ["Agent:ServerCertificatePath"] = "/not-a-real-agent-server.pfx",
                ["Agent:ServerCertificatePassword"] = "not-a-real-secret",
                ["Agent:AllowedClientThumbprints"] = thumbprints
            })
            .Build();
}
