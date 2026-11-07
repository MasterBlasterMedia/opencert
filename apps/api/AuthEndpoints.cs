using System.Net.Mail;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenCert.Application;
using OpenCert.Contracts;
using OpenCert.Infrastructure;
namespace OpenCert.Api;

public static class AuthEndpoints
{
    public static void MapAuth(this WebApplication app)
    {
        var auth = app.MapGroup("/api/v1/auth").RequireRateLimiting("auth");
        auth.MapGet("/csrf", (HttpContext ctx, IAntiforgery antiforgery) => Results.Ok(new { token = antiforgery.GetAndStoreTokens(ctx).RequestToken }));
        auth.MapPost("/email/start", async (EmailStart input, AppDb db, HttpContext ctx, IEmailDelivery email, IConfiguration config) =>
        {
            if (input.Email.Length > 254 || !MailAddress.TryCreate(input.Email.Trim(), out var parsed) || parsed.Address != input.Email.Trim()) throw new ApiFailure(400, "Enter a valid email address.");
            var normalized = parsed.Address.ToUpperInvariant(); var now = DateTimeOffset.UtcNow;
            var browser = Access.RandomToken(); var code = RandomNumberGenerator.GetInt32(10000000, 100000000).ToString();
            var flow = new LoginFlow { Email = normalized, BrowserHash = Access.Hash(browser), ExpiresAt = now.AddMinutes(10) };
            flow.CodeHash = Access.Hash(flow.Id + ":" + code);
            // Serializes starts for one email across API replicas. No account-existence branch.
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({normalized}))");
            var count = await db.LoginFlows.CountAsync(x => x.Email == normalized && x.CreatedAt > now.AddHours(-1));
            if (count < 3) { db.LoginFlows.Add(flow); await db.SaveChangesAsync(); }
            await tx.CommitAsync();
            ctx.Response.Cookies.Append("oc.flow", browser, Access.Cookie(ctx, TimeSpan.FromMinutes(10)));
            if (count < 3)
            {
                var origin = config["Web:Origin"] ?? "http://localhost:5173";
                await email.Send(parsed.Address, "Your OpenCert sign-in code", $"Your sign-in code is {code}. It expires in 10 minutes.\n\nOr open {origin}/login#flow={flow.Id}&code={code} in the browser where you requested it, then select Confirm sign in.\n\nIf you did not request this, ignore this email.", "login-" + flow.Id, ctx.RequestAborted);
            }
            return Results.Accepted(value: new { flowId = flow.Id, message = "If delivery is available, your code will arrive shortly. Check spam, or wait before requesting another." });
        });
        auth.MapPost("/email/finish", async (EmailFinish input, AppDb db, HttpContext ctx, UserManager<User> users, SignInManager<User> signIn) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var flow = await db.LoginFlows.FromSqlInterpolated($"SELECT * FROM \"LoginFlows\" WHERE \"Id\" = {input.FlowId} FOR UPDATE").SingleOrDefaultAsync();
            if (flow is null || flow.Consumed || flow.ExpiresAt <= DateTimeOffset.UtcNow || flow.Attempts >= 5 || !Access.Matches(ctx.Request.Cookies["oc.flow"] ?? "", flow.BrowserHash)) throw new ApiFailure(400, "This code expired or belongs to another browser. Request a new code.");
            flow.Attempts++;
            if (!Access.Matches(flow.Id + ":" + input.Code, flow.CodeHash)) { await db.SaveChangesAsync(); await tx.CommitAsync(); throw new ApiFailure(400, "The code didn't match. Check it and try again."); }
            flow.Consumed = true;
            var user = await users.FindByEmailAsync(flow.Email);
            if (user is null)
            {
                user = new User { UserName = flow.Email, Email = flow.Email, EmailConfirmed = true, LockoutEnabled = true };
                var result = await users.CreateAsync(user); if (!result.Succeeded) throw new ApiFailure(400, "We couldn't create your account. Please request a new code.");
                var org = new Organization(); db.Organizations.Add(org); db.Memberships.Add(new() { OrganizationId = org.Id, UserId = user.Id });
            }
            user = await Access.LockUser(db, user.Id);
            if (await users.IsLockedOutAsync(user)) throw new ApiFailure(429, "Please wait before trying to sign in again.");
            var requiresMfa = await users.GetTwoFactorEnabledAsync(user);
            if (requiresMfa) flow.MfaUserId = user.Id;
            Access.Audit(db, ctx, "email.verified"); await db.SaveChangesAsync(); await tx.CommitAsync();
            if (!requiresMfa) await Access.SignIn(db, signIn, user, false);
            return Results.Ok(new { requiresMfa, flowId = flow.Id });
        });
        auth.MapPost("/mfa/finish", async (EmailFinish input, AppDb db, HttpContext ctx, UserManager<User> users, SignInManager<User> signIn) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var flow = await db.LoginFlows.FromSqlInterpolated($"SELECT * FROM \"LoginFlows\" WHERE \"Id\" = {input.FlowId} FOR UPDATE").SingleOrDefaultAsync();
            if (flow?.MfaUserId is null || !flow.Consumed || flow.MfaCompleted || flow.MfaAttempts >= 5 || flow.ExpiresAt <= DateTimeOffset.UtcNow || !Access.Matches(ctx.Request.Cookies["oc.flow"] ?? "", flow.BrowserHash)) throw new ApiFailure(400, "Sign in again to request a fresh verification.");
            var user = await Access.LockUser(db, flow.MfaUserId);
            if (await users.IsLockedOutAsync(user)) throw new ApiFailure(429, "Please wait before trying again.");
            flow.MfaAttempts++;
            var valid = await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, input.Code.Replace(" ", ""));
            if (!valid)
            {
                var hash = Access.Hash(input.Code.Trim());
                valid = await db.RecoveryCodes.Where(x => x.UserId == user.Id && x.Hash == hash).ExecuteDeleteAsync() == 1;
            }
            if (!valid) { await users.AccessFailedAsync(user); await db.SaveChangesAsync(); await tx.CommitAsync(); throw new ApiFailure(400, "That authenticator or recovery code didn't match."); }
            flow.MfaCompleted = true; await users.ResetAccessFailedCountAsync(user); await db.SaveChangesAsync(); await tx.CommitAsync();
            await Access.SignIn(db, signIn, user, true); return Results.NoContent();
        });
        auth.MapGet("/me", async (HttpContext ctx, AppDb db, UserManager<User> users) =>
        {
            var uid = ctx.UserId(); var user = await users.FindByIdAsync(uid) ?? throw new ApiFailure(401, "Sign in again.");
            var organizations = await (from member in db.Memberships join org in db.Organizations on member.OrganizationId equals org.Id where member.UserId == uid select new { org.Id, org.Name, member.Role }).ToListAsync();
            return Results.Ok(new { user.Email, user.DisplayName, mfaEnabled = user.TwoFactorEnabled, organizations });
        }).RequireAuthorization();
        auth.MapPost("/logout", async (HttpContext ctx, AppDb db, SignInManager<User> signIn) => { var session = await Access.Session(db, ctx); session.RevokedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(); await signIn.SignOutAsync(); return Results.NoContent(); }).RequireAuthorization();
        auth.MapGet("/sessions", async (HttpContext ctx, AppDb db) => { var uid = ctx.UserId(); return await db.Sessions.Where(x => x.UserId == uid && x.RevokedAt == null).Select(x => new { x.Id, x.CreatedAt, x.ExpiresAt }).ToListAsync(); }).RequireAuthorization();
        auth.MapDelete("/sessions/{id:guid}", async (Guid id, HttpContext ctx, AppDb db) => { var uid = ctx.UserId(); await db.Sessions.Where(x => x.UserId == uid && x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow)); return Results.NoContent(); }).RequireAuthorization();
        auth.MapPost("/totp/setup", async (HttpContext ctx, AppDb db, UserManager<User> users, SignInManager<User> signIn) =>
        {
            var session = await Access.Session(db, ctx); if (session.CreatedAt < DateTimeOffset.UtcNow.AddMinutes(-10)) throw new ApiFailure(403, "Sign in again before adding an authenticator.");
            await using var tx = await db.Database.BeginTransactionAsync(); var user = await Access.LockUser(db, ctx.UserId()); if (user.TwoFactorEnabled) throw new ApiFailure(409, "An authenticator is already enabled.");
            var secret = await users.GetAuthenticatorKeyAsync(user); if (secret is null) { await users.ResetAuthenticatorKeyAsync(user); secret = await users.GetAuthenticatorKeyAsync(user); await Access.Refresh(signIn, user, session); }
            await tx.CommitAsync(); return Results.Ok(new { secret, uri = $"otpauth://totp/OpenCert:{Uri.EscapeDataString(user.Email!)}?secret={secret}&issuer=OpenCert&digits=6" });
        }).RequireAuthorization();
        auth.MapPost("/totp/enable", async (CodeInput input, HttpContext ctx, AppDb db, UserManager<User> users, SignInManager<User> signIn) =>
        {
            var session = await Access.Session(db, ctx); await using var tx = await db.Database.BeginTransactionAsync(); var user = await Access.LockUser(db, ctx.UserId());
            if (user.TwoFactorEnabled) throw new ApiFailure(409, "Your authenticator is already enabled.");
            if (session.CreatedAt < DateTimeOffset.UtcNow.AddMinutes(-10) || !await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, input.Code)) throw new ApiFailure(400, "Use a fresh sign-in and the current six-digit authenticator code.");
            AccountEndpoints.Ensure(await users.SetTwoFactorEnabledAsync(user, true)); session.StrongAt = DateTimeOffset.UtcNow;
            var codes = AccountEndpoints.NewRecoveryCodes(db, user.Id);
            Access.Audit(db, ctx, "mfa.enabled"); await db.SaveChangesAsync(); await tx.CommitAsync(); await Access.Refresh(signIn, user, session); return Results.Ok(new { recoveryCodes = codes });
        }).RequireAuthorization();
        auth.MapPost("/step-up", async (CodeInput input, HttpContext ctx, AppDb db, UserManager<User> users) =>
        {
            var session = await Access.Session(db, ctx); await using var tx = await db.Database.BeginTransactionAsync(); var user = await Access.LockUser(db, ctx.UserId());
            if (await users.IsLockedOutAsync(user) || !user.TwoFactorEnabled) throw new ApiFailure(403, "Enable an authenticator or use a passkey before this action.");
            if (!await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, input.Code)) { await users.AccessFailedAsync(user); await tx.CommitAsync(); throw new ApiFailure(400, "The authenticator code didn't match."); }
            await users.ResetAccessFailedCountAsync(user); session.StrongAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.NoContent();
        }).RequireAuthorization();
    }
}
