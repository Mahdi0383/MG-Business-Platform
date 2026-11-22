using Mg.Platform.Api.Secrets;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Text;
using System.Text.Json;
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
    public async Task RecoversCommittedSecretWhenVaultWriteResponseIsLostAndNeverOverwritesIt()
    {
        var handler = new InMemoryVaultHandler(loseFirstWriteResponse: true);
        using var client = new HttpClient(handler);
        var store = new VaultKv2TenantSecretStore(client, CreateConfiguration());

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            store.StoreAsync("tenants/cafe-01/postgres", "winning-secret", CancellationToken.None));

        Assert.Equal("winning-secret", await store.ReadAsync(
            "tenants/cafe-01/postgres", CancellationToken.None));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            store.StoreAsync("tenants/cafe-01/postgres", "replacement-secret", CancellationToken.None));

        Assert.Equal("winning-secret", await store.ReadAsync(
            "tenants/cafe-01/postgres", CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentKvV2CasCreateOnlyWritesCannotOverwriteWinningCredential()
    {
        var handler = new InMemoryVaultHandler();
        using var client = new HttpClient(handler);
        var store = new VaultKv2TenantSecretStore(client, CreateConfiguration());

        var results = await Task.WhenAll(
            Record.ExceptionAsync(() => store.StoreAsync(
                "tenants/cafe-01/postgres", "credential-a", CancellationToken.None)),
            Record.ExceptionAsync(() => store.StoreAsync(
                "tenants/cafe-01/postgres", "credential-b", CancellationToken.None)));

        Assert.Single(results, exception => exception is null);
        Assert.Single(results, exception => exception is HttpRequestException);
        var stored = await store.ReadAsync("tenants/cafe-01/postgres", CancellationToken.None);
        Assert.Contains(stored, new[] { "credential-a", "credential-b" });
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

    private sealed class InMemoryVaultHandler(bool loseFirstWriteResponse = false) : HttpMessageHandler
    {
        private readonly object gate = new();
        private string? storedValue;
        private bool responseLossPending = loseFirstWriteResponse;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post)
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                using var document = JsonDocument.Parse(body);
                Assert.Equal(0, document.RootElement.GetProperty("options").GetProperty("cas").GetInt32());
                var incoming = document.RootElement.GetProperty("data").GetProperty("value").GetString()
                    ?? throw new InvalidOperationException("Vault test request did not contain a value.");

                bool loseResponse;
                lock (gate)
                {
                    if (storedValue is not null)
                        return new HttpResponseMessage(HttpStatusCode.BadRequest);
                    storedValue = incoming;
                    loseResponse = responseLossPending;
                    responseLossPending = false;
                }

                if (loseResponse)
                    throw new HttpRequestException("Vault accepted the write but the response was lost.");
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Get)
            {
                string? value;
                lock (gate) value = storedValue;
                if (value is null)
                    return new HttpResponseMessage(HttpStatusCode.NotFound);

                var json = JsonSerializer.Serialize(new { data = new { data = new { value } } });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
        }
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
