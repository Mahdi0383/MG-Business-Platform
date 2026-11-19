using Mg.Platform.Api.Secrets;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Text;
using Xunit;

namespace Mg.Platform.Api.Tests;

public sealed class VaultKv2TenantSecretStoreTests
{
    [Fact]
    public async Task StoreUsesKvV2EndpointAndTokenHeader()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var handler = new StubHandler(async request =>
        {
            captured = request;
            body = request.Content is null ? null : await request.Content.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        using var client = new HttpClient(handler);
        var store = new VaultKv2TenantSecretStore(client, CreateConfiguration());

        await store.StoreAsync("tenants/cafe-01/postgres", "sensitive-connection-string",
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("/v1/secret/data/tenants/cafe-01/postgres", captured.RequestUri!.AbsolutePath);
        Assert.Equal("vault-test-token", Assert.Single(captured.Headers.GetValues("X-Vault-Token")));
        Assert.Contains("sensitive-connection-string", body ?? string.Empty);
        Assert.Contains("\"cas\":0", body ?? string.Empty);
    }

    [Fact]
    public async Task ReadReturnsKvV2ValueAndTreatsMissingSecretAsNull()
    {
        var handler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"data\":{\"data\":{\"value\":\"tenant-secret\"}}}",
                Encoding.UTF8, "application/json")
        }));
        using var client = new HttpClient(handler);
        var store = new VaultKv2TenantSecretStore(client, CreateConfiguration());

        Assert.Equal("tenant-secret", await store.ReadAsync(
            "tenants/cafe-01/postgres", CancellationToken.None));
    }

    [Fact]
    public async Task ReadReturnsNullForMissingSecret()
    {
        var handler = new StubHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        using var client = new HttpClient(handler);
        var store = new VaultKv2TenantSecretStore(client, CreateConfiguration());

        Assert.Null(await store.ReadAsync("tenants/cafe-01/postgres", CancellationToken.None));
    }

    [Fact]
    public async Task DeleteUsesKvV2MetadataEndpointAndIgnoresMissingSecret()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(request =>
        {
            captured = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });
        using var client = new HttpClient(handler);
        var store = new VaultKv2TenantSecretStore(client, CreateConfiguration());

        await store.DeleteAsync("tenants/cafe-01/postgres", CancellationToken.None);

        Assert.Equal(HttpMethod.Delete, captured!.Method);
        Assert.Equal("/v1/secret/metadata/tenants/cafe-01/postgres", captured.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task RejectsPathTraversalBeforeMakingRequest()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("Request must not be sent."));
        using var client = new HttpClient(handler);
        var store = new VaultKv2TenantSecretStore(client, CreateConfiguration());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.ReadAsync("tenants/../platform", CancellationToken.None));
    }

    private static IConfiguration CreateConfiguration() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Secrets:Vault:Address"] = "https://vault.example.test",
            ["Secrets:Vault:Token"] = "vault-test-token",
            ["Secrets:Vault:Mount"] = "secret"
        }).Build();

    private sealed class StubHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
