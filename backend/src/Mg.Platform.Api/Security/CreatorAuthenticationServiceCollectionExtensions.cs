using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Mg.Platform.Api.Security;

/// <summary>
/// Registers the validated OIDC bearer-token authentication used by MG Creator.
/// </summary>
public static class CreatorAuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddCreatorAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        var authentication = CreatorAuthenticationConfiguration.From(configuration);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authentication.Authority.AbsoluteUri.TrimEnd('/');
                options.Audience = authentication.Audience;
                options.RequireHttpsMetadata = true;
                options.MapInboundClaims = false;
            });

        return services;
    }
}
