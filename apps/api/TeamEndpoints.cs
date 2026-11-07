using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenCert.Contracts;
using OpenCert.Infrastructure;
namespace OpenCert.Api;

public static class TeamEndpoints
{
    private static readonly string[] Roles = ["Administrator", "Member", "Viewer"];
    private static async Task<Organization> LockOrganization(AppDb db, Guid id) =>
        await db.Organizations.FromSqlInterpolated($"SELECT * FROM \"Organizations\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync() ?? throw new ApiFailure(404, "Workspace not found.");
    public static void MapTeams(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/organizations/{organization:guid}").RequireAuthorization();
        group.MapGet("/members", async (Guid organization, AppDb db, HttpContext ctx) =>
        {
            await Access.Member(db, ctx, organization);
            return await (from member in db.Memberships join user in db.Users on member.UserId equals user.Id where member.OrganizationId == organization select new { member.UserId, member.Role, user.DisplayName, user.Email }).ToListAsync();
        });
        group.MapGet("/invitations", async (Guid organization, AppDb db, HttpContext ctx) =>
        {
            await Access.Member(db, ctx, organization, admin: true);
            return await db.Invitations.Where(i => i.OrganizationId == organization && i.RevokedAt == null && i.AcceptedAt == null && i.ExpiresAt > DateTimeOffset.UtcNow).Select(i => new { i.Id, i.Email, i.Role, i.ExpiresAt }).ToListAsync();
        });
        group.MapPost("/invitations", async (Guid organization, InviteInput input, AppDb db, HttpContext ctx, EmailOutbox outbox, IConfiguration config) =>
        {
            await Access.Session(db, ctx, true);
            await using var tx = await db.Database.BeginTransactionAsync(); var org = await LockOrganization(db, organization);
            var role = await Access.Member(db, ctx, organization, admin: true);
            if (!Roles.Contains(input.Role) || (role != "Owner" && input.Role == "Administrator")) throw new ApiFailure(403, "Only the owner can invite administrators. Choose a supported role.");
            if (input.Email.Length > 254 || !MailAddress.TryCreate(input.Email.Trim(), out var address) || address.Address != input.Email.Trim()) throw new ApiFailure(400, "Enter a valid email address.");
            var email = address.Address.ToUpperInvariant();
            if (await db.Invitations.CountAsync(i => i.OrganizationId == organization && i.CreatedAt > DateTimeOffset.UtcNow.AddDays(-1)) >= 25) throw new ApiFailure(429, "This workspace has reached its daily invitation limit.");
            if (await db.Memberships.CountAsync(m => m.OrganizationId == organization) >= 100) throw new ApiFailure(409, "This workspace has reached its 100-member limit.");
            if (await (from m in db.Memberships join u in db.Users on m.UserId equals u.Id where m.OrganizationId == organization && u.NormalizedEmail == email select m).AnyAsync()) throw new ApiFailure(409, "That person is already a member.");
            await db.Invitations.Where(i => i.OrganizationId == organization && i.Email == email && i.AcceptedAt == null && i.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(i => i.RevokedAt, DateTimeOffset.UtcNow));
            var token = Access.RandomToken(); var invitation = new Invitation { OrganizationId = organization, Email = email, Role = input.Role, TokenHash = Access.Hash(token) };
            db.Invitations.Add(invitation);
            outbox.Enqueue(address.Address, "You’re invited to an OpenCert workspace", $"You have been invited to {org.Name} as {input.Role}. Sign in with this email address, then intentionally accept the invitation at {config["Web:Origin"] ?? "http://localhost:5173"}/app/team#invite={invitation.Id}&token={token}. This invitation expires in seven days. Ignore it if you did not expect it.", "invite-" + invitation.Id, invitation.ExpiresAt);
            Access.Audit(db, ctx, "member.invited", organization, invitation.Id); await db.SaveChangesAsync(); await tx.CommitAsync();
            return Results.Accepted(value: new { invitation.Id, message = "Your invitation is queued for delivery." });
        });
        group.MapDelete("/invitations/{id:guid}", async (Guid organization, Guid id, AppDb db, HttpContext ctx) =>
        {
            await Access.Session(db, ctx, true); await using var tx = await db.Database.BeginTransactionAsync(); await LockOrganization(db, organization); await Access.Member(db, ctx, organization, admin: true);
            await db.Invitations.Where(i => i.Id == id && i.OrganizationId == organization && i.AcceptedAt == null).ExecuteUpdateAsync(s => s.SetProperty(i => i.RevokedAt, DateTimeOffset.UtcNow));
            Access.Audit(db, ctx, "invitation.revoked", organization, id); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.NoContent();
        });
        app.MapPost("/api/v1/invitations/{id:guid}/accept", async (Guid id, TokenInput input, AppDb db, HttpContext ctx) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(); var user = await Access.LockUser(db, ctx.UserId());
            var candidate = await db.Invitations.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id) ?? throw new ApiFailure(404, "Invitation not found.");
            var org = await LockOrganization(db, candidate.OrganizationId);
            var invitation = await db.Invitations.SingleAsync(i => i.Id == id);
            if (!user.EmailConfirmed || invitation.Email != user.NormalizedEmail || invitation.RevokedAt is not null || invitation.AcceptedAt is not null || invitation.ExpiresAt <= DateTimeOffset.UtcNow || !Access.Matches(input.Token, invitation.TokenHash)) throw new ApiFailure(400, "This invitation expired, was used, or belongs to a different email address.");
            if (await db.Memberships.CountAsync(m => m.OrganizationId == org.Id) >= 100) throw new ApiFailure(409, "This workspace has reached its member limit.");
            if (!await db.Memberships.AnyAsync(m => m.OrganizationId == org.Id && m.UserId == user.Id)) db.Memberships.Add(new() { OrganizationId = org.Id, UserId = user.Id, Role = invitation.Role });
            invitation.AcceptedAt = DateTimeOffset.UtcNow; invitation.TokenHash = "";
            Access.Audit(db, ctx, "invitation.accepted", org.Id, id); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { org.Id, org.Name });
        }).RequireAuthorization().RequireRateLimiting("auth");
        group.MapPut("/members/{userId}/role", async (Guid organization, string userId, RoleInput input, AppDb db, HttpContext ctx) =>
        {
            await Access.Session(db, ctx, true); await using var tx = await db.Database.BeginTransactionAsync(); await LockOrganization(db, organization);
            if (await Access.Member(db, ctx, organization) != "Owner") throw new ApiFailure(403, "Only the workspace owner can change member roles.");
            var member = await db.Memberships.SingleOrDefaultAsync(m => m.OrganizationId == organization && m.UserId == userId) ?? throw new ApiFailure(404, "Member not found.");
            if (!Roles.Contains(input.Role) || member.Role == "Owner") throw new ApiFailure(400, "Use ownership transfer to change the owner. Choose Administrator, Member or Viewer.");
            member.Role = input.Role; Access.Audit(db, ctx, "member.role_changed", organization, userId); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.NoContent();
        });
        group.MapDelete("/members/{userId}", async (Guid organization, string userId, AppDb db, HttpContext ctx) =>
        {
            await Access.Session(db, ctx, true); await using var tx = await db.Database.BeginTransactionAsync(); await LockOrganization(db, organization); var role = await Access.Member(db, ctx, organization);
            var member = await db.Memberships.SingleOrDefaultAsync(m => m.OrganizationId == organization && m.UserId == userId) ?? throw new ApiFailure(404, "Member not found.");
            if (member.Role == "Owner") throw new ApiFailure(409, "Transfer ownership before removing the owner.");
            if (userId != ctx.UserId() && role != "Owner" && !(role == "Administrator" && member.Role is "Member" or "Viewer")) throw new ApiFailure(403, "Your role cannot remove this member.");
            db.Memberships.Remove(member); Access.Audit(db, ctx, "member.removed", organization, userId); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.NoContent();
        });
        group.MapPost("/ownership", async (Guid organization, TransferInput input, AppDb db, HttpContext ctx) =>
        {
            await Access.Session(db, ctx, true); if (input.UserId == ctx.UserId()) throw new ApiFailure(400, "Choose another member.");
            await using var tx = await db.Database.BeginTransactionAsync(); await Access.LockUser(db, input.UserId); await LockOrganization(db, organization);
            if (await Access.Member(db, ctx, organization) != "Owner") throw new ApiFailure(403, "Only the workspace owner can transfer ownership.");
            var next = await db.Memberships.SingleOrDefaultAsync(m => m.OrganizationId == organization && m.UserId == input.UserId) ?? throw new ApiFailure(404, "The new owner must already be a member.");
            var current = await db.Memberships.SingleAsync(m => m.OrganizationId == organization && m.UserId == ctx.UserId()); next.Role = "Owner"; current.Role = "Administrator";
            Access.Audit(db, ctx, "organization.ownership_transferred", organization, input.UserId); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.NoContent();
        });
        group.MapPut("/profile", async (Guid organization, NameInput input, AppDb db, HttpContext ctx) =>
        {
            await Access.Member(db, ctx, organization, admin: true); if (input.Name.Trim().Length is < 1 or > 80) throw new ApiFailure(400, "Use 1–80 characters for the workspace name.");
            await db.Organizations.Where(o => o.Id == organization).ExecuteUpdateAsync(s => s.SetProperty(o => o.Name, input.Name.Trim())); return Results.NoContent();
        });
        group.MapDelete("/", async (Guid organization, [Microsoft.AspNetCore.Mvc.FromBody] ConfirmInput input, AppDb db, HttpContext ctx) =>
        {
            await Access.Session(db, ctx, true); await using var tx = await db.Database.BeginTransactionAsync(); var org = await LockOrganization(db, organization);
            if (await Access.Member(db, ctx, organization) != "Owner") throw new ApiFailure(403, "Only the owner can delete a workspace.");
            if (input.Confirmation != org.Name) throw new ApiFailure(400, "Type the workspace name to confirm deletion.");
            if (await db.Orders.AnyAsync(o => o.OrganizationId == organization)) throw new ApiFailure(409, "This workspace has certificate history. Retain it until the certificate retention and export workflow is available.");
            if (await db.Memberships.AnyAsync(m => m.OrganizationId == organization && m.UserId != ctx.UserId())) throw new ApiFailure(409, "Remove other members before deleting the workspace.");
            var sites = db.Websites.Where(w => w.OrganizationId == organization).Select(w => w.Id);
            await db.Jobs.Where(j => sites.Contains(j.ResourceId)).ExecuteDeleteAsync(); db.Organizations.Remove(org); Access.Audit(db, ctx, "organization.deleted", organization); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.NoContent();
        });
    }
}
