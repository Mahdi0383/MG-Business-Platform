using Microsoft.AspNetCore.RateLimiting;
using Mg.Platform.Api.Contracts;

namespace Mg.Platform.Api.Endpoints;

public static class PlatformEndpoints
{
    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/platform/businesses")
            .WithTags("MG Creator — Business Registry")
            .RequireAuthorization(CreatorAuthorizationExtensions.PolicyName)
            .RequireRateLimiting("creator-bootstrap");

        group.MapGet("/", async (BusinessRegistryService registry, CancellationToken ct) =>
            Results.Ok(await registry.ListAsync(ct)));

        group.MapPost("/", async (CreateBusinessRequest request,
            BusinessRegistryService registry, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 160)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["name"] = ["نام کسب‌وکار الزامی است و حداکثر ۱۶۰ نویسه دارد."]
                });
            if (!BusinessSlug.TryNormalize(request.Slug, out var normalizedSlug))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["slug"] = ["شناسه باید فقط شامل حروف کوچک انگلیسی، عدد و خط تیره باشد."]
                });

            var normalizedRequest = request with { Slug = normalizedSlug };
            var (business, conflict) = await registry.CreateAsync(normalizedRequest, ct);
            if (conflict) return Results.Conflict(new { code = "business_slug_conflict" });
            return Results.Created($"/api/v1/platform/businesses/{business!.Id}", business);
        });

        return endpoints;
    }
}
