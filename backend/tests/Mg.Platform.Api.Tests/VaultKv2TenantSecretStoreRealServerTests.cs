using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Mg.Platform.Api.Secrets;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Mg.Platform.Api.Tests;

/// <summary>
/// Exercises the production Vault KV v2 HTTP adapter against an actual disposable Vault server.
/// The CI Vault uses an ephemeral dev-mode storage backend and a generated TLS CA; it is never
/// a production configuration or a substitute for persistent-storage/HA acceptance.
/// </summary>
public sealed class VaultKv2TenantSecretStoreRealServerTests
{
    [Fact]
    public async Task RealVaultKvV2StoreReadAndDeleteRoundTrip()
    {
        using var rootCertificate = LoadVaultRootCertificate();
        using var client = CreateClient(rootCertificate);
        var store = CreateStore(client);
        var reference = NewReference();

        await store.StoreAsync(reference, "real-vault-secret", CancellationToken.None);

        Assert.Equal("real-vault-secret",
            await store.ReadAsync(reference, CancellationToken.None));

        await store.DeleteAsync(reference, CancellationToken.None);

        Assert.Null(await store.ReadAsync(reference, CancellationToken.None));
    }

    [Fact]
    public async Task RealVaultKvV2CreateOnlyWriteCannotOverwriteExistingCredential()
    {
        using var rootCertificate = LoadVaultRootCertificate();
        using var client = CreateClient(rootCertificate);
        var store = CreateStore(client);
        var reference = NewReference();

        await store.StoreAsync(reference, "original-credential", CancellationToken.None);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            store.StoreAsync(reference, "replacement-credential", CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal("original-credential",
            await store.ReadAsync(reference, CancellationToken.None));
    }

    [Fact]
    public async Task RealVaultTestTokenCannotReadOutsideItsGrantedSecretPrefix()
    {
        using var rootCertificate = LoadVaultRootCertificate();
        using var client = CreateClient(rootCertificate);
        var store = CreateStore(client);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            store.ReadAsync($"tenants/{Guid.NewGuid():N}/postgres", CancellationToken.None));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
    }

    [Fact]
    public async Task RealVaultCommittedWriteCanBeRecoveredWhenClientLosesResponse()
    {
        using var rootCertificate = LoadVaultRootCertificate();
        using var droppedResponseHandler = new DropFirstSuccessfulWriteResponseHandler(
            CreateVaultHandler(rootCertificate));
        using var interruptedClient = new HttpClient(droppedResponseHandler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        var interruptedStore = CreateStore(interruptedClient);
        var reference = NewReference();

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            interruptedStore.StoreAsync(reference, "committed-before-response-loss", CancellationToken.None));

        using var recoveryClient = CreateClient(rootCertificate);
        var recoveryStore = CreateStore(recoveryClient);

        Assert.Equal("committed-before-response-loss",
            await recoveryStore.ReadAsync(reference, CancellationToken.None));

        var retryException = await Assert.ThrowsAsync<HttpRequestException>(() =>
            recoveryStore.StoreAsync(reference, "must-not-overwrite", CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadRequest, retryException.StatusCode);
        Assert.Equal("committed-before-response-loss",
            await recoveryStore.ReadAsync(reference, CancellationToken.None));
    }

    private static string NewReference() => $"ci/{Guid.NewGuid():N}/postgres";

    private static X509Certificate2 LoadVaultRootCertificate()
    {
        var path = Environment.GetEnvironmentVariable("MG_TEST_VAULT_CA_CERT");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new InvalidOperationException(
                "MG_TEST_VAULT_CA_CERT must point to the CA certificate of the disposable Vault test server.");

        return X509Certificate2.CreateFromPemFile(path);
    }

    private static HttpClient CreateClient(X509Certificate2 rootCertificate) =>
        new(CreateVaultHandler(rootCertificate))
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

    private static HttpClientHandler CreateVaultHandler(X509Certificate2 rootCertificate)
    {
        var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = (_, certificate, _, sslErrors) =>
        {
            if (certificate is null
                || (sslErrors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
            {
                return false;
            }

            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(rootCertificate);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            return chain.Build(certificate);
        };
        return handler;
    }

    private static VaultKv2TenantSecretStore CreateStore(HttpClient client)
    {
        var address = Environment.GetEnvironmentVariable("MG_TEST_VAULT_ADDRESS");
        var token = Environment.GetEnvironmentVariable("MG_TEST_VAULT_TOKEN");
        if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException(
                "MG_TEST_VAULT_ADDRESS and MG_TEST_VAULT_TOKEN must configure the disposable Vault test server.");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Secrets:Vault:Address"] = address,
                ["Secrets:Vault:Token"] = token,
                ["Secrets:Vault:Mount"] = "secret"
            })
            .Build();

        return new VaultKv2TenantSecretStore(client, configuration);
    }

    private sealed class DropFirstSuccessfulWriteResponseHandler(HttpMessageHandler innerHandler)
        : DelegatingHandler(innerHandler)
    {
        private int dropped;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (request.Method == HttpMethod.Post
                && response.IsSuccessStatusCode
                && Interlocked.CompareExchange(ref dropped, 1, 0) == 0)
            {
                response.Dispose();
                throw new HttpRequestException(
                    "Test-only transport fault: Vault committed the KV v2 write but the client did not receive its response.");
            }

            return response;
        }
    }
}
