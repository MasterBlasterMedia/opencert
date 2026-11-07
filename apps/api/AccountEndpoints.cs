using System.Net.Mail;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenCert.Contracts;
using OpenCert.Infrastructure;
namespace OpenCert.Api;

public static class AccountEndpoints
{
    public static string[] NewRecoveryCodes(AppDb db, string userId)
    {
        var codes = Enumerable.Range(0, 10).Select(_ => Convert.ToHexString(RandomNumberGenerator.GetBytes(10))).ToArray();
        db.RecoveryCodes.AddRange(codes.Select(code => new RecoveryCode { UserId = userId, Hash = Access.Hash(code) })); return codes;
    }
    public static void Ensure(IdentityResult result) { if (!result.Succeeded) throw new ApiFailure(409, "This account changed in another session or the details are unavailable. Refresh and try again."); }
    public static void MapAccount(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/account").RequireAuthorization().RequireRateLimiting("auth");
        group.MapPut("/profile", async (NameInput input, AppDb db, HttpContext ctx) =>
        {
            if (input.Name.Trim().Length is < 1 or > 80) throw new ApiFailure(400, "Use a display name between 1 and 80 characters.");
            var uid = ctx.UserId(); await db.Users.Where(u => u.Id == uid).ExecuteUpdateAsync(s => s.SetProperty(u => u.DisplayName, input.Name.Trim())); return Results.NoContent();
        });
        group.MapPost("/recovery-codes", async (AppDb db, HttpContext ctx, EmailOutbox outbox) =>
        {
            await Access.Session(db, ctx, true); await using var tx = await db.Database.BeginTransactionAsync(); var user = await Access.LockUser(db, ctx.UserId());
            if (!user.TwoFactorEnabled) throw new ApiFailure(409, "Enable an authenticator before generating recovery codes.");
            await db.RecoveryCodes.Where(c => c.UserId == user.Id).ExecuteDeleteAsync(); var codes = NewRecoveryCodes(db, user.Id);
            outbox.Enqueue(user.Email!, "Your OpenCert recovery codes changed", "New recovery codes were generated. All previous codes are now invalid. If you did not do this, sign in and revoke unfamiliar sessions.", "recovery-change-" + Guid.NewGuid());
            Access.Audit(db, ctx, "recovery_codes.regenerated"); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { recoveryCodes = codes });
        });
        group.MapDelete("/authenticator", async ([Microsoft.AspNetCore.Mvc.FromBody] ConfirmInput input, AppDb db, HttpContext ctx, UserManager<User> users, SignInManager<User> signIn, EmailOutbox outbox) =>
        {
            var session = await Access.Session(db, ctx, true);
            if (input.Confirmation != "REMOVE") throw new ApiFailure(400, "Confirm removal of your authenticator.");
            await using var tx = await db.Database.BeginTransactionAsync(); var user = await Access.LockUser(db, ctx.UserId());
            if (!user.EmailConfirmed) throw new ApiFailure(409, "Verify a recovery email first.");
            if (!user.TwoFactorEnabled) throw new ApiFailure(409, "No authenticator is enabled.");
            Ensure(await users.SetTwoFactorEnabledAsync(user, false)); Ensure(await users.ResetAuthenticatorKeyAsync(user));
            await db.RecoveryCodes.Where(c => c.UserId == user.Id).ExecuteDeleteAsync();
            await db.Sessions.Where(s => s.UserId == user.Id && s.Id != session.Id).ExecuteUpdateAsync(s => s.SetProperty(s => s.RevokedAt, DateTimeOffset.UtcNow));
            session.StrongAt = null; outbox.Enqueue(user.Email!, "Your OpenCert authenticator was removed", "Your authenticator and recovery codes have been disabled, and other sessions were signed out. If this was not you, sign in with email, review your passkeys and connected accounts, and set up a new authenticator.", "mfa-removed-" + Guid.NewGuid());
            Access.Audit(db, ctx, "mfa.disabled"); await db.SaveChangesAsync(); await tx.CommitAsync(); await Access.Refresh(signIn, user, session); return Results.NoContent();
        });
        group.MapPost("/email/start", async (EmailStart input, AppDb db, HttpContext ctx, EmailOutbox outbox) =>
        {
            var session = await Access.Session(db, ctx, true);
            if (input.Email.Length > 254 || !MailAddress.TryCreate(input.Email.Trim(), out var address) || address.Address != input.Email.Trim()) throw new ApiFailure(400, "Enter a valid email address.");
            await using var tx = await db.Database.BeginTransactionAsync(); var user = await Access.LockUser(db, ctx.UserId());
            if (await db.EmailChanges.CountAsync(c => c.UserId == user.Id && c.CreatedAt > DateTimeOffset.UtcNow.AddHours(-1)) >= 3) throw new ApiFailure(429, "Wait before requesting another email change.");
            await db.EmailChanges.Where(c => c.UserId == user.Id && !c.Consumed).ExecuteUpdateAsync(s => s.SetProperty(c => c.Consumed, true));
            var change = new EmailChange { UserId = user.Id, SessionId = session.Id, Email = address.Address.ToUpperInvariant() };
            var code = RandomNumberGenerator.GetInt32(10000000, 100000000).ToString(); change.CodeHash = Access.Hash(change.Id + ":" + code); db.EmailChanges.Add(change);
            outbox.Enqueue(address.Address, "Confirm your new OpenCert email", $"Your email-change code is {code}. Enter it in the OpenCert account settings where you requested the change. It expires in ten minutes. Ignore this if you did not request it.", "email-change-" + change.Id, change.ExpiresAt);
            await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Accepted(value: new { flowId = change.Id });
        });
        group.MapPost("/email/finish", async (EmailFinish input, AppDb db, HttpContext ctx, UserManager<User> users, SignInManager<User> signIn, EmailOutbox outbox) =>
        {
            var session = await Access.Session(db, ctx, true); await using var tx = await db.Database.BeginTransactionAsync(); var user = await Access.LockUser(db, ctx.UserId());
            var change = await db.EmailChanges.SingleOrDefaultAsync(c => c.Id == input.FlowId && c.UserId == user.Id && c.SessionId == session.Id);
            if (change is null || change.Consumed || change.Attempts >= 5 || change.ExpiresAt <= DateTimeOffset.UtcNow) throw new ApiFailure(400, "Request a fresh email-change code in this browser.");
            change.Attempts++;
            if (!Access.Matches(change.Id + ":" + input.Code, change.CodeHash)) { await db.SaveChangesAsync(); await tx.CommitAsync(); throw new ApiFailure(400, "That code did not match."); }
            var existing = await users.FindByEmailAsync(change.Email); if (existing is not null && existing.Id != user.Id) throw new ApiFailure(409, "That email address is unavailable.");
            var oldEmail = user.Email!;
            // The transaction has already verified the one-time code for this exact new address.
            Ensure(await users.SetEmailAsync(user, change.Email)); Ensure(await users.SetUserNameAsync(user, change.Email)); user.EmailConfirmed = true; Ensure(await users.UpdateAsync(user));
            change.Consumed = true; change.CodeHash = "";
            await db.Sessions.Where(s => s.UserId == user.Id && s.Id != session.Id).ExecuteUpdateAsync(s => s.SetProperty(s => s.RevokedAt, DateTimeOffset.UtcNow));
            outbox.Enqueue(oldEmail, "Your OpenCert email address changed", "Your sign-in email address changed. Other sessions were signed out. If you did not request this, contact the instance operator through the security disclosure page.", "email-changed-" + change.Id);
            Access.Audit(db, ctx, "account.email_changed"); await db.SaveChangesAsync(); await tx.CommitAsync(); await Access.Refresh(signIn, user, session); return Results.NoContent();
        });
        group.MapDelete("/", async ([Microsoft.AspNetCore.Mvc.FromBody] ConfirmInput input, AppDb db, HttpContext ctx, UserManager<User> users, SignInManager<User> signIn) =>
        {
            await Access.Session(db, ctx, true); if (input.Confirmation != "DELETE") throw new ApiFailure(400, "Type DELETE to confirm account deletion.");
            await using var tx = await db.Database.BeginTransactionAsync(); var user = await Access.LockUser(db, ctx.UserId());
            if (await db.Memberships.AnyAsync(m => m.UserId == user.Id && m.Role == "Owner")) throw new ApiFailure(409, "Transfer ownership or delete every workspace you own before deleting your account.");
            await db.LoginFlows.Where(f => f.Email == user.NormalizedEmail || f.MfaUserId == user.Id).ExecuteDeleteAsync();
            await db.AuthChallenges.Where(c => c.UserId == user.Id).ExecuteDeleteAsync();
            await db.AuditEvents.Where(a => a.UserId == user.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.UserId, (string?)null));
            Ensure(await users.DeleteAsync(user)); await tx.CommitAsync(); await signIn.SignOutAsync(); return Results.NoContent();
        });
    }
}
