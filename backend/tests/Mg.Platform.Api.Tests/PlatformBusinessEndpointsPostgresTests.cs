using System.Security.Claims;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using Mg.Platform.Api.Contracts;
using Mg.Platform.Api.Data;
using Mg.Platform.Api.Endpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Mg.Platform.Api.Tests;

public sealed class PlatformBusinessEndpointsPostgresTests
{
    [Fact]
    public async Task ConcurrentSameSlugHttpRequestsReturnOneCreatedAndPersistOneOutboxJob()
    {
        await using var fixture = await BusinessRegistryConcurrencyPostgresTests.DatabaseFixture.CreateAsync();
        using var host = CreateHost(fixture.TestConnection);
        using var client = host.GetTestClient();

        async Task<HttpResponseMessage> CreateAsync(int index)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/platform/businesses/")
            {
                Content = System.Net.Http.Json.JsonContent.Create(
                    new CreateBusinessRequest($"Cafe {index}", "same-http-cafe"))
            };
            request.Headers.Add("X-Test-Role", "PlatformOwner");
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
            return await client.SendAsync(request);
        }

        var responses = await Task.WhenAll(Enumerable.Range(0, 16).Select(CreateAsync));
        try
        {
            Assert.Single(responses, response => response.StatusCode == System.Net.HttpStatusCode.Created);
            Assert.Equal(15, responses.Count(response => response.StatusCode == System.Net.HttpStatusCode.Conflict));
            Assert.All(responses, response =>
                Assert.Contains(response.StatusCode, new[] {
                    System.Net.HttpStatusCode.Created,
                    System.Net.HttpStatusCode.Conflict
                }));
            Assert.Equal(1, await fixture.CountBusinessesAsync());
            Assert.Equal(1, await fixture.CountJobsAsync());
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }

    [Fact]
    public async Task BusinessEndpointRateLimitsRequestsFromOneClientBeforeRegistryWrites()
    {
        await using var fixture = await BusinessRegistryConcurrencyPostgresTests.DatabaseFixture.CreateAsync();
        using var host = CreateHost(fixture.TestConnection);
        using var client = host.GetTestClient();

        async Task<HttpResponseMessage> CreateAsync(int index)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/platform/businesses/")
            {
                Content = JsonContent.Create(new CreateBusinessRequest($"Rate Cafe {index}", $"rate-cafe-{index}"))
            };
            request.Headers.Add("X-Test-Role", "PlatformOwner");
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
            return await client.SendAsync(request);
        }

        var responses = await Task.WhenAll(Enumerable.Range(0, 31).Select(CreateAsync));
        try
        {
            Assert.Equal(30, responses.Count(response => response.StatusCode == System.Net.HttpStatusCode.Created));
            Assert.Single(responses, response => response.StatusCode == System.Net.HttpStatusCode.TooManyRequests);
            Assert.Equal(30, await fixture.CountBusinessesAsync());
            Assert.Equal(30, await fixture.CountJobsAsync());
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }

    [Fact]
    public async Task SameIdempotencyKeyReplaysSameCreatedResourceAfterClientRetry()
    {
        await using var fixture = await BusinessRegistryConcurrencyPostgresTests.DatabaseFixture.CreateAsync();
        using var host = CreateHost(fixture.TestConnection);
        using var client = host.GetTestClient();
        var key = Guid.NewGuid().ToString("D");

        async Task<HttpResponseMessage> SendAsync(string name, string slug, string subject = "creator-1")
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/platform/businesses/")
            {
                Content = JsonContent.Create(new CreateBusinessRequest(name, slug))
            };
            request.Headers.Add("X-Test-Role", "PlatformOwner");
            request.Headers.Add("X-Test-Subject", subject);
            request.Headers.Add("Idempotency-Key", key);
            return await client.SendAsync(request);
        }

        using var first = await SendAsync("Replay Cafe", "replay-cafe");
        using var retry = await SendAsync(" Replay Cafe ", "REPLAY-CAFE");
        using var changedPayload = await SendAsync("Changed Cafe", "changed-cafe");
        using var changedCreator = await SendAsync("Replay Cafe", "replay-cafe", "creator-2");

        Assert.Equal(System.Net.HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal("true", retry.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal(System.Net.HttpStatusCode.Conflict, changedPayload.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Conflict, changedCreator.StatusCode);

        var firstBody = await first.Content.ReadFromJsonAsync<BusinessSummary>();
        var retryBody = await retry.Content.ReadFromJsonAsync<BusinessSummary>();
        Assert.NotNull(firstBody);
        Assert.NotNull(retryBody);
        Assert.Equal(firstBody.Id, retryBody.Id);
        Assert.Equal(1, await fixture.CountBusinessesAsync());
        Assert.Equal(1, await fixture.CountJobsAsync());
        Assert.Equal(1, await fixture.CountIdempotencyRecordsAsync());
    }

    [Fact]
    public async Task BusinessEndpointRequiresAValidIdempotencyKey()
    {
        await using var fixture = await BusinessRegistryConcurrencyPostgresTests.DatabaseFixture.CreateAsync();
        using var host = CreateHost(fixture.TestConnection);
        using var client = host.GetTestClient();

        using var missing = new HttpRequestMessage(HttpMethod.Post, "/api/v1/platform/businesses/")
        {
            Content = JsonContent.Create(new CreateBusinessRequest("Missing Key", "missing-key"))
        };
        missing.Headers.Add("X-Test-Role", "PlatformOwner");
        using var missingResponse = await client.SendAsync(missing);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, missingResponse.StatusCode);

        using var invalid = new HttpRequestMessage(HttpMethod.Post, "/api/v1/platform/businesses/")
        {
            Content = JsonContent.Create(new CreateBusinessRequest("Invalid Key", "invalid-key"))
        };
        invalid.Headers.Add("X-Test-Role", "PlatformOwner");
        invalid.Headers.Add("Idempotency-Key", Guid.Empty.ToString("D"));
        using var invalidResponse = await client.SendAsync(invalid);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, invalidResponse.StatusCode);

        Assert.Equal(0, await fixture.CountBusinessesAsync());
        Assert.Equal(0, await fixture.CountJobsAsync());
    }

    [Fact]
    public async Task BusinessEndpointRejectsUnauthenticatedAndWrongRoleRequestsBeforePersistence()
    {
        await using var fixture = await BusinessRegistryConcurrencyPostgresTests.DatabaseFixture.CreateAsync();
        using var host = CreateHost(fixture.TestConnection);
        using var client = host.GetTestClient();

        using var unauthenticated = await client.PostAsJsonAsync(
            "/api/v1/platform/businesses/", new CreateBusinessRequest("No Auth", "no-auth"));
        Assert.Equal(StatusCodes.Status401Unauthorized, (int)unauthenticated.StatusCode);

        using var wrongRoleRequest = new HttpRequestMessage(
            HttpMethod.Post, "/api/v1/platform/businesses/")
        {
            Content = System.Net.Http.Json.JsonContent.Create(
                new CreateBusinessRequest("Wrong Role", "wrong-role"))
        };
        wrongRoleRequest.Headers.Add("X-Test-Role", "StoreManager");
        using var wrongRole = await client.SendAsync(wrongRoleRequest);
        Assert.Equal(StatusCodes.Status403Forbidden, (int)wrongRole.StatusCode);

        using var invalidSlugRequest = new HttpRequestMessage(
            HttpMethod.Post, "/api/v1/platform/businesses/")
        {
            Content = System.Net.Http.Json.JsonContent.Create(
                new CreateBusinessRequest("Invalid Slug", "Not A Valid Slug"))
        };
        invalidSlugRequest.Headers.Add("X-Test-Role", "PlatformOwner");
        using var invalidSlug = await client.SendAsync(invalidSlugRequest);
        Assert.Equal(StatusCodes.Status400BadRequest, (int)invalidSlug.StatusCode);

        Assert.Equal(0, await fixture.CountBusinessesAsync());
        Assert.Equal(0, await fixture.CountJobsAsync());
    }

    private static IHost CreateHost(string connectionString) =>
        new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddDbContext<PlatformDbContext>(options => options.UseNpgsql(connectionString));
                    services.AddScoped<BusinessRegistryService>();
                    services.AddAuthentication("MG-Test")
                        .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("MG-Test", _ => { });
                    services.AddAuthorization(options =>
                        options.AddPolicy("mg-creator", policy => policy
                            .RequireAuthenticatedUser()
                            .RequireClaim("mg_platform_role", "PlatformOwner", "PlatformAdministrator")));
                    services.AddRateLimiter(options =>
                    {
                        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                        options.AddPolicy("creator-bootstrap", context =>
                            RateLimitPartition.GetFixedWindowLimiter(
                                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                                _ => new FixedWindowRateLimiterOptions
                                {
                                    PermitLimit = 30,
                                    Window = TimeSpan.FromMinutes(1),
                                    QueueLimit = 0,
                                    AutoReplenishment = false
                                }));
                    });
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseRateLimiter();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapPlatformEndpoints());
                });
            })
            .Start();

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role))
                return Task.FromResult(AuthenticateResult.NoResult());

            var subject = Request.Headers.TryGetValue("X-Test-Subject", out var suppliedSubject)
                ? suppliedSubject.ToString()
                : "integration-test-creator";
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, "integration-test-creator"),
                new("iss", "https://issuer.test"),
                new("sub", subject)
            };
            if (!string.IsNullOrWhiteSpace(role.ToString()))
                claims.Add(new Claim("mg_platform_role", role.ToString()));

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
