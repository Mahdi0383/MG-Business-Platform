using Mg.Provisioning.Agent.Api;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Mg.Platform.Api.Tests;

public sealed class NpgsqlAgentProvisioningHandlerTests
{
    [Fact]
    public void ReturnsNoHandlerWhenProvisioningSecretsAreNotConfigured()
    {
        var configuration = new ConfigurationBuilder().Build();
        Assert.Null(NpgsqlAgentProvisioningHandler.TryCreate(configuration, AppContext.BaseDirectory));
    }

    [Fact]
    public void RejectsPartialProvisioningSecretConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:ControlDatabase:ConnectionString"] =
                    "Host=localhost;Database=mg_agent_control;Username=mg_agent_control;Password=not-a-real-secret;SSL Mode=VerifyFull"
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            NpgsqlAgentProvisioningHandler.TryCreate(configuration, AppContext.BaseDirectory));
    }

    [Fact]
    public void CreatesHandlerOnlyWithCompleteStrictTlsControlConnectionAndKeyRing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:ControlDatabase:ConnectionString"] =
                    "Host=localhost;Database=mg_agent_control;Username=mg_agent_control;Password=not-a-real-secret;SSL Mode=VerifyFull",
                ["Agent:CredentialEncryption:CurrentKeyId"] = "key-v1",
                ["Agent:CredentialEncryption:Keys:key-v1"] =
                    Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            })
            .Build();

        using var handler = NpgsqlAgentProvisioningHandler.TryCreate(configuration, AppContext.BaseDirectory);

        Assert.NotNull(handler);
    }

    [Fact]
    public void RejectsControlConnectionThatDoesNotValidateServerCertificate()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:ControlDatabase:ConnectionString"] =
                    "Host=localhost;Database=mg_agent_control;Username=mg_agent_control;Password=not-a-real-secret;SSL Mode=Require",
                ["Agent:CredentialEncryption:CurrentKeyId"] = "key-v1",
                ["Agent:CredentialEncryption:Keys:key-v1"] =
                    Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            NpgsqlAgentProvisioningHandler.TryCreate(configuration, AppContext.BaseDirectory));
    }
}
