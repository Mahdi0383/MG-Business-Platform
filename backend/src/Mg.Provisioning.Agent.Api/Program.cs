using System.Text.Json;
using System.Threading.RateLimiting;
using Mg.Provisioning.Agent.Api;
using Mg.Provisioning.Agent.Core;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
var hostOptions = AgentHostOptions.From(builder.Configuration);
var provisioningHandler = NpgsqlAgentProvisioningHandler.TryCreate(
    builder.Configuration, AppContext.BaseDirectory);
if (provisioningHandler is not null)
    builder.Services.AddSingleton<IAgentProvisioningHandler>(provisioningHandler);

builder.WebHost.ConfigureKestrel(server =>
{
    server.Limits.MaxRequestBodySize = 4096;
    server.ListenAnyIP(hostOptions.Port, listen => listen.UseHttps(https =>
    {
        https.ServerCertificate = hostOptions.ServerCertificate;
        https.ClientCertificateMode = Microsoft.AspNetCore.Server.Kestrel.Https.ClientCertificateMode.RequireCertificate;
        https.CheckCertificateRevocation = true;
        https.ClientCertificateValidation = hostOptions.ValidateClientCertificate;
    }));
});

builder.Services.AddSingleton(hostOptions);
builder.Services.AddRateLimiter(rateLimiter =>
{
    rateLimiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    rateLimiter.AddPolicy("agent-provisioning", context =>
    {
        var thumbprint = context.Connection.ClientCertificate?.Thumbprint ?? "missing-certificate";
        return RateLimitPartition.GetFixedWindowLimiter(
            thumbprint,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 12,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
});

var app = builder.Build();
app.Lifetime.ApplicationStopping.Register(hostOptions.Dispose);
if (provisioningHandler is not null)
    app.Lifetime.ApplicationStopping.Register(provisioningHandler.Dispose);
app.UseRateLimiter();

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", async (HttpContext context, ILoggerFactory loggerFactory) =>
{
    var handler = context.RequestServices.GetService<IAgentProvisioningHandler>();
    if (handler is null)
        return Results.Json(new { status = "not-ready", code = "PROVISIONER_NOT_CONFIGURED" },
            statusCode: StatusCodes.Status503ServiceUnavailable);

    try
    {
        return await handler.IsReadyAsync(context.RequestAborted)
            ? Results.Ok(new { status = "ready" })
            : Results.Json(new { status = "not-ready", code = "AGENT_DEPENDENCIES_NOT_READY" },
                statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception exception)
    {
        loggerFactory.CreateLogger("AgentReadiness").LogError(
            "Agent readiness check failed. ExceptionType={ExceptionType}", exception.GetType().Name);
        return Results.Json(new { status = "not-ready", code = "AGENT_DEPENDENCIES_NOT_READY" },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPost("/internal/v1/tenant-databases", async (HttpContext context, ILoggerFactory loggerFactory) =>
{
    if (!context.Request.HasJsonContentType())
        return Results.Json(new { code = "CONTENT_TYPE_INVALID" }, statusCode: StatusCodes.Status415UnsupportedMediaType);

    if (context.Request.ContentLength is > 4096)
        return Results.Json(new { code = "REQUEST_TOO_LARGE" }, statusCode: StatusCodes.Status413PayloadTooLarge);

    if (context.Connection.ClientCertificate is null)
        return Results.Json(new { code = "CLIENT_CERTIFICATE_REQUIRED" }, statusCode: StatusCodes.Status401Unauthorized);

    AgentRequestDto? dto;
    try
    {
        using var document = await JsonDocument.ParseAsync(
            context.Request.Body,
            new JsonDocumentOptions { MaxDepth = 4, CommentHandling = JsonCommentHandling.Disallow },
            context.RequestAborted);
        if (!TryParseRequest(document.RootElement, out dto))
            return Results.Json(new { code = "REQUEST_INVALID" }, statusCode: StatusCodes.Status400BadRequest);
    }
    catch (JsonException)
    {
        return Results.Json(new { code = "REQUEST_INVALID" }, statusCode: StatusCodes.Status400BadRequest);
    }

    var expectedDatabase = $"mg_tenant_{dto!.TenantId:N}";
    if (dto.TenantId == Guid.Empty
        || dto.IdempotencyKey == Guid.Empty
        || !string.Equals(dto.DatabaseName, expectedDatabase, StringComparison.Ordinal))
        return Results.Json(new { code = "REQUEST_INVALID" }, statusCode: StatusCodes.Status400BadRequest);

    var handler = context.RequestServices.GetService<IAgentProvisioningHandler>();
    if (handler is null)
        return Results.Json(new { code = "PROVISIONER_NOT_CONFIGURED" },
            statusCode: StatusCodes.Status503ServiceUnavailable);

    try
    {
        var connectionString = await handler.ProvisionAsync(
            new AgentProvisioningRequest(dto.TenantId, dto.IdempotencyKey), context.RequestAborted);
        if (string.IsNullOrWhiteSpace(connectionString))
            return Results.Json(new { code = "PROVISIONING_RESULT_INVALID" },
                statusCode: StatusCodes.Status500InternalServerError);

        return Results.Json(new { connectionString });
    }
    catch (AgentProvisioningBusyException)
    {
        return Results.Json(new { code = "PROVISIONING_BUSY" }, statusCode: StatusCodes.Status429TooManyRequests);
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception exception)
    {
        // Exception messages may contain credentials, SQL or provider details; never log them.
        loggerFactory.CreateLogger("AgentProvisioning").LogError(
            "Tenant provisioning failed. ExceptionType={ExceptionType}", exception.GetType().Name);
        return Results.Json(new { code = "PROVISIONING_FAILED" },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
})
.RequireRateLimiting("agent-provisioning");

app.Run();

static bool TryParseRequest(JsonElement root, out AgentRequestDto? request)
{
    request = null;
    if (root.ValueKind != JsonValueKind.Object)
        return false;

    var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
    foreach (var property in root.EnumerateObject())
    {
        if (property.Name is not ("tenantId" or "idempotencyKey" or "databaseName")
            || !values.TryAdd(property.Name, property.Value))
            return false;
    }

    if (values.Count != 3
        || !values.TryGetValue("tenantId", out var tenantValue)
        || !values.TryGetValue("idempotencyKey", out var keyValue)
        || !values.TryGetValue("databaseName", out var databaseValue)
        || tenantValue.ValueKind != JsonValueKind.String
        || keyValue.ValueKind != JsonValueKind.String
        || databaseValue.ValueKind != JsonValueKind.String
        || !Guid.TryParseExact(tenantValue.GetString(), "D", out var tenantId)
        || !Guid.TryParseExact(keyValue.GetString(), "D", out var idempotencyKey))
        return false;

    request = new AgentRequestDto(tenantId, idempotencyKey, databaseValue.GetString() ?? string.Empty);
    return true;
}

internal sealed record AgentRequestDto(Guid TenantId, Guid IdempotencyKey, string DatabaseName);

public partial class Program { }
