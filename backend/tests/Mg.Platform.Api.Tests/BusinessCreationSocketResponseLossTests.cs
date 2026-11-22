using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using Mg.Platform.Api.Contracts;
using Mg.Platform.Api.Data;
using Mg.Platform.Api.Endpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Mg.Platform.Api.Tests;

/// <summary>
/// Uses a real loopback Kestrel socket and aborts the client after the registry transaction
/// commits but before response headers are sent.
/// </summary>
public sealed class BusinessCreationSocketResponseLossTests
{
    [Fact]
    public async Task SocketDisconnectAfterCommitCanRetrySameKeyWithoutDuplicateBusinessOrOutbox()
    {
        await using var fixture = await BusinessRegistryConcurrencyPostgresTests.DatabaseFixture.CreateAsync();
        var responseStarting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientDisconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = CreateHost(fixture.TestConnection, responseStarting, clientDisconnected);

        var server = host.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
        Assert.NotNull(addresses);
        var address = Assert.Single(addresses);
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        var key = Guid.NewGuid().ToString("D");

        using var firstRequest = CreateRequest(key);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var firstSend = client.SendAsync(
            firstRequest, HttpCompletionOption.ResponseContentRead, cancellation.Token);

        await responseStarting.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // OnStarting runs after the endpoint handler has completed its durable DB transaction.
        Assert.Equal(1, await fixture.CountBusinessesAsync());
        Assert.Equal(1, await fixture.CountJobsAsync());
        Assert.Equal(1, await fixture.CountIdempotencyRecordsAsync());

        cancellation.Cancel();
        await clientDisconnected.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var transportFailure = await Record.ExceptionAsync(async () => await firstSend);
        Assert.NotNull(transportFailure);
        Assert.True(
            transportFailure is OperationCanceledException
                or HttpRequestException
                or IOException,
            $"Expected a socket cancellation/disconnect, received {transportFailure.GetType().FullName}.");

        using var retryRequest = CreateRequest(key);
        using var retryResponse = await client.SendAsync(retryRequest);

        Assert.Equal(HttpStatusCode.Created, retryResponse.StatusCode);
        Assert.Equal("true", retryResponse.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal(1, await fixture.CountBusinessesAsync());
        Assert.Equal(1, await fixture.CountJobsAsync());
        Assert.Equal(1, await fixture.CountIdempotencyRecordsAsync());
    }

    private static HttpRequestMessage CreateRequest(string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/platform/businesses/")
        {
            Content = System.Net.Http.Json.JsonContent.Create(
                new CreateBusinessRequest("Socket Recovery Cafe", "socket-recovery-cafe"))
        };
        request.Headers.Add("X-Test-Role", "PlatformOwner");
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    private static IHost CreateHost(
        string connectionString,
        TaskCompletionSource responseStarting,
        TaskCompletionSource clientDisconnected)
    {
        var responseLossInjected = 0;
        return new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseKestrel();
                webHost.UseUrls("http://127.0.0.1:0");
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddDbContext<PlatformDbContext>(options => options.UseNpgsql(connectionString));
                    services.AddScoped<BusinessRegistryService>();
                    services.AddAuthentication("MG-Socket-Test")
                        .AddScheme<AuthenticationSchemeOptions, SocketTestAuthenticationHandler>(
                            "MG-Socket-Test", _ => { });
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
                    app.Use(async (context, next) =>
                    {
                        if (context.Request.Path == "/api/v1/platform/businesses/"
                            && Interlocked.CompareExchange(ref responseLossInjected, 1, 0) == 0)
                        {
                            context.Response.OnStarting(async () =>
                            {
                                responseStarting.TrySetResult();
                                try
                                {
                                    await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
                                }
                                catch (OperationCanceledException)
                                {
                                    clientDisconnected.TrySetResult();
                                    throw;
                                }
                            });
                        }

                        await next();
                    });
                    app.UseEndpoints(endpoints => endpoints.MapPlatformEndpoints());
                });
            })
            .Start();
    }

    private sealed class SocketTestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, "socket-integration-creator"),
                new("iss", "https://issuer.test"),
                new("sub", "socket-integration-subject"),
                new("mg_platform_role", role.ToString())
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
