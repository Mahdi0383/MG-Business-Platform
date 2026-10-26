using System.Net;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;
using MG.BusinessPlatform.Infrastructure.Persistence;
using MG.BusinessPlatform.Licensing;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 64 * 1024);

var configuredPath = Environment.GetEnvironmentVariable("MG_BUSINESS_DB_PATH")
    ?? builder.Configuration["Storefront:DatabasePath"]
    ?? "data/mg-business-platform.db";
var databasePath = Path.GetFullPath(Path.IsPathRooted(configuredPath)
    ? configuredPath
    : Path.Combine(builder.Environment.ContentRootPath, configuredPath));
Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

builder.Services.AddDbContext<BusinessDbContext>(options =>
    options.UseSqlite($"Data Source={databasePath};Cache=Shared;Default Timeout=15"));
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<IStorefrontLicenseGuard, StorefrontLicenseGuard>();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
        | ForwardedHeaders.XForwardedProto
        | ForwardedHeaders.XForwardedHost;
    options.ForwardLimit = 1;
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("public", context =>
    {
        var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(remoteIp, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
});

var app = builder.Build();

// Trust forwarded client/host metadata only from the loopback Caddy proxy.
// This keeps per-client rate limiting meaningful without trusting internet-supplied X-Forwarded-* headers.
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    context.Response.Headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
    context.Response.Headers.TryAdd("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    context.Response.Headers.TryAdd("Content-Security-Policy",
        "default-src 'self'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'; object-src 'none'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; connect-src 'self'");
    await next();
});

// Browser-based state-changing requests must originate from this storefront's own
// origin. Non-browser API clients without an Origin header remain supported; when
// browsers send Fetch Metadata, explicitly reject cross-site mutations as defense in depth.
app.Use(async (context, next) =>
{
    var isMutation = HttpMethods.IsPost(context.Request.Method)
        || HttpMethods.IsPut(context.Request.Method)
        || HttpMethods.IsPatch(context.Request.Method)
        || HttpMethods.IsDelete(context.Request.Method);
    if (isMutation)
    {
        var fetchSite = context.Request.Headers["Sec-Fetch-Site"].ToString();
        var originHeader = context.Request.Headers["Origin"].ToString();
        var crossSiteFetch = string.Equals(fetchSite, "cross-site", StringComparison.OrdinalIgnoreCase);
        var originMismatch = false;

        if (!string.IsNullOrWhiteSpace(originHeader))
        {
            var expectedOriginText = $"{context.Request.Scheme}://{context.Request.Host}";
            originMismatch = !Uri.TryCreate(originHeader, UriKind.Absolute, out var origin)
                || !Uri.TryCreate(expectedOriginText, UriKind.Absolute, out var expectedOrigin)
                || !string.Equals(origin.Scheme, expectedOrigin.Scheme, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(origin.Host, expectedOrigin.Host, StringComparison.OrdinalIgnoreCase)
                || origin.Port != expectedOrigin.Port;
        }

        if (crossSiteFetch || originMismatch)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(
                new { error = "Cross-origin state-changing requests are not allowed." },
                context.RequestAborted);
            return;
        }
    }

    await next();
});

// Apply endpoint rate limits before the host allow-list performs a database lookup.
app.UseRateLimiter();

// The customer-facing process enforces the signed license itself, including after
// the desktop UI closes or a license expires while this process remains alive.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/health"))
    {
        await next();
        return;
    }

    var validation = context.RequestServices.GetRequiredService<IStorefrontLicenseGuard>().Validate();
    if (!validation.IsValid || validation.Payload is null)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new { error = "The store's software license is missing, invalid, or expired." }, context.RequestAborted);
        return;
    }

    if (!validation.Payload.Modules.Contains("Storefront", StringComparer.Ordinal))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { error = "The current license does not include the online storefront module." }, context.RequestAborted);
        return;
    }

    if (HttpMethods.IsPost(context.Request.Method)
        && context.Request.Path.StartsWithSegments("/api/storefront/orders")
        && (!validation.Payload.Modules.Contains("Accounting", StringComparer.Ordinal)
            || !validation.Payload.Modules.Contains("Inventory", StringComparer.Ordinal)))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(
            new { error = "Online ordering requires both Accounting and Inventory entitlements so orders can be fulfilled consistently." },
            context.RequestAborted);
        return;
    }

    if ((context.Request.Path.StartsWithSegments("/api/storefront/reservations")
            || context.Request.Path.StartsWithSegments("/api/storefront/reservation-resources"))
        && !validation.Payload.Modules.Contains("Reservations", StringComparer.Ordinal))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { error = "The current license does not include online reservations." }, context.RequestAborted);
        return;
    }

    await next();
});

app.Use(async (context, next) =>
{
    var host = context.Request.Host.Host;
    var isLocal = host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    var db = context.RequestServices.GetRequiredService<BusinessDbContext>();
    var settings = await db.StoreSettings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, context.RequestAborted);
    var configured = settings is not null && !string.IsNullOrWhiteSpace(settings.PublicHostname)
        && host.Equals(settings.PublicHostname, StringComparison.OrdinalIgnoreCase);
    if (!isLocal && !configured)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = "Unrecognized host name." }, context.RequestAborted);
        return;
    }

    await next();
});

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
    await DatabaseInitializer.InitializeAsync(db);
}

app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous().RequireRateLimiting("public");
app.MapGet("/health/ready", async (BusinessDbContext db, CancellationToken ct) =>
{
    var canConnect = await db.Database.CanConnectAsync(ct);
    return canConnect
        ? Results.Ok(new { status = "ready" })
        : Results.Problem("Database is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
}).RequireRateLimiting("public");

app.MapGet("/api/storefront", async (BusinessDbContext db, IStorefrontLicenseGuard licenseGuard, CancellationToken ct) =>
{
    var settings = await db.StoreSettings.AsNoTracking().SingleAsync(x => x.Id == 1, ct);
    var license = licenseGuard.Validate();
    var canOrder = license.Payload is { } payload
        && payload.Modules.Contains("Accounting", StringComparer.Ordinal)
        && payload.Modules.Contains("Inventory", StringComparer.Ordinal);
    var reservationsEnabled = settings.OnlineReservationsEnabled
        && license.Payload?.Modules.Contains("Reservations", StringComparer.Ordinal) == true;
    return Results.Ok(new StorefrontInfo(settings.StoreName, settings.CurrencyCode,
        settings.OnlineOrderingEnabled && canOrder, reservationsEnabled));
}).RequireRateLimiting("public");

app.MapGet("/api/storefront/products", async (
    BusinessDbContext db, IStorefrontLicenseGuard licenseGuard, CancellationToken ct) =>
{
    // Do not expose the shop's catalogue or pricing while online ordering is disabled or
    // its license lacks the accounting/inventory modules required to fulfill an order.
    // Returning an empty array preserves the customer page contract for reservation-only stores.
    var settings = await db.StoreSettings.AsNoTracking().SingleAsync(x => x.Id == 1, ct);
    var license = licenseGuard.Validate();
    var canOrder = license.Payload is { } payload
        && payload.Modules.Contains("Accounting", StringComparer.Ordinal)
        && payload.Modules.Contains("Inventory", StringComparer.Ordinal);
    if (!settings.OnlineOrderingEnabled || !canOrder)
        return Results.Ok(Array.Empty<ProductResponse>());

    var products = await db.StoreProducts.AsNoTracking()
        .Where(x => x.IsAvailable)
        .OrderBy(x => x.Name)
        .Take(500)
        .ToListAsync(ct);
    var productIds = products.Select(product => product.Id).ToArray();
    var addonRows = await (
        from link in db.StoreProductAddons.AsNoTracking()
        join addon in db.ProductAddons.AsNoTracking() on link.AddonId equals addon.Id
        where productIds.Contains(link.ProductId) && addon.IsActive
        select new { link.ProductId, Addon = addon }).ToListAsync(ct);
    var addonsByProduct = addonRows
        .GroupBy(row => row.ProductId)
        .ToDictionary(group => group.Key, group => group
            .OrderBy(row => row.Addon.Name)
            .Select(row => new ProductAddonResponse(row.Addon.Id, row.Addon.Name, row.Addon.Price))
            .ToArray());
    return Results.Ok(products.Select(product => new ProductResponse(
        product.Id, product.Sku, product.Name, product.Description, product.UnitPrice,
        product.CurrencyCode, product.StockQuantity > 0,
        addonsByProduct.GetValueOrDefault(product.Id) ?? Array.Empty<ProductAddonResponse>())).ToArray());
}).RequireRateLimiting("public");

app.MapGet("/api/storefront/reservation-resources", async (BusinessDbContext db, CancellationToken ct) =>
{
    var settings = await db.StoreSettings.AsNoTracking().SingleAsync(x => x.Id == 1, ct);
    if (!settings.OnlineReservationsEnabled)
        return Results.Problem("Online reservations are not enabled by the store.", statusCode: StatusCodes.Status503ServiceUnavailable);

    var resources = await db.ReservationResources.AsNoTracking()
        .Where(x => x.IsAvailable)
        .OrderBy(x => x.Name)
        .Select(x => new ReservationResourceResponse(x.Id, x.Name, x.ResourceType, x.Capacity))
        .Take(200)
        .ToListAsync(ct);
    return Results.Ok(resources);
}).RequireRateLimiting("public");

app.MapPost("/api/storefront/reservations", async (
    HttpContext context,
    CreateReservationRequest? request,
    BusinessDbContext db,
    CancellationToken ct) =>
{
    var settings = await db.StoreSettings.AsNoTracking().SingleAsync(x => x.Id == 1, ct);
    if (!settings.OnlineReservationsEnabled)
        return Results.Problem("Online reservations are not enabled by the store.", statusCode: StatusCodes.Status503ServiceUnavailable);
    if (request is null)
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["Request"] = ["A valid reservation request is required."]
        });

    var key = context.Request.Headers["Idempotency-Key"].ToString().Trim();
    if (key.Length is < 16 or > 100 || key.Any(char.IsControl))
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["Idempotency-Key"] = ["A 16-100 character idempotency key is required."]
        });

    if (request.ResourceId == Guid.Empty)
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["ResourceId"] = ["Choose a valid reservation resource."]
        });
    if (string.IsNullOrWhiteSpace(request.GuestName) || request.GuestName.Trim().Length > 200)
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["GuestName"] = ["Guest name is required and must not exceed 200 characters."]
        });
    if (string.IsNullOrWhiteSpace(request.GuestPhone) || request.GuestPhone.Trim().Length > 40)
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["GuestPhone"] = ["Guest phone is required and must not exceed 40 characters."]
        });
    if (request.GuestCount is < 1 or > 1000 || (request.Notes?.Length ?? 0) > 1000)
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["GuestCount"] = ["Guest count must be positive and notes cannot exceed 1000 characters."]
        });

    var startsAt = request.StartsAt.ToUniversalTime();
    var endsAt = request.EndsAt.ToUniversalTime();
    var now = DateTimeOffset.UtcNow;
    if (startsAt < now.AddMinutes(5) || startsAt > now.AddDays(90)
        || endsAt <= startsAt || endsAt - startsAt < TimeSpan.FromMinutes(30)
        || endsAt - startsAt > TimeSpan.FromHours(8))
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["StartsAt"] = ["Choose a start time at least 5 minutes from now and within 90 days; duration must be between 30 minutes and 8 hours."]
        });

    var existing = await db.CustomerReservations.AsNoTracking()
        .SingleOrDefaultAsync(x => x.IdempotencyKey == key, ct);
    if (existing is not null)
    {
        var sameRequest = existing.ResourceId == request.ResourceId
            && existing.GuestName == request.GuestName.Trim()
            && existing.GuestPhone == request.GuestPhone.Trim()
            && existing.GuestCount == request.GuestCount
            && existing.StartsAtUtc == startsAt
            && existing.EndsAtUtc == endsAt
            && existing.Notes == (request.Notes?.Trim() ?? string.Empty);
        if (!sameRequest)
            return Results.Conflict(new { error = "This idempotency key was already used for a different reservation request." });
        return Results.Ok(new ReservationCreatedResponse(existing.Id, existing.ReservationNumber,
            existing.ResourceId, existing.StartsAtUtc, existing.EndsAtUtc, existing.Status));
    }

    try
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // The initial idempotency lookup is a fast path, not a concurrency guarantee.
        // Recheck after opening the write transaction so a simultaneous retry returns
        // the first committed reservation instead of trying to create another one.
        var existingInTransaction = await db.CustomerReservations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.IdempotencyKey == key, ct);
        if (existingInTransaction is not null)
        {
            var sameRequest = existingInTransaction.ResourceId == request.ResourceId
                && existingInTransaction.GuestName == request.GuestName.Trim()
                && existingInTransaction.GuestPhone == request.GuestPhone.Trim()
                && existingInTransaction.GuestCount == request.GuestCount
                && existingInTransaction.StartsAtUtc == startsAt
                && existingInTransaction.EndsAtUtc == endsAt
                && existingInTransaction.Notes == (request.Notes?.Trim() ?? string.Empty);
            if (!sameRequest)
                return Results.Conflict(new { error = "This idempotency key was already used for a different reservation request." });
            await transaction.CommitAsync(ct);
            return Results.Ok(new ReservationCreatedResponse(existingInTransaction.Id, existingInTransaction.ReservationNumber,
                existingInTransaction.ResourceId, existingInTransaction.StartsAtUtc, existingInTransaction.EndsAtUtc, existingInTransaction.Status));
        }

        var resource = await db.ReservationResources.SingleOrDefaultAsync(
            x => x.Id == request.ResourceId && x.IsAvailable, ct);
        if (resource is null)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["ResourceId"] = ["This reservation resource is unavailable. Refresh the list and try again."]
            });
        if (request.GuestCount > resource.Capacity)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["GuestCount"] = [$"This resource supports up to {resource.Capacity} guests."]
            });

        // SQLite stores DateTimeOffset as text and cannot reliably translate
        // range comparisons for this type. Filter by indexed resource/status in SQL,
        // then compare normalized UTC instants in memory.
        var activeReservations = await db.CustomerReservations.AsNoTracking()
            .Where(x => x.ResourceId == resource.Id
                && (x.Status == "Requested" || x.Status == "Confirmed" || x.Status == "CheckedIn"))
            .Select(x => new { x.StartsAtUtc, x.EndsAtUtc })
            .ToListAsync(ct);
        var overlaps = activeReservations.Any(x => x.StartsAtUtc < endsAt && startsAt < x.EndsAtUtc);
        if (overlaps)
            return Results.Conflict(new { error = "This resource is already reserved during the selected time. Choose another time." });

        var reservation = new CustomerReservationRecord
        {
            Id = Guid.NewGuid(),
            ResourceId = resource.Id,
            ReservationNumber = $"RSV-{Guid.NewGuid():N}".ToUpperInvariant(),
            IdempotencyKey = key,
            GuestName = request.GuestName.Trim(),
            GuestPhone = request.GuestPhone.Trim(),
            GuestCount = request.GuestCount,
            StartsAtUtc = startsAt,
            EndsAtUtc = endsAt,
            Status = "Requested",
            Notes = request.Notes?.Trim() ?? string.Empty,
            CreatedAtUtc = now
        };
        db.CustomerReservations.Add(reservation);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Json(new ReservationCreatedResponse(reservation.Id, reservation.ReservationNumber,
            reservation.ResourceId, reservation.StartsAtUtc, reservation.EndsAtUtc, reservation.Status),
            statusCode: StatusCodes.Status201Created);
    }
    catch (DbUpdateException ex)
    {
        app.Logger.LogWarning(ex, "Reservation persistence failed.");
        db.ChangeTracker.Clear();
        var committed = await db.CustomerReservations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.IdempotencyKey == key, ct);
        if (committed is not null)
        {
            var sameRequest = committed.ResourceId == request.ResourceId
                && committed.GuestName == request.GuestName.Trim()
                && committed.GuestPhone == request.GuestPhone.Trim()
                && committed.GuestCount == request.GuestCount
                && committed.StartsAtUtc == startsAt
                && committed.EndsAtUtc == endsAt
                && committed.Notes == (request.Notes?.Trim() ?? string.Empty);
            if (sameRequest)
                return Results.Ok(new ReservationCreatedResponse(committed.Id, committed.ReservationNumber,
                    committed.ResourceId, committed.StartsAtUtc, committed.EndsAtUtc, committed.Status));
            return Results.Conflict(new { error = "This idempotency key was already used for a different reservation request." });
        }
        return Results.Conflict(new { error = "The reservation could not be confirmed. Retry with the same idempotency key." });
    }
    catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
    {
        app.Logger.LogWarning(ex, "SQLite was busy while processing a reservation.");
        db.ChangeTracker.Clear();
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var committed = await db.CustomerReservations.AsNoTracking()
                .SingleOrDefaultAsync(x => x.IdempotencyKey == key, ct);
            if (committed is not null)
            {
                var sameRequest = committed.ResourceId == request.ResourceId
                    && committed.GuestName == request.GuestName.Trim()
                    && committed.GuestPhone == request.GuestPhone.Trim()
                    && committed.GuestCount == request.GuestCount
                    && committed.StartsAtUtc == startsAt
                    && committed.EndsAtUtc == endsAt
                    && committed.Notes == (request.Notes?.Trim() ?? string.Empty);
                if (sameRequest)
                    return Results.Ok(new ReservationCreatedResponse(committed.Id, committed.ReservationNumber,
                        committed.ResourceId, committed.StartsAtUtc, committed.EndsAtUtc, committed.Status));
                return Results.Conflict(new { error = "This idempotency key was already used for a different reservation request." });
            }
            if (attempt < 5)
                await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)), ct);
        }
        return Results.Conflict(new { error = "The reservation schedule is being updated. Refresh availability and retry." });
    }
}).RequireRateLimiting("public");

app.MapPost("/api/storefront/orders", async (
    HttpContext context,
    CreateOrderRequest? request,
    BusinessDbContext db,
    IConfiguration config,
    CancellationToken ct) =>
{
    var storeSettings = await db.StoreSettings.AsNoTracking().SingleAsync(x => x.Id == 1, ct);
    if (!storeSettings.OnlineOrderingEnabled)
        return Results.Problem("Online ordering is not enabled by the store.", statusCode: StatusCodes.Status503ServiceUnavailable);
    if (request is null)
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["Request"] = ["A valid order request is required."]
        });

    var key = context.Request.Headers["Idempotency-Key"].ToString().Trim();
    if (key.Length is < 16 or > 100 || key.Any(char.IsControl))
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["Idempotency-Key"] = ["A 16-100 character idempotency key is required."]
        });

    if (request.Items is null || request.Items.Count is < 1 or > 40)
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["Items"] = ["An order must contain between 1 and 40 items."]
        });
    if (string.IsNullOrWhiteSpace(request.CustomerName) || request.CustomerName.Trim().Length > 160)
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["CustomerName"] = ["Customer name is required and must not exceed 160 characters."]
        });
    if (string.IsNullOrWhiteSpace(request.CustomerPhone) || request.CustomerPhone.Trim().Length > 40)
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["CustomerPhone"] = ["Customer phone is required and must not exceed 40 characters."]
        });
    if ((request.DeliveryAddress?.Length ?? 0) > 1000 || (request.Notes?.Length ?? 0) > 1000)
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["DeliveryAddress"] = ["Address and notes must not exceed 1000 characters."]
        });

    var requested = new Dictionary<string, RequestedOrderLine>(StringComparer.Ordinal);
    foreach (var item in request.Items)
    {
        if (item.ProductId == Guid.Empty || item.Quantity is < 1 or > 99)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Items"] = ["Each product must have a valid ID and quantity between 1 and 99."]
            });

        var addonIds = item.AddonIds?.ToArray() ?? Array.Empty<Guid>();
        if (addonIds.Length > 20 || addonIds.Any(id => id == Guid.Empty)
            || addonIds.Distinct().Count() != addonIds.Length)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Items"] = ["Each product line can contain at most 20 unique, valid add-ons."]
            });

        addonIds = addonIds.Order().ToArray();
        var lineKey = Program.GetOrderLineKey(item.ProductId, addonIds);
        if (requested.TryGetValue(lineKey, out var existingLine))
        {
            var combinedQuantity = checked(existingLine.Quantity + item.Quantity);
            if (combinedQuantity > 99)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Items"] = ["Combined quantity for the same product and add-ons cannot exceed 99."]
                });
            requested[lineKey] = existingLine with { Quantity = combinedQuantity };
        }
        else
        {
            requested[lineKey] = new RequestedOrderLine(item.ProductId, item.Quantity, addonIds);
        }
    }

    var quantities = requested.Values
        .GroupBy(line => line.ProductId)
        .ToDictionary(group => group.Key, group => group.Sum(line => line.Quantity));
    if (quantities.Values.Any(quantity => quantity > 99))
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["Items"] = ["Combined quantity for a product cannot exceed 99, even when add-on selections differ."]
        });

    var existing = await db.CustomerOrders.AsNoTracking()
        .Include(x => x.Lines)
        .SingleOrDefaultAsync(x => x.IdempotencyKey == key, ct);
    if (existing is not null)
    {
        if (!Program.MatchesOrderRequest(existing, request, requested))
            return Results.Conflict(new { error = "This idempotency key was already used for a different order request." });
        return Results.Ok(new OrderCreatedResponse(existing.Id, existing.OrderNumber, existing.TotalAmount, existing.CurrencyCode, existing.Status));
    }

    try
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Recheck after acquiring the write transaction. The first lookup above is
        // only a fast path; two concurrent retries can both pass that initial lookup.
        var existingInTransaction = await db.CustomerOrders.AsNoTracking()
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.IdempotencyKey == key, ct);
        if (existingInTransaction is not null)
        {
            await transaction.CommitAsync(ct);
            if (!Program.MatchesOrderRequest(existingInTransaction, request, requested))
                return Results.Conflict(new { error = "This idempotency key was already used for a different order request." });
            return Results.Ok(new OrderCreatedResponse(existingInTransaction.Id, existingInTransaction.OrderNumber,
                existingInTransaction.TotalAmount, existingInTransaction.CurrencyCode, existingInTransaction.Status));
        }

        var productIds = quantities.Keys.ToArray();
        var products = await db.StoreProducts.Where(x => productIds.Contains(x.Id) && x.IsAvailable)
            .ToDictionaryAsync(x => x.Id, ct);
        if (products.Count != quantities.Count)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Items"] = ["One or more products are unavailable. Refresh the catalog and try again."]
            });

        var currency = products.Values.First().CurrencyCode;
        if (products.Values.Any(x => x.CurrencyCode != currency))
            return Results.Problem("The store catalog contains mixed currencies.", statusCode: StatusCodes.Status500InternalServerError);

        var addonIdsUsed = requested.Values.SelectMany(line => line.AddonIds).Distinct().ToArray();
        var addons = addonIdsUsed.Length == 0
            ? new Dictionary<Guid, ProductAddonRecord>()
            : await db.ProductAddons
                .Where(addon => addonIdsUsed.Contains(addon.Id) && addon.IsActive)
                .ToDictionaryAsync(addon => addon.Id, ct);
        var assignedAddonPairs = addonIdsUsed.Length == 0
            ? new HashSet<(Guid ProductId, Guid AddonId)>()
            : (await db.StoreProductAddons.AsNoTracking()
                    .Where(link => productIds.Contains(link.ProductId) && addonIdsUsed.Contains(link.AddonId))
                    .ToListAsync(ct))
                .Select(link => (link.ProductId, link.AddonId))
                .ToHashSet();

        foreach (var line in requested.Values)
        {
            var product = products[line.ProductId];
            foreach (var addonId in line.AddonIds)
            {
                if (!addons.TryGetValue(addonId, out var addon)
                    || !assignedAddonPairs.Contains((line.ProductId, addonId)))
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["Items"] = [$"Add-on {addonId} is inactive or is not assigned to {product.Name}."]
                    });
                if (!string.Equals(addon.CurrencyCode, product.CurrencyCode, StringComparison.Ordinal))
                    return Results.Problem("A product add-on uses a different currency from its product.", statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        foreach (var (id, quantity) in quantities)
        {
            var product = products[id];
            if (product.StockQuantity < quantity)
                return Results.Conflict(new { error = $"Insufficient stock for {product.Name}." });
        }

        decimal total = 0m;
        var order = new CustomerOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = $"WEB-{Guid.NewGuid():N}".ToUpperInvariant(),
            IdempotencyKey = key,
            CustomerName = request.CustomerName.Trim(),
            CustomerPhone = request.CustomerPhone.Trim(),
            DeliveryAddress = request.DeliveryAddress?.Trim() ?? string.Empty,
            Notes = request.Notes?.Trim() ?? string.Empty,
            CurrencyCode = currency,
            Status = "Received",
            CreatedAt = DateTimeOffset.UtcNow
        };

        foreach (var (id, quantity) in requested)
        {
            var product = products[id];
            var lineTotal = checked(product.UnitPrice * quantity);
            total = checked(total + lineTotal);
            var updatedAt = DateTimeOffset.UtcNow;
            var affected = await db.StoreProducts
                .Where(x => x.Id == id && x.IsAvailable && x.StockQuantity >= quantity
                    && x.UnitPrice == product.UnitPrice && x.UnitCost == product.UnitCost
                    && x.CurrencyCode == product.CurrencyCode)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.StockQuantity, x => x.StockQuantity - quantity)
                    .SetProperty(x => x.UpdatedAt, updatedAt), ct);
            if (affected != 1)
                return Results.Conflict(new { error = $"Stock or price changed for {product.Name}. Refresh the catalog and retry." });

            order.Lines.Add(new CustomerOrderLine
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                ProductName = product.Name,
                UnitPrice = product.UnitPrice,
                UnitCost = product.UnitCost,
                Quantity = quantity,
                LineTotal = lineTotal
            });
            db.StockMovements.Add(new StockMovementRecord
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                OccurredAt = DateTimeOffset.UtcNow,
                MovementType = "WebOrderIssue",
                Quantity = quantity,
                SignedQuantity = -quantity,
                UnitCost = product.UnitCost,
                CurrencyCode = product.CurrencyCode,
                Reference = order.OrderNumber,
                Note = "Stock reserved for customer web order"
            });
        }

        order.TotalAmount = total;
        db.CustomerOrders.Add(order);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return Results.Json(
            new OrderCreatedResponse(order.Id, order.OrderNumber, order.TotalAmount, order.CurrencyCode, order.Status),
            statusCode: StatusCodes.Status201Created);
    }
    catch (DbUpdateException ex)
    {
        app.Logger.LogWarning(ex, "Order persistence failed.");
        // A concurrent request with the same key may have committed first. Treat
        // the unique-key conflict as a successful idempotent retry when possible.
        var committedOrder = await db.CustomerOrders.AsNoTracking()
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.IdempotencyKey == key, ct);
        if (committedOrder is not null)
        {
            if (!Program.MatchesOrderRequest(committedOrder, request, requested))
                return Results.Conflict(new { error = "This idempotency key was already used for a different order request." });
            return Results.Ok(new OrderCreatedResponse(committedOrder.Id, committedOrder.OrderNumber,
                committedOrder.TotalAmount, committedOrder.CurrencyCode, committedOrder.Status));
        }
        return Results.Conflict(new { error = "The order could not be confirmed. Retry with the same idempotency key." });
    }
    catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
    {
        app.Logger.LogWarning(ex, "SQLite was busy while processing a public order.");
        CustomerOrder? committedOrder = null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            committedOrder = await db.CustomerOrders.AsNoTracking()
                .Include(x => x.Lines)
                .SingleOrDefaultAsync(x => x.IdempotencyKey == key, ct);
            if (committedOrder is not null)
            {
                if (!Program.MatchesOrderRequest(committedOrder, request, requested))
                    return Results.Conflict(new { error = "This idempotency key was already used for a different order request." });
                return Results.Ok(new OrderCreatedResponse(committedOrder.Id, committedOrder.OrderNumber,
                    committedOrder.TotalAmount, committedOrder.CurrencyCode, committedOrder.Status));
            }
            if (attempt < 5)
                await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)), ct);
        }
        return Results.Conflict(new { error = "The store is processing another update. Retry with the same idempotency key." });
    }
    catch (OverflowException)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["Items"] = ["Order total is outside the supported range."]
        });
    }
})
    .RequireRateLimiting("public");

app.MapGet("/", () => Results.Content(StorefrontHtml.Content, "text/html; charset=utf-8"))
    .RequireRateLimiting("public");

app.Run();

public sealed record StorefrontInfo(string StoreName, string CurrencyCode, bool OnlineOrderingEnabled, bool OnlineReservationsEnabled);
public sealed record ReservationResourceResponse(Guid Id, string Name, string ResourceType, int Capacity);
public sealed record CreateReservationRequest(Guid ResourceId, string GuestName, string GuestPhone, int GuestCount,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Notes);
public sealed record ReservationCreatedResponse(Guid Id, string ReservationNumber, Guid ResourceId,
    DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, string Status);
public sealed record ProductAddonResponse(Guid Id, string Name, decimal Price);
public sealed record ProductResponse(Guid Id, string Sku, string Name, string Description, decimal Price, string CurrencyCode, bool InStock, IReadOnlyList<ProductAddonResponse> Addons);
public sealed record CreateOrderRequest(string CustomerName, string CustomerPhone, string? DeliveryAddress, string? Notes, List<CreateOrderItemRequest>? Items);
public sealed record CreateOrderItemRequest(Guid ProductId, int Quantity, List<Guid>? AddonIds = null);
public sealed record OrderCreatedResponse(Guid Id, string OrderNumber, decimal Total, string CurrencyCode, string Status);

public partial class Program
{
    internal static bool MatchesOrderRequest(
        CustomerOrder order,
        CreateOrderRequest request,
        IReadOnlyDictionary<Guid, int> requested)
    {
        if (!string.Equals(order.CustomerName, request.CustomerName.Trim(), StringComparison.Ordinal)
            || !string.Equals(order.CustomerPhone, request.CustomerPhone.Trim(), StringComparison.Ordinal)
            || !string.Equals(order.DeliveryAddress, request.DeliveryAddress?.Trim() ?? string.Empty, StringComparison.Ordinal)
            || !string.Equals(order.Notes, request.Notes?.Trim() ?? string.Empty, StringComparison.Ordinal)
            || order.Lines.Count != requested.Count)
            return false;

        return order.Lines.All(line =>
            requested.TryGetValue(line.ProductId, out var quantity) && line.Quantity == quantity);
    }
}

internal static class StorefrontHtml
{
    public const string Content = """
<!doctype html>
<html lang="fa" dir="rtl">
<head>
<meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="color-scheme" content="light"><title>فروشگاه آنلاین</title>
<style>
:root{font-family:Tahoma,Arial,sans-serif;color:#18212f;background:#f4f6fa}*{box-sizing:border-box}body{margin:0}.top{background:#17243b;color:#fff;padding:24px max(18px,calc((100% - 1080px)/2))}.top h1{margin:0 0 8px;font-size:24px}.top p{margin:0;color:#d5dbea}.wrap{max-width:1080px;margin:24px auto;padding:0 16px;display:grid;grid-template-columns:minmax(0,1.5fr) minmax(280px,1fr);gap:20px}.panel{background:white;border:1px solid #e0e6ef;border-radius:16px;padding:18px;box-shadow:0 8px 28px #17243b0a}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:12px}.item{border:1px solid #e0e6ef;border-radius:12px;padding:14px}.item h3{margin:0 0 8px;font-size:16px}.muted{color:#64748b;font-size:13px}.price{font-weight:bold;margin:12px 0}.row{display:flex;gap:8px;align-items:center}.row input{min-width:0;flex:1}input,textarea,select,button{font:inherit;border-radius:8px;padding:10px;border:1px solid #cbd5e1;width:100%;margin:5px 0}button{background:#176b52;color:white;border:0;cursor:pointer;font-weight:bold}button:disabled{opacity:.45;cursor:not-allowed}.qty{width:70px;flex:0 0 70px}.status{padding:10px;border-radius:8px;background:#eef4ff;margin:10px 0;white-space:pre-wrap}.cartline{display:flex;justify-content:space-between;gap:10px;border-bottom:1px solid #eef0f4;padding:8px 0;font-size:14px}@media(max-width:760px){.wrap{grid-template-columns:1fr}.top{padding:20px}}
</style>
</head>
<body>
<header class="top"><h1 id="store">فروشگاه آنلاین</h1><p>سفارش خود را ثبت کنید؛ قیمت و موجودی هنگام ثبت سفارش دوباره بررسی می‌شود.</p></header>
<main class="wrap"><section class="panel"><h2>محصولات</h2><div id="products" class="grid"><p>در حال دریافت محصولات…</p></div></section>
<section class="panel"><h2>سبد سفارش</h2><div id="cart"><p class="muted">هنوز محصولی انتخاب نکرده‌اید.</p></div><form id="order"><h3>اطلاعات مشتری</h3><input name="CustomerName" placeholder="نام و نام خانوادگی" maxlength="160" required><input name="CustomerPhone" placeholder="شماره تماس" maxlength="40" required><textarea name="DeliveryAddress" placeholder="نشانی تحویل" maxlength="1000" rows="2"></textarea><textarea name="Notes" placeholder="توضیحات (اختیاری)" maxlength="1000" rows="2"></textarea><button id="submit" type="submit" disabled>ثبت سفارش</button></form><div id="status" class="status" role="status" aria-live="polite">در حال بررسی فروشگاه…</div></section>
<section class="panel" id="reservationPanel" hidden><h2>رزرو میز یا دستگاه</h2><p class="muted">زمان‌ها بر اساس ساعت محلی دستگاه شما انتخاب می‌شوند. ثبت رزرو ابتدا به‌صورت درخواست انجام می‌شود و فروشگاه آن را تأیید می‌کند.</p><form id="reservation"><label for="reservationResource">مورد رزرو</label><select id="reservationResource" name="ResourceId" required></select><input name="GuestName" placeholder="نام و نام خانوادگی" maxlength="200" required><input name="GuestPhone" placeholder="شماره تماس" maxlength="40" required><label for="reservationStart">شروع رزرو</label><input id="reservationStart" name="StartsAt" type="datetime-local" required><label for="reservationEnd">پایان رزرو</label><input id="reservationEnd" name="EndsAt" type="datetime-local" required><label for="reservationCount">تعداد نفرات</label><input id="reservationCount" name="GuestCount" type="number" min="1" max="1000" value="1" required><textarea name="Notes" placeholder="توضیحات (اختیاری)" maxlength="1000" rows="2"></textarea><button id="reservationSubmit" type="submit">ثبت درخواست رزرو</button></form><div id="reservationStatus" class="status" role="status" aria-live="polite">در حال دریافت منابع رزرو…</div></section></main>
<script>
const $=s=>document.querySelector(s);const cart=new Map();let products=[];let info={currencyCode:"IRR",onlineOrderingEnabled:false,onlineReservationsEnabled:false};let pendingKey=null;let pendingOrder=null;let pendingReservationKey=null;let pendingReservation=null;
const fmt=(n,c)=>new Intl.NumberFormat("fa-IR",{maximumFractionDigits:2}).format(n)+" "+c;
function renderCart(){const root=$("#cart");root.replaceChildren();let total=0;for(const [id,q] of cart){const p=products.find(x=>x.id===id);if(!p)continue;total+=p.price*q;const row=document.createElement("div");row.className="cartline";const label=document.createElement("span");label.textContent=p.name+" × "+q;const price=document.createElement("strong");price.textContent=fmt(p.price*q,p.currencyCode);row.append(label,price);root.append(row)}if(cart.size===0){const empty=document.createElement("p");empty.className="muted";empty.textContent="هنوز محصولی انتخاب نکرده‌اید.";root.append(empty)}const t=document.createElement("p");t.innerHTML="<strong>مجموع: </strong>";const v=document.createElement("span");v.textContent=fmt(total,info.currencyCode);t.append(v);root.append(t);$("#submit").disabled=cart.size===0||!info.onlineOrderingEnabled}
function renderProducts(){const root=$("#products");root.replaceChildren();for(const p of products){const card=document.createElement("article");card.className="item";const h=document.createElement("h3");h.textContent=p.name;const d=document.createElement("p");d.className="muted";d.textContent=p.description||" ";const price=document.createElement("div");price.className="price";price.textContent=fmt(p.price,p.currencyCode);const row=document.createElement("div");row.className="row";const qty=document.createElement("input");qty.className="qty";qty.type="number";qty.min="1";qty.max="99";qty.value="1";qty.setAttribute("aria-label","تعداد "+p.name);const b=document.createElement("button");b.type="button";b.textContent=p.inStock?"افزودن":"ناموجود";b.disabled=!p.inStock;b.addEventListener("click",()=>{const q=Math.max(1,Math.min(99,Number.parseInt(qty.value,10)||1));cart.set(p.id,Math.min(99,(cart.get(p.id)||0)+q));renderCart()});row.append(qty,b);card.append(h,d,price,row);root.append(card)}if(!products.length)root.textContent="فعلاً محصولی برای نمایش وجود ندارد."}
async function init(){try{const [i,p]=await Promise.all([fetch("/api/storefront"),fetch("/api/storefront/products")]);if(!i.ok||!p.ok)throw Error("دریافت اطلاعات فروشگاه ناموفق بود.");info=await i.json();products=await p.json();$("#store").textContent=info.storeName;renderProducts();renderCart();$("#status").textContent=info.onlineOrderingEnabled?"فروشگاه آماده دریافت سفارش است.":(info.onlineReservationsEnabled?"امکان رزرو آنلاین فعال است؛ برای سفارش کالا این قابلیت هنوز فعال نشده است.":"ثبت سفارش و رزرو آنلاین هنوز توسط فروشگاه فعال نشده است.")}catch(e){$("#status").textContent=e.message;$("#products").textContent="دریافت محصولات ناموفق بود."}}
async function initReservations(){if(!info.onlineReservationsEnabled){$("#reservationPanel").hidden=true;return}$("#reservationPanel").hidden=false;try{const r=await fetch("/api/storefront/reservation-resources");if(!r.ok)throw Error("دریافت منابع رزرو ناموفق بود.");const resources=await r.json();const select=$("#reservationResource");select.replaceChildren();for(const resource of resources){const option=document.createElement("option");option.value=resource.id;option.textContent=resource.name+" — "+resource.resourceType+" (ظرفیت "+resource.capacity+")";select.append(option)}$("#reservationSubmit").disabled=resources.length===0;$("#reservationStatus").textContent=resources.length?"زمان دلخواه را انتخاب و درخواست را ثبت کنید.":"هنوز مورد قابل رزروی از طرف فروشگاه تعریف نشده است."}catch(err){$("#reservationSubmit").disabled=true;$("#reservationStatus").textContent=err.message}}
$("#reservation").addEventListener("submit",async e=>{e.preventDefault();const form=new FormData(e.currentTarget);const start=new Date(form.get("StartsAt"));const end=new Date(form.get("EndsAt"));if(Number.isNaN(start.getTime())||Number.isNaN(end.getTime())||end<=start){$("#reservationStatus").textContent="زمان شروع و پایان را درست وارد کنید.";return}if(!pendingReservation){pendingReservation={resourceId:form.get("ResourceId"),guestName:form.get("GuestName"),guestPhone:form.get("GuestPhone"),guestCount:Number(form.get("GuestCount")),startsAt:start.toISOString(),endsAt:end.toISOString(),notes:form.get("Notes")};pendingReservationKey=crypto.randomUUID()+crypto.randomUUID()}$("#reservationSubmit").disabled=true;$("#reservationStatus").textContent="در حال ثبت درخواست رزرو…";try{const r=await fetch("/api/storefront/reservations",{method:"POST",headers:{"Content-Type":"application/json","Idempotency-Key":pendingReservationKey},body:JSON.stringify(pendingReservation)});const data=await r.json();if(!r.ok)throw Error(data.error||data.title||"ثبت درخواست رزرو ناموفق بود.");$("#reservationStatus").textContent="درخواست رزرو ثبت شد. کد پیگیری: "+data.reservationNumber+"؛ وضعیت: در انتظار تأیید فروشگاه.";e.currentTarget.reset();pendingReservation=null;pendingReservationKey=null}catch(err){$("#reservationStatus").textContent=err.message}finally{$("#reservationSubmit").disabled=false}});
$("#order").addEventListener("submit",async e=>{e.preventDefault();if(!cart.size||!info.onlineOrderingEnabled)return;const form=new FormData(e.currentTarget);if(!pendingOrder){pendingOrder={customerName:form.get("CustomerName"),customerPhone:form.get("CustomerPhone"),deliveryAddress:form.get("DeliveryAddress"),notes:form.get("Notes"),items:[...cart].map(([productId,quantity])=>({productId,quantity}))};pendingKey=crypto.randomUUID()+crypto.randomUUID()}const body=pendingOrder;const key=pendingKey;$("#submit").disabled=true;$("#status").textContent="در حال ثبت سفارش…";try{const r=await fetch("/api/storefront/orders",{method:"POST",headers:{"Content-Type":"application/json","Idempotency-Key":key},body:JSON.stringify(body)});const data=await r.json();if(!r.ok)throw Error(data.error||data.title||"ثبت سفارش ناموفق بود.");cart.clear();pendingOrder=null;pendingKey=null;renderCart();e.currentTarget.reset();$("#status").textContent="سفارش با موفقیت ثبت شد. شماره سفارش: "+data.orderNumber+" — مبلغ: "+fmt(data.total,data.currencyCode)}catch(err){$("#status").textContent=err.message}finally{renderCart()}});
init().then(initReservations);
</script>
</body></html>
""";
}
