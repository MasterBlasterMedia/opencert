using Microsoft.EntityFrameworkCore;
namespace OpenCert.Infrastructure;

public sealed class OperationalMaintenance(AppDb db, EmailOutbox outbox)
{
    public async Task Run(DateTimeOffset now, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // One producer at a time; all schedules, notices and mail are committed together.
        if (!await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({713109714L}) AS \"Value\"").SingleAsync(ct)) return;
        var untrackedRenewals = await db.Orders.Where(o => o.State == "Issued" && o.CertificatePem != null && o.ExpiresAt > now && o.RenewalInfoCheckedAt == null && !db.Jobs.Any(j => j.DeduplicationKey == "ari:" + o.Id.ToString())).OrderBy(o => o.CreatedAt).Take(100).ToListAsync(ct);
        foreach (var order in untrackedRenewals)
        {
            order.NextRenewalInfoAt = now;
            db.Jobs.Add(new Job { Kind = "AcmeRenewalInfo", ResourceId = order.Id, DeduplicationKey = "ari:" + order.Id, DueAt = now });
        }
        var websites = await db.Websites.Where(w => w.NextCheckAt <= now).OrderBy(w => w.NextCheckAt).Take(200).ToListAsync(ct);
        foreach (var website in websites)
        {
            if (!await db.Jobs.AnyAsync(j => j.ResourceId == website.Id && j.Kind == "Inspect" && (j.State == "Pending" || j.State == "Running"), ct))
                db.Jobs.Add(new Job { Kind = "Inspect", ResourceId = website.Id, DeduplicationKey = "scheduled-inspect:" + Guid.NewGuid(), DueAt = now });
            website.NextCheckAt = now.AddHours(6);
            if (website.ExpiresAt is not { } expires || website.Fingerprint is null) continue;
            var stage = ExpirationStage(expires, now);
            if (stage is null) continue;
            var timing = stage == 0 ? "has expired" : $"expires within {stage} days";
            var message = $"The last certificate observed for {website.Hostname} {timing} (expiry {expires:yyyy-MM-dd HH:mm} UTC). Check your host or install a replacement, then verify live HTTPS in OpenCert. Automatic renewal has not been confirmed.";
            await Notify(website.OrganizationId, "expiry:" + website.Id + ":" + website.Fingerprint + ":" + stage, "Check the certificate for " + website.Hostname, message, now, ct);
        }

        var due = await db.Orders.Where(o => o.State == "Issued" && o.RenewalDueAt <= now && !db.Notifications.Any(n => n.DeduplicationKey == "renewal-due:" + o.Id.ToString()) && !db.Orders.Any(newer => newer.WebsiteId == o.WebsiteId && newer.State == "Issued" && newer.CreatedAt > o.CreatedAt)).OrderBy(o => o.RenewalDueAt).Take(100).ToListAsync(ct);
        foreach (var order in due)
        {
            var site = await db.Websites.SingleAsync(w => w.Id == order.WebsiteId, ct);
            var basis = order.RenewalSource == "Ari" ? "the certificate authority's suggested renewal window" : "its actual validity period";
            var message = $"The {(order.Staging ? "test " : "")}certificate requested for {site.Hostname} is due for renewal based on {basis}. Create a new request on the machine holding your private key, complete verification and install the replacement. Renewal and installation remain manual.";
            await Notify(order.OrganizationId, "renewal-due:" + order.Id, "Renew your certificate for " + site.Hostname, message, now, ct, email: !order.Staging);
        }

        // A process can crash on its last attempt before it updates the order itself.
        var exhausted = await db.Orders.Where(o => o.State != "Issued" && o.State != "Revoked" && o.State != "Failed" && db.Jobs.Any(j => j.ResourceId == o.Id && j.State == "Failed" && (j.Kind == "AcmeCreate" || j.Kind == "AcmeVerify"))).OrderBy(o => o.CreatedAt).Take(100).ToListAsync(ct);
        foreach (var order in exhausted) { order.State = "Failed"; order.Diagnostic = "The certificate operation exhausted its retries. Check your DNS and provider settings, then create a new request."; }
        var failed = await db.Orders.Where(o => o.State == "Failed" && !db.Notifications.Any(n => n.DeduplicationKey == "order-failed:" + o.Id.ToString())).OrderBy(o => o.CreatedAt).Take(100).ToListAsync(ct);
        foreach (var order in failed.Concat(exhausted).DistinctBy(o => o.Id))
        {
            var site = await db.Websites.SingleAsync(w => w.Id == order.WebsiteId, ct);
            await Notify(order.OrganizationId, "order-failed:" + order.Id, "Certificate request needs attention", $"A certificate request for {site.Hostname} failed. Check the request details and DNS settings in OpenCert. Any certificate already installed on your website has not been replaced by this request.", now, ct, email: !order.Staging);
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    public static int? ExpirationStage(DateTimeOffset expires, DateTimeOffset now)
    {
        var remaining = expires - now;
        foreach (var days in new[] { 0, 1, 7, 14, 30 }) if (remaining <= TimeSpan.FromDays(days)) return days;
        return null;
    }

    private async Task Notify(Guid organization, string key, string subject, string message, DateTimeOffset now, CancellationToken ct, bool email = true)
    {
        if (db.Notifications.Local.Any(n => n.DeduplicationKey == key) || await db.Notifications.AnyAsync(n => n.DeduplicationKey == key, ct)) return;
        db.Notifications.Add(new Notification { OrganizationId = organization, DeduplicationKey = key, Message = message, CreatedAt = now });
        if (!email) return;
        var recipients = await db.Users.Where(u => u.CertificateEmails && u.EmailConfirmed && u.Email != null && db.Memberships.Any(m => m.OrganizationId == organization && m.UserId == u.Id)).Select(u => new { u.Id, u.Email }).ToListAsync(ct);
        foreach (var recipient in recipients)
        {
            // Operational email is capped per recipient; mandatory security mail uses its own path.
            var queued = await db.OutgoingEmails.CountAsync(m => m.RecipientUserId == recipient.Id && m.CreatedAt > now.AddDays(-1), ct) + db.OutgoingEmails.Local.Count(m => m.RecipientUserId == recipient.Id);
            if (queued >= 20) continue;
            outbox.Enqueue(recipient.Email!, subject, message, key + ":" + recipient.Id, now.AddDays(1), organization, recipient.Id);
        }
    }
}
