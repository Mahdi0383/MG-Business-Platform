using System.Security.Cryptography;
using System.Text;
using Mg.Platform.Api.Data;
using Mg.Platform.Api.Endpoints;
using Mg.Platform.Api.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Platform")
    ?? throw new InvalidOperationException("ConnectionStrings:Platform is required.");

builder.Services.AddDbContext<PlatformDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure()));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<PlatformDbContext>("platform-database", HealthStatus.Unhealthy);
builder.Services.AddScoped<BusinessRegistryService>();
builder.Services.AddSingleton<PlatformKeyAuthenticator>();

var app = builder.Build();

app.UseExceptionHandler();
app.MapOpenApi();

app.MapGet("/health/live", () => Results.Ok(new { status = "live", service = "mg-platform-api" }))
    .AllowAnonymous();
app.MapHealthChecks("/health/ready");

app.MapPlatformEndpoints();

app.Run();

public partial class Program;
