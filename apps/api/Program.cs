using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenCert.Api;
using OpenCert.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var ingress = WebIngress.Create(builder.Configuration, builder.Environment);
builder.Configuration["Web:Origin"] = ingress.Origin.GetLeftPart(UriPartial.Authority);
builder.Logging.ClearProviders(); builder.Logging.AddJsonConsole();
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 32768);
builder.Services.AddOpenCert(builder.Configuration, builder.Environment);
builder.Services.AddIdentity<User, IdentityRole>(o => { o.User.RequireUniqueEmail = true; o.SignIn.RequireConfirmedEmail = true; o.Lockout.MaxFailedAccessAttempts = 5; o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15); o.Stores.SchemaVersion = IdentitySchemaVersions.Version3; })
    .AddEntityFrameworkStores<AppDb>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = builder.Environment.IsDevelopment() ? "oc.session" : "__Host-oc.session";
    o.Cookie.HttpOnly = true; o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Lax; o.ExpireTimeSpan = TimeSpan.FromHours(12); o.SlidingExpiration = false;
    o.Events = new CookieAuthenticationEvents
    {
        OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; },
        OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; },
        OnValidatePrincipal = async ctx =>
        {
            var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDb>(); var sm = ctx.HttpContext.RequestServices.GetRequiredService<SignInManager<User>>();
            var validId = Guid.TryParse(ctx.Principal?.FindFirstValue("sid"), out var id); var uid = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!validId || !await db.Sessions.AnyAsync(s => s.Id == id && s.UserId == uid && s.RevokedAt == null && s.ExpiresAt > DateTimeOffset.UtcNow) || await sm.ValidateSecurityStampAsync(ctx.Principal) is null) ctx.RejectPrincipal();
        }
    };
});
builder.Services.Configure<IdentityPasskeyOptions>(o => { o.ServerDomain = new Uri(builder.Configuration["Web:Origin"] ?? "http://localhost:5173").Host; o.UserVerificationRequirement = "required"; o.ValidateOrigin = context => ValueTask.FromResult(!context.CrossOrigin && context.Origin == (builder.Configuration["Web:Origin"] ?? "http://localhost:5173")); });
builder.Services.AddExternalAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(o => { o.HeaderName = "X-CSRF-Token"; o.Cookie.Name = "oc.csrf"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Lax; o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always; });
var origin = builder.Configuration["Web:Origin"] ?? "http://localhost:5173";
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origin).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new() { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter((ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown") + ctx.Request.Path, _ => new() { PermitLimit = ctx.Request.Method == "GET" ? 120 : 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy("diagnostics", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.UserId(), _ => new() { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddOpenApi(); builder.Services.AddProblemDetails();
var app = builder.Build();
if (args.Contains("--migrate")) { using var scope = app.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<AppDb>().Database.MigrateAsync(); return; }
app.UseForwardedHeaders(ingress.Forwarding);
app.Use(ingress.Enforce);
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff"; ctx.Response.Headers["Referrer-Policy"] = "no-referrer"; ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    ctx.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    if (ctx.Request.Path.StartsWithSegments("/api")) ctx.Response.Headers.CacheControl = "no-store";
    try { await next(ctx); }
    catch (ApiFailure e) { await Results.Problem(statusCode: e.Status, title: e.Message, extensions: new Dictionary<string, object?> { ["traceId"] = ctx.TraceIdentifier }).ExecuteAsync(ctx); }
    catch (ArgumentException e) { await Results.Problem(statusCode: 400, title: e.Message).ExecuteAsync(ctx); }
    catch (DbUpdateException) { await Results.Problem(statusCode: 409, title: "This change conflicts with another update. Refresh and try again.").ExecuteAsync(ctx); }
    catch (Exception e) { app.Logger.LogError("Request {TraceId} failed with {Type}", ctx.TraceIdentifier, e.GetType().Name); await Results.Problem(statusCode: 500, title: "We couldn't complete this action. Try again or use the request ID when reporting it.", extensions: new Dictionary<string, object?> { ["traceId"] = ctx.TraceIdentifier }).ExecuteAsync(ctx); }
});
app.UseCors(); app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter();
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api") && ctx.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
    {
        try { await ctx.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(ctx); }
        catch (AntiforgeryValidationException) { await Results.Problem(statusCode: 400, title: "Your page security token expired. Refresh and try again.").ExecuteAsync(ctx); return; }
    }
    await next(ctx);
});
app.MapAuth(); app.MapPasskeys(); app.MapWebsites(); app.MapTeams(); app.MapAccount(); app.MapExternal();
app.MapGet("/health/live", () => Results.Ok(new { status = "up" }));
app.MapGet("/health/ready", async (AppDb db) => await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503));
app.MapGet("/api/v1/status", async (AppDb db) =>
{
    var database = await db.Database.CanConnectAsync(); var heartbeat = database ? await db.Heartbeats.AsNoTracking().SingleOrDefaultAsync(h => h.Id == "worker") : null;
    return Results.Ok(new { api = "Available", database = database ? "Available" : "Unavailable", worker = heartbeat?.At > DateTimeOffset.UtcNow.AddMinutes(-2) ? "Available" : "Not responding", checkedAt = DateTimeOffset.UtcNow });
});
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseDefaultFiles(); app.UseStaticFiles(); app.MapFallbackToFile("index.html");
await app.RunAsync();
public partial class Program { }
