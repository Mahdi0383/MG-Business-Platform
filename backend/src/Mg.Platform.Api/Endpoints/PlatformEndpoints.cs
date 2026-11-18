using System.Text.RegularExpressions;
using Mg.Platform.Api.Contracts;
using Mg.Platform.Api.Security;

namespace Mg.Platform.Api.Endpoints;

public static partial class PlatformEndpoints
{
    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,78}[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();

    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/platform/businesses")
            .WithTags("MG Creator — Business Registry");

        group.MapGet("/", async (HttpRequest request, PlatformKeyAuthenticator auth,
            BusinessRegistryService registry, CancellationToken ct) =>
        {
            if (!Authorize(request, auth)) return Results.Unauthorized();
            return Results.Ok(await registry.ListAsync(ct));
        });

        group.MapPost("/", async (CreateBusinessRequest request, HttpRequest http,
            PlatformKeyAuthenticator auth, BusinessRegistryService registry, CancellationToken ct) =>
        {
            if (!Authorize(http, auth)) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 160)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["name"] = ["نام کسب‌وکار الزامی است و حداکثر ۱۶۰ نویسه دارد."]
                });
            if (string.IsNullOrWhiteSpace(request.Slug) || !SlugPattern().IsMatch(request.Slug.Trim().ToLowerInvariant()))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["slug"] = ["شناسه باید فقط شامل حروف کوچک انگلیسی، عدد و خط تیره باشد."]
                });

            var (business, conflict) = await registry.CreateAsync(request, ct);
            if (conflict) return Results.Conflict(new { code = "business_slug_conflict" });
            return Results.Created($"/api/v1/platform/businesses/{business!.Id}", business);
        });

        return endpoints;
    }

    private static bool Authorize(HttpRequest request, PlatformKeyAuthenticator auth) =>
        auth.IsAuthorized(request.Headers["X-MG-Platform-Key"].FirstOrDefault());
}
