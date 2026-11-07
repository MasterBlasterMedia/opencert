using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenCert.Contracts;
using OpenCert.Infrastructure;
namespace OpenCert.Api;

public static class PasskeyEndpoints
{
    private static async Task CreateChallenge(AppDb db, HttpContext ctx, string purpose, string? userId)
    {
        var token = Access.RandomToken(); var challenge = new AuthChallenge { Purpose = purpose, UserId = userId, BrowserHash = Access.Hash(token) };
        db.AuthChallenges.Add(challenge); await db.SaveChangesAsync(); ctx.Response.Cookies.Append("oc.passkey", challenge.Id + "." + token, Access.Cookie(ctx, TimeSpan.FromMinutes(5)));
    }
    private static async Task<AuthChallenge> Consume(AppDb db, HttpContext ctx, string purpose)
    {
        var cookie = (ctx.Request.Cookies["oc.passkey"] ?? "").Split('.');
        if (cookie.Length != 2 || !Guid.TryParse(cookie[0], out var id)) throw new ApiFailure(400, "Request a new passkey challenge.");
        var challenge = await db.AuthChallenges.FromSqlInterpolated($"SELECT * FROM \"AuthChallenges\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync();
        if (challenge is null || challenge.Consumed || challenge.Purpose != purpose || challenge.ExpiresAt < DateTimeOffset.UtcNow || !Access.Matches(cookie[1], challenge.BrowserHash)) throw new ApiFailure(400, "This passkey challenge expired or was already used.");
        challenge.Consumed = true; ctx.Response.Cookies.Delete("oc.passkey"); await db.SaveChangesAsync(); return challenge;
    }
    public static void MapPasskeys(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/auth/passkeys").RequireRateLimiting("auth");
        group.MapPost("/register/options", async (HttpContext ctx, AppDb db, UserManager<User> users, SignInManager<User> sm) =>
        {
            var user = (await users.FindByIdAsync(ctx.UserId()))!; var session = await Access.Session(db, ctx, user.TwoFactorEnabled);
            if (session.CreatedAt < DateTimeOffset.UtcNow.AddMinutes(-10) && (session.StrongAt is null || session.StrongAt < DateTimeOffset.UtcNow.AddMinutes(-10))) throw new ApiFailure(403, "Sign in again before registering a passkey.");
            await CreateChallenge(db, ctx, "register", user.Id);
            return Results.Content(await sm.MakePasskeyCreationOptionsAsync(new PasskeyUserEntity { Id = user.Id, Name = user.Email!, DisplayName = user.DisplayName }), "application/json");
        }).RequireAuthorization();
        group.MapPost("/register", async (PasskeyInput input, HttpContext ctx, AppDb db, UserManager<User> users, SignInManager<User> sm) =>
        {
            if (input.Name.Trim().Length is < 1 or > 80) throw new ApiFailure(400, "Give your passkey a name between 1 and 80 characters.");
            await using var tx = await db.Database.BeginTransactionAsync(); var challenge = await Consume(db, ctx, "register");
            if (challenge.UserId != ctx.UserId()) throw new ApiFailure(403, "This challenge belongs to a different account.");
            var result = await sm.PerformPasskeyAttestationAsync(input.CredentialJson);
            if (!result.Succeeded || result.UserEntity.Id != ctx.UserId() || !result.Passkey.IsUserVerified) { await tx.CommitAsync(); throw new ApiFailure(400, "Your passkey could not be verified. Try registering again."); }
            var user = (await users.FindByIdAsync(ctx.UserId()))!; result.Passkey.Name = input.Name.Trim();
            var save = await users.AddOrUpdatePasskeyAsync(user, result.Passkey); if (!save.Succeeded) throw new ApiFailure(409, "This passkey could not be saved.");
            Access.Audit(db, ctx, "passkey.added"); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.NoContent();
        }).RequireAuthorization();
        group.MapPost("/login/options", async (HttpContext ctx, AppDb db, SignInManager<User> sm) => { await CreateChallenge(db, ctx, "login", null); return Results.Content(await sm.MakePasskeyRequestOptionsAsync(null), "application/json"); });
        group.MapPost("/login", async (PasskeyInput input, HttpContext ctx, AppDb db, UserManager<User> users, SignInManager<User> sm) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(); await Consume(db, ctx, "login");
            var result = await sm.PerformPasskeyAssertionAsync(input.CredentialJson);
            if (!result.Succeeded || !result.Passkey.IsUserVerified || await users.IsLockedOutAsync(result.User)) { await tx.CommitAsync(); throw new ApiFailure(400, "Your passkey could not be verified. Try again or use email sign-in."); }
            if (ctx.User.Identity?.IsAuthenticated == true && result.User.Id != ctx.UserId()) { await tx.CommitAsync(); throw new ApiFailure(403, "Use a passkey belonging to the signed-in account."); }
            var save = await users.AddOrUpdatePasskeyAsync(result.User, result.Passkey); if (!save.Succeeded) throw new ApiFailure(409, "The passkey changed in another session. Try again.");
            await tx.CommitAsync(); await Access.SignIn(db, sm, result.User, true); return Results.NoContent();
        });
        group.MapGet("/", async (HttpContext ctx, UserManager<User> users) => { var user = (await users.FindByIdAsync(ctx.UserId()))!; return (await users.GetPasskeysAsync(user)).Select(p => new { id = Convert.ToHexString(p.CredentialId), p.Name, p.CreatedAt }); }).RequireAuthorization();
        group.MapDelete("/{id}", async (string id, HttpContext ctx, AppDb db, UserManager<User> users) =>
        {
            await Access.Session(db, ctx, true); var user = (await users.FindByIdAsync(ctx.UserId()))!;
            if (!user.EmailConfirmed) throw new ApiFailure(409, "Verify a recovery email before removing a passkey.");
            byte[] credential; try { credential = Convert.FromHexString(id); } catch (FormatException) { throw new ApiFailure(400, "Invalid passkey identifier."); }
            var result = await users.RemovePasskeyAsync(user, credential); if (!result.Succeeded) throw new ApiFailure(404, "Passkey not found.");
            Access.Audit(db, ctx, "passkey.removed"); await db.SaveChangesAsync(); return Results.NoContent();
        }).RequireAuthorization();
    }
}
