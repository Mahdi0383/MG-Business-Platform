using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Mg.Platform.Api.Data;
using Mg.Platform.Api.Endpoints;
using Mg.Platform.Api.Security;
using Mg.Platform.Api.Secrets;
using Mg.Platform.Api.Provisioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Platform")
    ?? throw new InvalidOperationException("ConnectionStrings:Platform is required.");

builder.Services.AddDbContext<PlatformDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure()));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddCreatorAuthentication(builder.Configuration);
builder.Services.AddCreatorAuthorization();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<PlatformDbContext>("platform-database", HealthStatus.Unhealthy);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("creator-bootstrap", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});
builder.Services.AddScoped<BusinessRegistryService>();
builder.Services.AddScoped<TenantProvisioningLeaseService>();
builder.Services.AddHttpClient<ITenantSecretStore, VaultKv2TenantSecretStore>(client =>
    client.Timeout = TimeSpan.FromSeconds(30))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    });
var provisioningAgentOptions = TenantProvisioningAgentOptions.From(builder.Configuration);
if (provisioningAgentOptions is not null)
{
    builder.Services.AddSingleton(provisioningAgentOptions);
    builder.Services.AddHttpClient<ITenantDatabaseProvisioner, HttpTenantDatabaseProvisioner>(client =>
        {
            client.BaseAddress = provisioningAgentOptions.Address;
            client.Timeout = TimeSpan.FromSeconds(90);
        })
        .ConfigurePrimaryHttpMessageHandler(services =>
        {
            var options = services.GetRequiredService<TenantProvisioningAgentOptions>();
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                CheckCertificateRevocationList = true,
                ClientCertificateOptions = ClientCertificateOption.Manual
            };
            handler.ClientCertificates.Add(options.ClientCertificate);
            return handler;
        });
    builder.Services.AddScoped<ITenantProvisioningHandler, TenantProvisioningHandler>();
}
if (builder.Configuration.GetValue<bool>("Provisioning:WorkerEnabled"))
{
    if (!builder.Services.Any(descriptor => descriptor.ServiceType == typeof(ITenantProvisioningHandler)))
        throw new InvalidOperationException("Provisioning worker cannot be enabled until a reviewed ITenantProvisioningHandler is registered.");
    builder.Services.AddHostedService<TenantProvisioningWorker>();
}

var app = builder.Build();

app.UseExceptionHandler();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapOpenApi();

app.MapGet("/health/live", () => Results.Ok(new { status = "live", service = "mg-platform-api" }))
    .AllowAnonymous();
app.MapHealthChecks("/health/ready");

app.MapPlatformEndpoints();

app.Run();

public partial class Program { }
