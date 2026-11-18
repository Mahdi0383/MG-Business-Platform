using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mg.Platform.Api.Provisioning;
using Xunit;

namespace Mg.Platform.Api.Tests;

public sealed class HttpTenantDatabaseProvisionerTests
{
    [Fact]
    public async Task SendsOnlyTenantIdentityAndIdempotencyContractToAgent()
    {
        var tenantId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(request =>
        {
            captured = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    connectionString = $"Host=tenant-db.internal;Database=mg_tenant_{tenantId:N};Username=tenant_app;Password=secret;SSL Mode=VerifyFull"
                })
            });
        });
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://provisioner.internal/")
        };
        var provisioner = new HttpTenantDatabaseProvisioner(client);

        var result = await provisioner.ProvisionAsync(
            new TenantDatabaseProvisioningRequest(
                tenantId, idempotencyKey, $"mg_tenant_{tenantId:N}"),
            CancellationToken.None);

        Assert.Contains($"Database=mg_tenant_{tenantId:N}", result, StringComparison.Ordinal);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("/internal/v1/tenant-databases", captured.RequestUri!.AbsolutePath);
        using var body = await JsonDocument.ParseAsync(await captured.Content!.ReadAsStreamAsync());
        Assert.Equal(tenantId, body.RootElement.GetProperty("tenantId").GetGuid());
        Assert.Equal(idempotencyKey, body.RootElement.GetProperty("idempotencyKey").GetGuid());
        Assert.Equal($"mg_tenant_{tenantId:N}", body.RootElement.GetProperty("databaseName").GetString());
    }

    [Fact]
    public async Task RejectsNonDeterministicDatabaseNameWithoutCallingAgent()
    {
        var called = false;
        var handler = new StubHandler(_ =>
        {
            called = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var client = new HttpClient(handler);
        var provisioner = new HttpTenantDatabaseProvisioner(client);
        var tenantId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<TenantProvisioningException>(() =>
            provisioner.ProvisionAsync(
                new TenantDatabaseProvisioningRequest(tenantId, Guid.NewGuid(), "mg_tenant_arbitrary"),
                CancellationToken.None));

        Assert.Equal("TENANT_PROVISIONING_REQUEST_INVALID", exception.SafeErrorCode);
        Assert.True(exception.Permanent);
        Assert.False(called);
    }

    [Fact]
    public async Task MapsAgentUnavailableToSafeTransientFailure()
    {
        var handler = new StubHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var client = new HttpClient(handler);
        var provisioner = new HttpTenantDatabaseProvisioner(client);
        var tenantId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<TenantProvisioningException>(() =>
            provisioner.ProvisionAsync(
                new TenantDatabaseProvisioningRequest(tenantId, Guid.NewGuid(), $"mg_tenant_{tenantId:N}"),
                CancellationToken.None));

        Assert.Equal("TENANT_PROVISIONING_AGENT_UNAVAILABLE", exception.SafeErrorCode);
        Assert.False(exception.Permanent);
    }

    [Fact]
    public async Task MapsAgentAuthorizationFailureToPermanentSafeFailure()
    {
        var handler = new StubHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)));
        using var client = new HttpClient(handler);
        var provisioner = new HttpTenantDatabaseProvisioner(client);
        var tenantId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<TenantProvisioningException>(() =>
            provisioner.ProvisionAsync(
                new TenantDatabaseProvisioningRequest(tenantId, Guid.NewGuid(), $"mg_tenant_{tenantId:N}"),
                CancellationToken.None));

        Assert.Equal("TENANT_PROVISIONING_AGENT_REJECTED", exception.SafeErrorCode);
        Assert.True(exception.Permanent);
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            responseFactory(request);
    }
}
