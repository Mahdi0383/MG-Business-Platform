using Microsoft.Extensions.DependencyInjection;

namespace Mg.Platform.Api.Security;

/// <summary>
/// Registers the authorization boundary for MG Creator control-plane operations.
/// </summary>
public static class CreatorAuthorizationExtensions
{
    public const string PolicyName = "mg-creator";
    public const string PlatformRoleClaim = "mg_platform_role";

    public static IServiceCollection AddCreatorAuthorization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAuthorization(options =>
        {
            options.AddPolicy(PolicyName, policy =>
                policy.RequireAuthenticatedUser()
                    .RequireClaim(PlatformRoleClaim, "PlatformOwner", "PlatformAdministrator"));
        });

        return services;
    }
}
