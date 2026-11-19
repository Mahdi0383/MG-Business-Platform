using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Mg.Platform.Api.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Mg.Platform.Api.Tests;

public sealed class CreatorOidcBearerIntegrationTests
{
    private const string Issuer = "https://identity.example.test/realms/mg";
    private const string Audience = "mg-platform-api";

    [Fact]
    public async Task AcceptsSignedTokenWithValidIssuerAudienceAndCreatorRole()
    {
        using var rsa = RSA.Create(2048);
        await using var server = await CreateServer(rsa);
        using var client = server.GetTestClient();
        var token = CreateToken(rsa, Issuer, Audience, "PlatformOwner");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/secure");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("authorized", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RejectsTokenWithWrongIssuer()
    {
        using var rsa = RSA.Create(2048);
        await using var server = await CreateServer(rsa);
        using var client = server.GetTestClient();
        var token = CreateToken(rsa, "https://attacker.example.test", Audience, "PlatformOwner");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/secure");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RejectsTokenWithWrongAudience()
    {
        using var rsa = RSA.Create(2048);
        await using var server = await CreateServer(rsa);
        using var client = server.GetTestClient();
        var token = CreateToken(rsa, Issuer, "another-api", "PlatformOwner");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/secure");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RejectsValidTokenWithoutCreatorAdministratorRole()
    {
        using var rsa = RSA.Create(2048);
        await using var server = await CreateServer(rsa);
        using var client = server.GetTestClient();
        var token = CreateToken(rsa, Issuer, Audience, "BusinessOwner");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/secure");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectsExpiredToken()
    {
        using var rsa = RSA.Create(2048);
        await using var server = await CreateServer(rsa);
        using var client = server.GetTestClient();
        var token = CreateToken(rsa, Issuer, Audience, "PlatformOwner", expiresAt: DateTime.UtcNow.AddMinutes(-2));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/secure");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ForbidsAuthenticatedTokenWithoutPlatformRoleClaim()
    {
        using var rsa = RSA.Create(2048);
        await using var server = await CreateServer(rsa);
        using var client = server.GetTestClient();
        var token = CreateToken(rsa, Issuer, Audience, null);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/secure");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectsTokenSignedByUnknownKey()
    {
        using var trustedRsa = RSA.Create(2048);
        using var untrustedRsa = RSA.Create(2048);
        await using var server = await CreateServer(trustedRsa);
        using var client = server.GetTestClient();
        var token = CreateToken(untrustedRsa, Issuer, Audience, "PlatformOwner");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/secure");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<WebApplication> CreateServer(RSA rsa)
    {
        var signingKey = new RsaSecurityKey(rsa) { KeyId = "mg-test-signing-key" };
        var oidcConfiguration = new OpenIdConnectConfiguration { Issuer = Issuer };
        oidcConfiguration.SigningKeys.Add(signingKey);
        var configurationManager =
            new StaticConfigurationManager<OpenIdConnectConfiguration>(oidcConfiguration);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Authority"] = Issuer,
                ["Authentication:Audience"] = Audience
            })
            .Build();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddCreatorAuthentication(configuration);
        builder.Services.PostConfigure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options => options.ConfigurationManager = configurationManager);
        builder.Services.AddCreatorAuthorization();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/secure", async context =>
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsync("authorized");
        }).RequireAuthorization(CreatorAuthorizationExtensions.PolicyName);

        await app.StartAsync();
        return app;
    }

    private static string CreateToken(RSA rsa, string issuer, string audience, string? platformRole, DateTime? expiresAt = null)
    {
        var key = new RsaSecurityKey(rsa) { KeyId = "mg-test-signing-key" };
        var credentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
        var token = new JwtSecurityToken(
            issuer,
            audience,
            platformRole is null ? [] : [new Claim(CreatorAuthorizationExtensions.PlatformRoleClaim, platformRole)],
            DateTime.UtcNow.AddMinutes(-1),
            expiresAt ?? DateTime.UtcNow.AddMinutes(5),
            credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
