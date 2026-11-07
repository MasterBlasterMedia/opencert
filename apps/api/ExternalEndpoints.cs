using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenCert.Infrastructure;
namespace OpenCert.Api;

public record ExternalStart(string Provider, string Intent);
public static class ExternalEndpoints
{
    public static void MapExternal(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/auth/external").RequireRateLimiting("auth");
        group.MapGet("/providers", async (SignInManager<User> signIn) => (await signIn.GetExternalAuthenticationSchemesAsync()).Select(s => new { name = s.Name, label = s.DisplayName ?? s.Name }));
        group.MapGet("/connections", async (HttpContext ctx, UserManager<User> users) =>
        {
            var user = (await users.FindByIdAsync(ctx.UserId()))!;
            return (await users.GetLoginsAsync(user)).Select(l => new { provider = l.LoginProvider, name = l.ProviderDisplayName });
        }).RequireAuthorization();
        group.MapPost("/begin", async (ExternalStart input, HttpContext ctx, AppDb db, SignInManager<User> signIn, UserManager<User> users) =>
        {
            if (input.Intent is not ("login" or "link") || !(await signIn.GetExternalAuthenticationSchemesAsync()).Any(s => s.Name == input.Provider)) throw new ApiFailure(400, "This sign-in provider is not configured.");
            string? userId = null;
            if (input.Intent == "link") { userId = ctx.UserId(); var user = (await users.FindByIdAsync(userId))!; var session = await Access.Session(db, ctx, user.TwoFactorEnabled); if (session.CreatedAt < DateTimeOffset.UtcNow.AddMinutes(-10) && (session.StrongAt is null || session.StrongAt < DateTimeOffset.UtcNow.AddMinutes(-10))) throw new ApiFailure(403, "Sign in again before linking an account."); }
            else if (ctx.User.Identity?.IsAuthenticated == true) throw new ApiFailure(409, "Use Connected accounts to link another provider, or sign out first.");
            var browser = Access.RandomToken(); var flow = new AuthChallenge { Purpose = "external:" + input.Provider + ":" + input.Intent, UserId = userId, BrowserHash = Access.Hash(browser) };
            db.AuthChallenges.Add(flow); await db.SaveChangesAsync(); ctx.Response.Cookies.Append("oc.external-flow", browser, Access.Cookie(ctx, TimeSpan.FromMinutes(5)));
            return Results.Ok(new { url = "/api/v1/auth/external/redirect/" + flow.Id });
        });
        group.MapGet("/redirect/{id:guid}", async (Guid id, HttpContext ctx, AppDb db, SignInManager<User> signIn) =>
        {
            var flow = await db.AuthChallenges.AsNoTracking().SingleOrDefaultAsync(f => f.Id == id);
            if (flow is null || flow.Consumed || flow.ExpiresAt <= DateTimeOffset.UtcNow || !flow.Purpose.StartsWith("external:", StringComparison.Ordinal) || !Access.Matches(ctx.Request.Cookies["oc.external-flow"] ?? "", flow.BrowserHash)) throw new ApiFailure(400, "Start sign-in again.");
            var provider = flow.Purpose.Split(':')[1]; var properties = signIn.ConfigureExternalAuthenticationProperties(provider, "/api/v1/auth/external/complete", flow.UserId);
            properties.Items["oc.flow"] = flow.Id.ToString(); return Results.Challenge(properties, [provider]);
        });
        group.MapGet("/complete", async (HttpContext ctx, AppDb db, SignInManager<User> signIn, UserManager<User> users) =>
        {
            var external = await ctx.AuthenticateAsync(IdentityConstants.ExternalScheme);
            if (!external.Succeeded || external.Properties is null || !external.Properties.Items.TryGetValue("oc.flow", out var flowValue) || !Guid.TryParse(flowValue, out var id)) return Results.Redirect("/login?external=failed");
            await using var tx = await db.Database.BeginTransactionAsync();
            var flow = await db.AuthChallenges.FromSqlInterpolated($"SELECT * FROM \"AuthChallenges\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync();
            if (flow is null || flow.Consumed || flow.ExpiresAt <= DateTimeOffset.UtcNow || !Access.Matches(ctx.Request.Cookies["oc.external-flow"] ?? "", flow.BrowserHash)) return Results.Redirect("/login?external=failed");
            flow.Consumed = true; var info = await signIn.GetExternalLoginInfoAsync(flow.UserId); await ctx.SignOutAsync(IdentityConstants.ExternalScheme); ctx.Response.Cookies.Delete("oc.external-flow");
            if (info is null || flow.Purpose.Split(':')[1] != info.LoginProvider || string.IsNullOrEmpty(info.ProviderKey)) { await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Redirect("/login?external=failed"); }
            if (flow.UserId is not null)
            {
                if (ctx.UserId() != flow.UserId) throw new ApiFailure(403, "Sign in with the account that started this connection.");
                var user = await Access.LockUser(db, flow.UserId); var session = await Access.Session(db, ctx, user.TwoFactorEnabled);
                if (session.CreatedAt < DateTimeOffset.UtcNow.AddMinutes(-10) && (session.StrongAt is null || session.StrongAt < DateTimeOffset.UtcNow.AddMinutes(-10))) throw new ApiFailure(403, "Sign in again before connecting a provider.");
                var linked = await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
                if (linked is not null && linked.Id != user.Id) throw new ApiFailure(409, "This provider identity is already connected to another account.");
                if (linked is null)
                {
                    if ((await users.GetLoginsAsync(user)).Any(l => l.LoginProvider == info.LoginProvider)) throw new ApiFailure(409, "Disconnect your existing account for this provider before connecting a different one.");
                    AccountEndpoints.Ensure(await users.AddLoginAsync(user, info));
                }
                Access.Audit(db, ctx, "login_provider.linked", resource: info.LoginProvider); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Redirect("/app/account?connection=added");
            }
            // Email claims never authorize account merging. Connect a provider explicitly after email verification.
            var account = await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            if (account is null) { await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Redirect("/login?external=unlinked"); }
            account = await Access.LockUser(db, account.Id);
            if (await users.IsLockedOutAsync(account) || !account.EmailConfirmed) { await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Redirect("/login?external=failed"); }
            if (account.TwoFactorEnabled)
            {
                var browser = Access.RandomToken(); var pending = new LoginFlow { Email = account.NormalizedEmail!, Consumed = true, MfaUserId = account.Id, BrowserHash = Access.Hash(browser), ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5) };
                db.LoginFlows.Add(pending); await db.SaveChangesAsync(); await tx.CommitAsync(); ctx.Response.Cookies.Append("oc.flow", browser, Access.Cookie(ctx, TimeSpan.FromMinutes(5)));
                return Results.Redirect("/login#mfa=" + pending.Id);
            }
            await db.SaveChangesAsync(); await tx.CommitAsync(); await Access.SignIn(db, signIn, account, false); return Results.Redirect("/app");
        });
        group.MapDelete("/connections/{provider}", async (string provider, HttpContext ctx, AppDb db, UserManager<User> users, SignInManager<User> signIn) =>
        {
            var session = await Access.Session(db, ctx, true); await using var tx = await db.Database.BeginTransactionAsync(); var user = await Access.LockUser(db, ctx.UserId());
            if (!user.EmailConfirmed) throw new ApiFailure(409, "Verify a recovery email before removing a connection.");
            var login = (await users.GetLoginsAsync(user)).SingleOrDefault(l => l.LoginProvider == provider) ?? throw new ApiFailure(404, "Connection not found.");
            AccountEndpoints.Ensure(await users.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey));
            Access.Audit(db, ctx, "login_provider.removed", resource: provider); await db.SaveChangesAsync(); await tx.CommitAsync(); await Access.Refresh(signIn, user, session); return Results.NoContent();
        }).RequireAuthorization();
    }
}
