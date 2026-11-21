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

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, "integration-test-creator")
            };
            if (!string.IsNullOrWhiteSpace(role.ToString()))
                claims.Add(new Claim("mg_platform_role", role.ToString()));

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
