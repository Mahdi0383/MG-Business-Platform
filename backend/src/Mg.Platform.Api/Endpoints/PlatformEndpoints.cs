using Microsoft.AspNetCore.RateLimiting;
using Mg.Platform.Api.Contracts;

namespace Mg.Platform.Api.Endpoints;

public static class PlatformEndpoints
{
    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/platform/businesses")
            .WithTags("MG Creator — Business Registry")
            .RequireAuthorization("mg-creator")
            .RequireRateLimiting("creator-bootstrap");

        group.MapGet("/", async (BusinessRegistryService registry, CancellationToken ct) =>
            Results.Ok(await registry.ListAsync(ct)));

        group.MapPost("/", async (CreateBusinessRequest request, HttpContext httpContext,
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
            if (!Guid.TryParse(httpContext.Request.Headers["Idempotency-Key"], out var idempotencyKey)
                || idempotencyKey == Guid.Empty)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Idempotency-Key"] = ["A non-empty UUID Idempotency-Key header is required."]
                });

            var issuer = httpContext.User.FindFirst("iss")?.Value;
            var subject = httpContext.User.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
                return Results.Forbid();

            var normalizedRequest = request with { Name = request.Name.Trim(), Slug = normalizedSlug };
            var (business, conflict, replayed) = await registry.CreateAsync(
                normalizedRequest, idempotencyKey, issuer, subject, ct);
            if (conflict)
                return Results.Conflict(new { code = "business_creation_conflict" });

            if (replayed)
                httpContext.Response.Headers["Idempotency-Replayed"] = "true";

            return Results.Created($"/api/v1/platform/businesses/{business!.Id}", business);
        });

        return endpoints;
    }
}
