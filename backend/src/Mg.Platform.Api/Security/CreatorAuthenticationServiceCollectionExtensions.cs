using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

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

        var useLastKnownGoodConfiguration =
            configuration.GetValue("Authentication:UseLastKnownGoodConfiguration", true);
        services.PostConfigure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options =>
            {
                if (options.ConfigurationManager is not BaseConfigurationManager manager)
                {
                    throw new InvalidOperationException(
                        "The OIDC configuration manager does not support last-known-good configuration controls.");
                }

                manager.UseLastKnownGoodConfiguration = useLastKnownGoodConfiguration;
            });

        return services;
    }
}
