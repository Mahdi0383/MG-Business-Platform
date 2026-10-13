using System.Net;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MG.BusinessPlatform.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

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

app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    context.Response.Headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
    context.Response.Headers.TryAdd("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
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

app.UseRateLimiter();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
    await db.Database.MigrateAsync();
    if (!await db.StoreSettings.AnyAsync())
    {
        db.StoreSettings.Add(new StoreSettings { Id = 1, StoreName = "فروشگاه شما", CurrencyCode = "IRR", OnlineOrderingEnabled = false, PublicHostname = string.Empty, UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }
}

app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
app.MapGet("/health/ready", async (BusinessDbContext db, CancellationToken ct) =>
{
    var canConnect = await db.Database.CanConnectAsync(ct);
    return canConnect
        ? Results.Ok(new { status = "ready" })
        : Results.Problem("Database is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
}).RequireRateLimiting("public");

app.MapGet("/api/storefront", async (BusinessDbContext db, CancellationToken ct) =>
{
    var settings = await db.StoreSettings.AsNoTracking().SingleAsync(x => x.Id == 1, ct);
    return Results.Ok(new StorefrontInfo(settings.StoreName, settings.CurrencyCode, settings.OnlineOrderingEnabled));
}).RequireRateLimiting("public");

app.MapGet("/api/storefront/products", async (BusinessDbContext db, CancellationToken ct) =>
{
    var products = await db.StoreProducts.AsNoTracking()
        .Where(x => x.IsAvailable)
        .OrderBy(x => x.Name)
        .Select(x => new ProductResponse(x.Id, x.Sku, x.Name, x.Description, x.UnitPrice, x.CurrencyCode, x.StockQuantity > 0))
        .Take(500)
        .ToListAsync(ct);
    return Results.Ok(products);
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

    var existing = await db.CustomerOrders.AsNoTracking().SingleOrDefaultAsync(x => x.IdempotencyKey == key, ct);
    if (existing is not null)
        return Results.Ok(new OrderCreatedResponse(existing.Id, existing.OrderNumber, existing.TotalAmount, existing.CurrencyCode, existing.Status));

    var requested = new Dictionary<Guid, int>();
    foreach (var item in request.Items)
    {
        if (item.ProductId == Guid.Empty || item.Quantity is < 1 or > 99)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Items"] = ["Each product must have a valid ID and quantity between 1 and 99."]
            });
        requested[item.ProductId] = checked(requested.GetValueOrDefault(item.ProductId) + item.Quantity);
        if (requested[item.ProductId] > 99)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Items"] = ["Combined quantity for a product cannot exceed 99."]
            });
    }

    await using var transaction = await db.Database.BeginTransactionAsync(ct);
    try
    {
        var productIds = requested.Keys.ToArray();
        var products = await db.StoreProducts.Where(x => productIds.Contains(x.Id) && x.IsAvailable)
            .ToDictionaryAsync(x => x.Id, ct);
        if (products.Count != requested.Count)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Items"] = ["One or more products are unavailable. Refresh the catalog and try again."]
            });

        var currency = products.Values.First().CurrencyCode;
        if (products.Values.Any(x => x.CurrencyCode != currency))
            return Results.Problem("The store catalog contains mixed currencies.", statusCode: StatusCodes.Status500InternalServerError);

        foreach (var (id, quantity) in requested)
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
            product.StockQuantity -= quantity;
            product.UpdatedAt = DateTimeOffset.UtcNow;
            order.Lines.Add(new CustomerOrderLine
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                ProductName = product.Name,
                UnitPrice = product.UnitPrice,
                Quantity = quantity,
                LineTotal = lineTotal
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
        await transaction.RollbackAsync(ct);
        app.Logger.LogWarning(ex, "Order persistence failed.");
        return Results.Conflict(new { error = "The order could not be confirmed. Retry with the same idempotency key." });
    }
    catch (OverflowException)
    {
        await transaction.RollbackAsync(ct);
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["Items"] = ["Order total is outside the supported range."]
        });
    }
})
    .RequireRateLimiting("public")
    .WithRequestSizeLimit(64 * 1024);

app.MapGet("/", () => Results.Content(StorefrontHtml.Content, "text/html; charset=utf-8"))
    .RequireRateLimiting("public");

app.Run();

public sealed record StorefrontInfo(string StoreName, string CurrencyCode, bool OnlineOrderingEnabled);
public sealed record ProductResponse(Guid Id, string Sku, string Name, string Description, decimal Price, string CurrencyCode, bool InStock);
public sealed record CreateOrderRequest(string CustomerName, string CustomerPhone, string? DeliveryAddress, string? Notes, List<CreateOrderItemRequest>? Items);
public sealed record CreateOrderItemRequest(Guid ProductId, int Quantity);
public sealed record OrderCreatedResponse(Guid Id, string OrderNumber, decimal Total, string CurrencyCode, string Status);

public partial class Program { }

internal static class StorefrontHtml
{
    public const string Content = """
<!doctype html>
<html lang="fa" dir="rtl">
<head>
<meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="color-scheme" content="light"><title>فروشگاه آنلاین</title>
<style>
:root{font-family:Tahoma,Arial,sans-serif;color:#18212f;background:#f4f6fa}*{box-sizing:border-box}body{margin:0}.top{background:#17243b;color:#fff;padding:24px max(18px,calc((100% - 1080px)/2))}.top h1{margin:0 0 8px;font-size:24px}.top p{margin:0;color:#d5dbea}.wrap{max-width:1080px;margin:24px auto;padding:0 16px;display:grid;grid-template-columns:minmax(0,1.5fr) minmax(280px,1fr);gap:20px}.panel{background:white;border:1px solid #e0e6ef;border-radius:16px;padding:18px;box-shadow:0 8px 28px #17243b0a}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:12px}.item{border:1px solid #e0e6ef;border-radius:12px;padding:14px}.item h3{margin:0 0 8px;font-size:16px}.muted{color:#64748b;font-size:13px}.price{font-weight:bold;margin:12px 0}.row{display:flex;gap:8px;align-items:center}.row input{min-width:0;flex:1}input,textarea,button{font:inherit;border-radius:8px;padding:10px;border:1px solid #cbd5e1;width:100%;margin:5px 0}button{background:#176b52;color:white;border:0;cursor:pointer;font-weight:bold}button:disabled{opacity:.45;cursor:not-allowed}.qty{width:70px;flex:0 0 70px}.status{padding:10px;border-radius:8px;background:#eef4ff;margin:10px 0;white-space:pre-wrap}.cartline{display:flex;justify-content:space-between;gap:10px;border-bottom:1px solid #eef0f4;padding:8px 0;font-size:14px}@media(max-width:760px){.wrap{grid-template-columns:1fr}.top{padding:20px}}
</style>
</head>
<body>
<header class="top"><h1 id="store">فروشگاه آنلاین</h1><p>سفارش خود را ثبت کنید؛ قیمت و موجودی هنگام ثبت سفارش دوباره بررسی می‌شود.</p></header>
<main class="wrap"><section class="panel"><h2>محصولات</h2><div id="products" class="grid"><p>در حال دریافت محصولات…</p></div></section>
<section class="panel"><h2>سبد سفارش</h2><div id="cart"><p class="muted">هنوز محصولی انتخاب نکرده‌اید.</p></div><form id="order"><h3>اطلاعات مشتری</h3><input name="CustomerName" placeholder="نام و نام خانوادگی" maxlength="160" required><input name="CustomerPhone" placeholder="شماره تماس" maxlength="40" required><textarea name="DeliveryAddress" placeholder="نشانی تحویل" maxlength="1000" rows="2"></textarea><textarea name="Notes" placeholder="توضیحات (اختیاری)" maxlength="1000" rows="2"></textarea><button id="submit" type="submit" disabled>ثبت سفارش</button></form><div id="status" class="status" role="status" aria-live="polite">در حال بررسی فروشگاه…</div></section></main>
<script>
const $=s=>document.querySelector(s);const cart=new Map();let products=[];let info={CurrencyCode:"IRR",OnlineOrderingEnabled:false};let pendingKey=null;let pendingOrder=null;
const fmt=(n,c)=>new Intl.NumberFormat("fa-IR",{maximumFractionDigits:2}).format(n)+" "+c;
function renderCart(){const root=$("#cart");root.replaceChildren();let total=0;for(const [id,q] of cart){const p=products.find(x=>x.id===id);if(!p)continue;total+=p.price*q;const row=document.createElement("div");row.className="cartline";const label=document.createElement("span");label.textContent=p.name+" × "+q;const price=document.createElement("strong");price.textContent=fmt(p.price*q,p.currencyCode);row.append(label,price);root.append(row)}if(cart.size===0){const empty=document.createElement("p");empty.className="muted";empty.textContent="هنوز محصولی انتخاب نکرده‌اید.";root.append(empty)}const t=document.createElement("p");t.innerHTML="<strong>مجموع: </strong>";const v=document.createElement("span");v.textContent=fmt(total,info.CurrencyCode);t.append(v);root.append(t);$("#submit").disabled=cart.size===0||!info.OnlineOrderingEnabled}
function renderProducts(){const root=$("#products");root.replaceChildren();for(const p of products){const card=document.createElement("article");card.className="item";const h=document.createElement("h3");h.textContent=p.name;const d=document.createElement("p");d.className="muted";d.textContent=p.description||" ";const price=document.createElement("div");price.className="price";price.textContent=fmt(p.price,p.currencyCode);const row=document.createElement("div");row.className="row";const qty=document.createElement("input");qty.className="qty";qty.type="number";qty.min="1";qty.max="99";qty.value="1";qty.setAttribute("aria-label","تعداد "+p.name);const b=document.createElement("button");b.type="button";b.textContent=p.inStock?"افزودن":"ناموجود";b.disabled=!p.inStock;b.addEventListener("click",()=>{const q=Math.max(1,Math.min(99,Number.parseInt(qty.value,10)||1));cart.set(p.id,Math.min(99,(cart.get(p.id)||0)+q));renderCart()});row.append(qty,b);card.append(h,d,price,row);root.append(card)}if(!products.length)root.textContent="فعلاً محصولی برای نمایش وجود ندارد."}
async function init(){try{const [i,p]=await Promise.all([fetch("/api/storefront"),fetch("/api/storefront/products")]);if(!i.ok||!p.ok)throw Error("دریافت اطلاعات فروشگاه ناموفق بود.");info=await i.json();products=await p.json();$("#store").textContent=info.storeName;renderProducts();renderCart();$("#status").textContent=info.onlineOrderingEnabled?"فروشگاه آماده دریافت سفارش است.":"ثبت آنلاین سفارش هنوز توسط فروشگاه فعال نشده است."}catch(e){$("#status").textContent=e.message;$("#products").textContent="دریافت محصولات ناموفق بود."}}
$("#order").addEventListener("submit",async e=>{e.preventDefault();if(!cart.size||!info.OnlineOrderingEnabled)return;const form=new FormData(e.currentTarget);if(!pendingOrder){pendingOrder={customerName:form.get("CustomerName"),customerPhone:form.get("CustomerPhone"),deliveryAddress:form.get("DeliveryAddress"),notes:form.get("Notes"),items:[...cart].map(([productId,quantity])=>({productId,quantity}))};pendingKey=crypto.randomUUID()+crypto.randomUUID()}const body=pendingOrder;const key=pendingKey;$("#submit").disabled=true;$("#status").textContent="در حال ثبت سفارش…";try{const r=await fetch("/api/storefront/orders",{method:"POST",headers:{"Content-Type":"application/json","Idempotency-Key":key},body:JSON.stringify(body)});const data=await r.json();if(!r.ok)throw Error(data.error||data.title||"ثبت سفارش ناموفق بود.");cart.clear();pendingOrder=null;pendingKey=null;renderCart();e.currentTarget.reset();$("#status").textContent="سفارش با موفقیت ثبت شد. شماره سفارش: "+data.orderNumber+" — مبلغ: "+fmt(data.total,data.currencyCode)}catch(err){$("#status").textContent=err.message}finally{renderCart()}});
init();
</script>
</body></html>
""";
}
