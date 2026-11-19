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
        using var server = CreateServer(rsa);
        using var client = server.CreateClient();
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
        using var server = CreateServer(rsa);
        using var client = server.CreateClient();
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
        using var server = CreateServer(rsa);
        using var client = server.CreateClient();
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
        using var server = CreateServer(rsa);
        using var client = server.CreateClient();
        var token = CreateToken(rsa, Issuer, Audience, "BusinessOwner");

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
        using var server = CreateServer(trustedRsa);
        using var client = server.CreateClient();
        var token = CreateToken(untrustedRsa, Issuer, Audience, "PlatformOwner");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/secure");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static TestServer CreateServer(RSA rsa)
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

        return new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddCreatorAuthentication(configuration);
                services.PostConfigure<JwtBearerOptions>(
                    JwtBearerDefaults.AuthenticationScheme,
                    options => options.ConfigurationManager = configurationManager);
                services.AddCreatorAuthorization();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints =>
                    endpoints.MapGet("/secure", async context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status200OK;
                        await context.Response.WriteAsync("authorized");
                    }).RequireAuthorization(CreatorAuthorizationExtensions.PolicyName));
            }));
    }

    private static string CreateToken(RSA rsa, string issuer, string audience, string platformRole)
    {
        var key = new RsaSecurityKey(rsa) { KeyId = "mg-test-signing-key" };
        var credentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
        var token = new JwtSecurityToken(
            issuer,
            audience,
            [new Claim(CreatorAuthorizationExtensions.PlatformRoleClaim, platformRole)],
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(5),
            credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
