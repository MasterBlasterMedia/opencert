using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenCert.Infrastructure;
namespace OpenCert.Api;

public sealed class ApiFailure(int status, string message) : Exception(message) { public int Status { get; } = status; }
public static class Access
{
    public static string UserId(this HttpContext context) => context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new ApiFailure(401, "Please sign in to continue.");
    public static async Task<string> Member(AppDb db, HttpContext context, Guid organization, bool write = false, bool admin = false)
    {
        var userId = context.UserId();
        var role = await db.Memberships.Where(x => x.OrganizationId == organization && x.UserId == userId).Select(x => x.Role).SingleOrDefaultAsync();
        if (role is null) throw new ApiFailure(404, "This workspace wasn't found.");
        if (write && role == "Viewer" || admin && role is not ("Owner" or "Administrator")) throw new ApiFailure(403, "Your workspace role does not allow this action.");
        return role;
    }
    public static async Task<UserSession> Session(AppDb db, HttpContext context, bool strong = false)
    {
        if (!Guid.TryParse(context.User.FindFirstValue("sid"), out var id)) throw new ApiFailure(401, "Please sign in again.");
        var uid = context.UserId();
        var session = await db.Sessions.SingleOrDefaultAsync(x => x.Id == id && x.UserId == uid && x.RevokedAt == null && x.ExpiresAt > DateTimeOffset.UtcNow) ?? throw new ApiFailure(401, "Your session has ended. Sign in again.");
        if (strong && (session.StrongAt is null || session.StrongAt < DateTimeOffset.UtcNow.AddMinutes(-10))) throw new ApiFailure(403, "Confirm with your authenticator or passkey in Account Settings before this action.");
        return session;
    }
    public static async Task<User> LockUser(AppDb db, string userId)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Account locks require a transaction.");
        var user = await db.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {userId} FOR UPDATE").SingleOrDefaultAsync() ?? throw new ApiFailure(404, "Account not found.");
        // Authentication may already have tracked this user before the row lock was acquired.
        await db.Entry(user).ReloadAsync(); return user;
    }
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool Matches(string value, string hash) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Hash(value)), Encoding.UTF8.GetBytes(hash));
    public static string RandomToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public static CookieOptions Cookie(HttpContext ctx, TimeSpan lifetime) => new() { HttpOnly = true, Secure = !ctx.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment(), SameSite = SameSiteMode.Lax, Path = "/", MaxAge = lifetime, IsEssential = true };
    public static void Audit(AppDb db, HttpContext ctx, string action, Guid? organization = null, object? resource = null) => db.AuditEvents.Add(new() { OrganizationId = organization, UserId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier), Action = action, ResourceId = resource?.ToString() });
    public static Task Refresh(SignInManager<User> signIn, User user, UserSession session) => signIn.SignInWithClaimsAsync(user, false, [new Claim("sid", session.Id.ToString()), new Claim("amr", session.StrongAt is null ? "email" : "mfa")]);
    public static async Task SignIn(AppDb db, SignInManager<User> signIn, User user, bool strong)
    {
        var session = new UserSession { UserId = user.Id, StrongAt = strong ? DateTimeOffset.UtcNow : null };
        db.Sessions.Add(session); await db.SaveChangesAsync();
        await signIn.SignInWithClaimsAsync(user, false, [new Claim("sid", session.Id.ToString()), new Claim("amr", strong ? "mfa" : "email")]);
    }
}
