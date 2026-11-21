using System.Security.Claims;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
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
    public async Task ClientResponseLossAfterCommitCanRetryWithSameKeyAndRecoverOriginalBusiness()
    {
        await using var fixture = await BusinessRegistryConcurrencyPostgresTests.DatabaseFixture.CreateAsync();
        var key = Guid.NewGuid().ToString("D");
        using var host = CreateHost(fixture.TestConnection, key);
        using var client = host.GetTestClient();

        using var firstRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/platform/businesses/")
        {
            Content = JsonContent.Create(new CreateBusinessRequest("Lost Response Cafe", "lost-response-cafe"))
        };
        firstRequest.Headers.Add("X-Test-Role", "PlatformOwner");
        firstRequest.Headers.Add("Idempotency-Key", key);

        HttpResponseMessage? firstResponse = null;
        Exception? transportFailure = null;
        try
        {
            firstResponse = await client.SendAsync(firstRequest);
        }
        catch (Exception exception)
        {
            transportFailure = exception;
        }

        if (firstResponse is not null)
        {
            Assert.NotEqual(System.Net.HttpStatusCode.Created, firstResponse.StatusCode);
            firstResponse.Dispose();
        }
        else
        {
            Assert.NotNull(transportFailure);
        }

        Assert.Equal(1, await fixture.CountBusinessesAsync());
        Assert.Equal(1, await fixture.CountJobsAsync());
        Assert.Equal(1, await fixture.CountIdempotencyRecordsAsync());

        using var retryRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/platform/businesses/")
        {
            Content = JsonContent.Create(new CreateBusinessRequest("Lost Response Cafe", "lost-response-cafe"))
        };
        retryRequest.Headers.Add("X-Test-Role", "PlatformOwner");
        retryRequest.Headers.Add("Idempotency-Key", key);
        using var retryResponse = await client.SendAsync(retryRequest);

        Assert.Equal(System.Net.HttpStatusCode.Created, retryResponse.StatusCode);
        Assert.Equal("true", retryResponse.Headers.GetValues("Idempotency-Replayed").Single());
        var replayed = await retryResponse.Content.ReadFromJsonAsync<BusinessSummary>();
        Assert.NotNull(replayed);
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
        wrongRoleRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var wrongRole = await client.SendAsync(wrongRoleRequest);
        Assert.Equal(StatusCodes.Status403Forbidden, (int)wrongRole.StatusCode);

        using var invalidSlugRequest = new HttpRequestMessage(
            HttpMethod.Post, "/api/v1/platform/businesses/")
        {
            Content = System.Net.Http.Json.JsonContent.Create(
                new CreateBusinessRequest("Invalid Slug", "Not A Valid Slug"))
        };
        invalidSlugRequest.Headers.Add("X-Test-Role", "PlatformOwner");
        invalidSlugRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var invalidSlug = await client.SendAsync(invalidSlugRequest);
        Assert.Equal(StatusCodes.Status400BadRequest, (int)invalidSlug.StatusCode);

        Assert.Equal(0, await fixture.CountBusinessesAsync());
        Assert.Equal(0, await fixture.CountJobsAsync());
    }


    [Fact]
    public async Task BusinessEndpointReplaysSameIdempotentRequestAndRejectsChangedPayloadOrCreator()
    {
        await using var fixture = await BusinessRegistryConcurrencyPostgresTests.DatabaseFixture.CreateAsync();
        using var host = CreateHost(fixture.TestConnection);
        using var client = host.GetTestClient();
        var key = Guid.NewGuid().ToString();
        var payload = new CreateBusinessRequest("Replay Cafe", "replay-cafe");

        async Task<HttpResponseMessage> SendAsync(CreateBusinessRequest requestBody, string subject)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/platform/businesses/")
            {
                Content = JsonContent.Create(requestBody)
            };
            request.Headers.Add("X-Test-Role", "PlatformOwner");
            request.Headers.Add("X-Test-Subject", subject);
            request.Headers.Add("Idempotency-Key", key);
            return await client.SendAsync(request);
        }

        using var first = await SendAsync(payload, "creator-one");
        using var replay = await SendAsync(
            payload with { Name = " Replay Cafe ", Slug = "REPLAY-CAFE" }, "creator-one");
        using var changedPayload = await SendAsync(
            new CreateBusinessRequest("Different Cafe", "different-cafe"), "creator-one");
        using var changedCreator = await SendAsync(payload, "creator-two");

        Assert.Equal(StatusCodes.Status201Created, (int)first.StatusCode);
        Assert.Equal(StatusCodes.Status201Created, (int)replay.StatusCode);
        Assert.Equal(first.Headers.Location, replay.Headers.Location);
        Assert.Equal("true", replay.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal(StatusCodes.Status409Conflict, (int)changedPayload.StatusCode);
        Assert.Equal(StatusCodes.Status409Conflict, (int)changedCreator.StatusCode);
        Assert.Equal(1, await fixture.CountBusinessesAsync());
        Assert.Equal(1, await fixture.CountJobsAsync());
        Assert.Equal(1, await fixture.CountIdempotencyRecordsAsync());
    }


    [Fact]
    public async Task OpenApiDocumentsIdempotencyKeyAsRequiredHeaderParameter()
    {
        await using var fixture = await BusinessRegistryConcurrencyPostgresTests.DatabaseFixture.CreateAsync();
        using var host = CreateHost(fixture.TestConnection);
        using var client = host.GetTestClient();

        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        var businessPath = paths.EnumerateObject()
            .Single(path => path.Name.TrimEnd('/') == "/api/v1/platform/businesses");
        var post = businessPath.Value.GetProperty("post");
        var parameters = new List<JsonElement>();
        if (businessPath.Value.TryGetProperty("parameters", out var pathParameters))
            parameters.AddRange(pathParameters.EnumerateArray());
        if (post.TryGetProperty("parameters", out var operationParameters))
            parameters.AddRange(operationParameters.EnumerateArray());
        var header = parameters.Single(parameter =>
            parameter.GetProperty("name").GetString() == "Idempotency-Key");

        Assert.Equal("header", header.GetProperty("in").GetString());
        Assert.True(header.GetProperty("required").GetBoolean());
        Assert.Contains("UUID", header.GetProperty("description").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    private static IHost CreateHost(string connectionString, string? loseFirstResponseForKey = null) =>
        new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddOpenApi();
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
                var responseLossInjected = 0;
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseRateLimiter();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.Use(async (context, next) =>
                    {
                        var shouldLoseResponse = !string.IsNullOrWhiteSpace(loseFirstResponseForKey)
                            && context.Request.Path == "/api/v1/platform/businesses/"
                            && context.Request.Headers["Idempotency-Key"] == loseFirstResponseForKey
                            && Interlocked.CompareExchange(ref responseLossInjected, 1, 0) == 0;
                        if (!shouldLoseResponse)
                        {
                            await next();
                            return;
                        }

                        var originalBody = context.Response.Body;
                        context.Response.Body = new DropResponseStream();
                        try
                        {
                            await next();
                        }
                        finally
                        {
                            context.Response.Body = originalBody;
                        }
                    });
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapOpenApi();
                        endpoints.MapPlatformEndpoints();
                    });
                });
            })
            .Start();

    private sealed class DropResponseStream : Stream
    {
        private static IOException LostResponse() =>
            new("Simulated client connection loss after the business/outbox transaction committed.");

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw LostResponse();
        public override Task FlushAsync(CancellationToken cancellationToken) =>
            Task.FromException(LostResponse());
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw LostResponse();
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromException(LostResponse());
        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException(LostResponse());
    }

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
