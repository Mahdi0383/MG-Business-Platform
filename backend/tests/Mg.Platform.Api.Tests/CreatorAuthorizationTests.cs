using System.Security.Claims;
using Mg.Platform.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mg.Platform.Api.Tests;

public sealed class CreatorAuthorizationTests
{
    [Theory]
    [InlineData("PlatformOwner")]
    [InlineData("PlatformAdministrator")]
    public async Task AllowsOnlyExplicitPlatformAdministratorRoles(string role)
    {
        using var provider = CreateServices();
        var user = AuthenticatedUser(
            new Claim(CreatorAuthorizationExtensions.PlatformRoleClaim, role));

        var result = await provider.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(user, resource: null, CreatorAuthorizationExtensions.PolicyName);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("BusinessOwner")]
    [InlineData("PlatformSupport")]
    public async Task RejectsOtherAuthenticatedPlatformRoles(string role)
    {
        using var provider = CreateServices();
        var user = AuthenticatedUser(
            new Claim(CreatorAuthorizationExtensions.PlatformRoleClaim, role));

        var result = await provider.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(user, resource: null, CreatorAuthorizationExtensions.PolicyName);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task RejectsAuthenticatedUserWithoutPlatformRoleClaim()
    {
        using var provider = CreateServices();

        var result = await provider.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(AuthenticatedUser(), resource: null,
                CreatorAuthorizationExtensions.PolicyName);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task RejectsUnauthenticatedPrincipalEvenWithPlatformRoleClaim()
    {
        using var provider = CreateServices();
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(CreatorAuthorizationExtensions.PlatformRoleClaim, "PlatformOwner")
        });
        var user = new ClaimsPrincipal(identity);

        var result = await provider.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(user, resource: null, CreatorAuthorizationExtensions.PolicyName);

        Assert.False(result.Succeeded);
    }

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCreatorAuthorization();
        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal AuthenticatedUser(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "test"));
}
