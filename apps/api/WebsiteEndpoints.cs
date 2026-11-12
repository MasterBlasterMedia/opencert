using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenCert.Contracts;
using OpenCert.Domain;
using OpenCert.Infrastructure;
namespace OpenCert.Api;

public static class WebsiteEndpoints
{
    static object View(Website w) => new { w.Id, w.Hostname, w.SetupMethod, w.HttpsTrusted, w.ExpiresAt, w.CheckedAt, w.CertificateObservedAt, w.NextCheckAt, w.Issuer, w.Fingerprint, w.HostingProvider, w.DnsProvider, w.Diagnostic, status = WebsiteStatus.Describe(w.HttpsTrusted, w.ExpiresAt, w.CheckedAt, DateTimeOffset.UtcNow), renewal = "Not confirmed", discovery = JsonSerializer.Deserialize<JsonElement>(w.DiscoveryJson) };
    public static void MapWebsites(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/organizations/{organization:guid}").RequireAuthorization();
        group.MapGet("/websites", async (Guid organization, HttpContext ctx, AppDb db, int page = 1) => { await Access.Member(db, ctx, organization); return Results.Ok((await db.Websites.Where(x => x.OrganizationId == organization).OrderBy(x => x.Hostname).Skip((Math.Clamp(page, 1, 10000) - 1) * 50).Take(50).ToListAsync()).Select(View)); });
        group.MapPost("/websites", async (Guid organization, WebsiteInput input, HttpContext ctx, AppDb db) =>
        {
            await Access.Member(db, ctx, organization, write: true); var host = DomainName.Normalize(input.Hostname);
            if (await db.Websites.AnyAsync(w => w.OrganizationId == organization && w.Hostname == host)) throw new ApiFailure(409, "This website is already in your workspace.");
            if (await db.Websites.CountAsync(w => w.OrganizationId == organization) >= 100) throw new ApiFailure(409, "This workspace has reached the configured 100-website limit.");
            var site = new Website { OrganizationId = organization, Hostname = host }; db.Websites.Add(site);
            db.Jobs.Add(new() { Kind = "Inspect", ResourceId = site.Id, DeduplicationKey = "initial-inspect:" + site.Id });
            Access.Audit(db, ctx, "website.added", organization, site.Id); await db.SaveChangesAsync(); return Results.Created($"/api/v1/organizations/{organization}/websites/{site.Id}", View(site));
        });
        group.MapGet("/websites/{id:guid}", async (Guid organization, Guid id, HttpContext ctx, AppDb db) => { await Access.Member(db, ctx, organization); var site = await db.Websites.SingleOrDefaultAsync(w => w.OrganizationId == organization && w.Id == id) ?? throw new ApiFailure(404, "This website wasn't found."); return Results.Ok(View(site)); });
        group.MapPost("/websites/{id:guid}/check", async (Guid organization, Guid id, HttpContext ctx, AppDb db) =>
        {
            await Access.Member(db, ctx, organization, write: true); if (!await db.Websites.AnyAsync(w => w.OrganizationId == organization && w.Id == id)) throw new ApiFailure(404, "This website wasn't found.");
            if (!await db.Jobs.AnyAsync(j => j.ResourceId == id && j.Kind == "Inspect" && (j.State == "Pending" || j.State == "Running"))) { db.Jobs.Add(new() { Kind = "Inspect", ResourceId = id, DeduplicationKey = "inspect:" + Guid.NewGuid() }); await db.SaveChangesAsync(); }
            return Results.Accepted(value: new { message = "Your website check is queued." });
        }).RequireRateLimiting("diagnostics");
        group.MapPut("/websites/{id:guid}/setup", async (Guid organization, Guid id, SetupInput input, HttpContext ctx, AppDb db) =>
        {
            await Access.Member(db, ctx, organization, write: true); if (input.Method is not ("ProviderManaged" or "ManualCsr" or "Undecided")) throw new ApiFailure(400, "Choose a supported setup method.");
            var site = await db.Websites.SingleOrDefaultAsync(w => w.OrganizationId == organization && w.Id == id) ?? throw new ApiFailure(404, "This website wasn't found."); site.SetupMethod = input.Method; await db.SaveChangesAsync(); return Results.Ok(View(site));
        });
        group.MapDelete("/websites/{id:guid}", async (Guid organization, Guid id, HttpContext ctx, AppDb db) =>
        {
            await Access.Member(db, ctx, organization, admin: true); var site = await db.Websites.SingleOrDefaultAsync(w => w.OrganizationId == organization && w.Id == id) ?? throw new ApiFailure(404, "This website wasn't found.");
            if (await db.Orders.AnyAsync(o => o.WebsiteId == id)) throw new ApiFailure(409, "Keep this website while its certificate history is retained.");
            db.Websites.Remove(site); Access.Audit(db, ctx, "website.removed", organization, id); await db.SaveChangesAsync(); return Results.NoContent();
        });
        group.MapPost("/websites/{id:guid}/orders", async (Guid organization, Guid id, OrderInput input, HttpContext ctx, AppDb db, AcmeOrders acme) =>
        {
            await Access.Member(db, ctx, organization, write: true); await Access.Session(db, ctx, strong: true);
            await using var tx = await db.Database.BeginTransactionAsync();
            var site = await db.Websites.FromSqlInterpolated($"SELECT * FROM \"Websites\" WHERE \"Id\" = {id} AND \"OrganizationId\" = {organization} FOR UPDATE").SingleOrDefaultAsync() ?? throw new ApiFailure(404, "This website wasn't found.");
            if (!input.AcceptTerms) throw new ApiFailure(400, "Read and accept the certificate authority's subscriber agreement first.");
            var names = input.Hostnames.Select(n => DomainName.Normalize(n, true)).ToArray();
            foreach (var name in names) if (name != site.Hostname && name != "*." + site.Hostname && !await db.Websites.AnyAsync(w => w.OrganizationId == organization && w.Hostname == name)) throw new ApiFailure(400, "Add each certificate name to this workspace before requesting it.");
            CsrPolicy.Validate(input.CsrPem, names);
            if (await db.Orders.AnyAsync(o => o.WebsiteId == id && (o.State == "Queued" || o.State == "AwaitingVerification" || o.State == "Verifying"))) throw new ApiFailure(409, "Finish your existing certificate request first.");
            var order = new CertificateOrder { OrganizationId = organization, WebsiteId = id, CsrPem = input.CsrPem, HostnamesJson = JsonSerializer.Serialize(names), Staging = acme.IsStaging, Directory = acme.DirectoryUrl };
            db.Orders.Add(order); db.Jobs.Add(new() { Kind = "AcmeCreate", ResourceId = order.Id, DeduplicationKey = "create:" + order.Id }); Access.Audit(db, ctx, "certificate.requested", organization, order.Id); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Accepted(value: new { order.Id });
        });
        group.MapGet("/websites/{id:guid}/orders", async (Guid organization, Guid id, HttpContext ctx, AppDb db) => { await Access.Member(db, ctx, organization); return await db.Orders.Where(o => o.OrganizationId == organization && o.WebsiteId == id).OrderByDescending(o => o.CreatedAt).Take(50).Select(o => new { o.Id, o.State, o.CreatedAt, o.ExpiresAt, o.RenewalDueAt, o.RenewalSource, o.RenewalWindowStart, o.RenewalWindowEnd, o.RenewalInfoCheckedAt, o.NextRenewalInfoAt, o.RenewalInfoError, o.Staging, o.Diagnostic, o.ChallengesJson, o.HostnamesJson }).ToListAsync(); });
        group.MapPost("/orders/{id:guid}/verify", async (Guid organization, Guid id, HttpContext ctx, AppDb db) =>
        {
            await Access.Member(db, ctx, organization, write: true); await using var tx = await db.Database.BeginTransactionAsync();
            var order = await db.Orders.FromSqlInterpolated($"SELECT * FROM \"Orders\" WHERE \"Id\" = {id} AND \"OrganizationId\" = {organization} FOR UPDATE").SingleOrDefaultAsync() ?? throw new ApiFailure(404, "Request not found.");
            if (order.State is not ("AwaitingVerification" or "Verifying")) throw new ApiFailure(409, "This request is not ready for domain verification.");
            if (!await db.Jobs.AnyAsync(j => j.ResourceId == id && j.Kind == "AcmeVerify" && (j.State == "Pending" || j.State == "Running"))) { db.Jobs.Add(new() { Kind = "AcmeVerify", ResourceId = id, DeduplicationKey = "verify:" + Guid.NewGuid() }); await db.SaveChangesAsync(); }
            await tx.CommitAsync(); return Results.Accepted();
        });
        group.MapGet("/orders/{id:guid}/certificate", async (Guid organization, Guid id, HttpContext ctx, AppDb db) => { await Access.Member(db, ctx, organization); var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == id && o.OrganizationId == organization) ?? throw new ApiFailure(404, "Request not found."); if (order.CertificatePem is null) throw new ApiFailure(409, "Your certificate has not been issued yet."); return Results.File(System.Text.Encoding.UTF8.GetBytes(order.CertificatePem), "application/x-pem-file", "fullchain.pem"); });
        group.MapPost("/orders/{id:guid}/revoke", async (Guid organization, Guid id, HttpContext ctx, AppDb db) =>
        {
            await Access.Member(db, ctx, organization, admin: true); await Access.Session(db, ctx, strong: true);
            if (!await db.Orders.AnyAsync(o => o.Id == id && o.OrganizationId == organization && o.State == "Issued")) throw new ApiFailure(404, "Issued certificate not found.");
            db.Jobs.Add(new() { Kind = "AcmeRevoke", ResourceId = id, DeduplicationKey = "revoke:" + id }); Access.Audit(db, ctx, "certificate.revocation_requested", organization, id); await db.SaveChangesAsync(); return Results.Accepted();
        });
        group.MapGet("/notifications", async (Guid organization, HttpContext ctx, AppDb db) => { await Access.Member(db, ctx, organization); return await db.Notifications.Where(n => n.OrganizationId == organization).OrderByDescending(n => n.CreatedAt).Take(100).Select(n => new { n.Id, n.Message, n.CreatedAt, n.Read }).ToListAsync(); });
        group.MapPut("/notifications/{id:guid}/read", async (Guid organization, Guid id, HttpContext ctx, AppDb db) => { await Access.Member(db, ctx, organization); await db.Notifications.Where(n => n.Id == id && n.OrganizationId == organization).ExecuteUpdateAsync(s => s.SetProperty(n => n.Read, true)); return Results.NoContent(); });
        group.MapGet("/activity", async (Guid organization, HttpContext ctx, AppDb db) => { await Access.Member(db, ctx, organization); return await db.AuditEvents.Where(n => n.OrganizationId == organization).OrderByDescending(n => n.At).Take(100).Select(n => new { n.Action, n.At, n.ResourceId }).ToListAsync(); });
        app.MapPost("/api/v1/organizations", async (NameInput input, AppDb db, HttpContext ctx) => { if (input.Name.Trim().Length is < 1 or > 80) throw new ApiFailure(400, "Use a workspace name between 1 and 80 characters."); var org = new Organization { Name = input.Name.Trim() }; db.Organizations.Add(org); db.Memberships.Add(new() { OrganizationId = org.Id, UserId = ctx.UserId() }); await db.SaveChangesAsync(); return Results.Ok(new { org.Id, org.Name }); }).RequireAuthorization();
    }
}
