using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mg.Platform.Api.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Mg.Platform.Api.Tests;

/// <summary>
/// Exercises the real JwtBearer middleware against a local OIDC discovery/JWKS issuer.
/// No test authentication handler is registered in this host.
/// </summary>
public sealed class CreatorOidcJwtBearerIntegrationTests
{
    private const string Issuer = "https://issuer.test";
    private const string Audience = "mg-platform-api";
    private const string KeyId = "mg-creator-integration-key";

    [Fact]
    public async Task AcceptsSignedTokenAndPreservesIssuerSubjectAndCreatorRoleClaims()
    {
        using var signingKey = RSA.Create(2048);
        using var host = CreateHost(signingKey);
        using var client = host.GetTestClient();
        var token = CreateToken(signingKey, Issuer, Audience, DateTime.UtcNow.AddMinutes(5), "PlatformOwner");

        using var response = await SendAsync(client, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(Issuer, body.RootElement.GetProperty("issuer").GetString());
        Assert.Equal("creator-oidc-123", body.RootElement.GetProperty("subject").GetString());
        Assert.Equal("PlatformOwner", body.RootElement.GetProperty("role").GetString());
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expired")]
    public async Task RejectsTokenWithInvalidIssuerAudienceOrLifetime(string invalidField)
    {
        using var signingKey = RSA.Create(2048);
        using var host = CreateHost(signingKey);
        using var client = host.GetTestClient();

        var issuer = invalidField == "issuer" ? "https://attacker.test" : Issuer;
        var audience = invalidField == "audience" ? "another-api" : Audience;
        var expires = invalidField == "expired"
            ? DateTime.UtcNow.AddMinutes(-5)
            : DateTime.UtcNow.AddMinutes(5);
        var token = CreateToken(signingKey, issuer, audience, expires, "PlatformOwner");

        using var response = await SendAsync(client, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RejectsTokenSignedByKeyNotPublishedInIssuerJwks()
    {
        using var publishedKey = RSA.Create(2048);
        using var attackerKey = RSA.Create(2048);
        using var host = CreateHost(publishedKey);
        using var client = host.GetTestClient();
        var token = CreateToken(attackerKey, Issuer, Audience, DateTime.UtcNow.AddMinutes(5), "PlatformOwner");

        using var response = await SendAsync(client, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RejectsValidlySignedTokenWithoutAnAuthorizedCreatorRole()
    {
        using var signingKey = RSA.Create(2048);
        using var host = CreateHost(signingKey);
        using var client = host.GetTestClient();
        var token = CreateToken(signingKey, Issuer, Audience, DateTime.UtcNow.AddMinutes(5), "StoreManager");

        using var response = await SendAsync(client, token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static IHost CreateHost(RSA signingKey)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Authority"] = Issuer,
                ["Authentication:Audience"] = Audience
            })
            .Build();

        return new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddCreatorAuthentication(configuration);
                    services.Configure<JwtBearerOptions>(
                        JwtBearerDefaults.AuthenticationScheme,
                        options =>
                        {
                            options.BackchannelHttpHandler = new LocalOidcJwksHandler(signingKey);
                            options.BackchannelTimeout = TimeSpan.FromSeconds(5);
                        });
                    services.AddCreatorAuthorization();
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/creator", (HttpContext context) => Results.Json(new
                        {
                            issuer = context.User.FindFirst("iss")?.Value,
                            subject = context.User.FindFirst("sub")?.Value,
                            role = context.User.FindFirst(CreatorAuthorizationExtensions.PlatformRoleClaim)?.Value
                        })).RequireAuthorization(CreatorAuthorizationExtensions.PolicyName);
                    });
                });
            })
            .Start();
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/creator");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static string CreateToken(
        RSA signingKey,
        string issuer,
        string audience,
        DateTime expiresUtc,
        string role)
    {
        var securityKey = new RsaSecurityKey(signingKey) { KeyId = KeyId };
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer,
            audience,
            new[]
            {
                new Claim("sub", "creator-oidc-123"),
                new Claim(CreatorAuthorizationExtensions.PlatformRoleClaim, role)
            },
            expiresUtc <= now ? expiresUtc.AddMinutes(-10) : now.AddMinutes(-1),
            expiresUtc,
            credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class LocalOidcJwksHandler(RSA signingKey) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath;
            if (path == "/.well-known/openid-configuration")
            {
                return Task.FromResult(JsonResponse(JsonSerializer.Serialize(new
                {
                    issuer = Issuer,
                    jwks_uri = $"{Issuer}/.well-known/jwks.json"
                })));
            }

            if (path == "/.well-known/jwks.json")
            {
                var parameters = signingKey.ExportParameters(false);
                var jwks = JsonSerializer.Serialize(new
                {
                    keys = new[]
                    {
                        new
                        {
                            kty = "RSA",
                            use = "sig",
                            kid = KeyId,
                            alg = SecurityAlgorithms.RsaSha256,
                            n = Base64UrlEncoder.Encode(parameters.Modulus!),
                            e = Base64UrlEncoder.Encode(parameters.Exponent!)
                        }
                    }
                });
                return Task.FromResult(JsonResponse(jwks));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage JsonResponse(string json) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
    }
}
