using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCert.Infrastructure;
using Xunit;
namespace OpenCert.PersistenceTests;

public sealed class MaintenanceTests(TestDatabase fixture) : IClassFixture<TestDatabase>
{
    [Fact]
    public async Task SchedulingAndAlertsSurviveRepeatedAndConcurrentProducerRuns()
    {
        using var setup = fixture.Services.CreateScope(); var db = setup.ServiceProvider.GetRequiredService<AppDb>();
        var now = DateTimeOffset.UtcNow; var org = new Organization();
        var subscribed = new User { Email = "subscribed@example.com", EmailConfirmed = true };
        var optedOut = new User { Email = "opted-out@example.com", EmailConfirmed = true, CertificateEmails = false };
        var site = new Website { OrganizationId = org.Id, Hostname = "reminder.example.com", ExpiresAt = now.AddDays(6), Fingerprint = "test-fingerprint", CertificateObservedAt = now.AddDays(-1), NextCheckAt = now.AddMinutes(-1) };
        db.Organizations.Add(org); db.Users.AddRange(subscribed, optedOut); db.Websites.Add(site);
        db.Memberships.AddRange(new Membership { OrganizationId = org.Id, UserId = subscribed.Id }, new Membership { OrganizationId = org.Id, UserId = optedOut.Id, Role = "Member" });
        await db.SaveChangesAsync();
        try
        {
            async Task Tick() { using var scope = fixture.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<OperationalMaintenance>().Run(now, default); }
            await Task.WhenAll(Tick(), Tick()); await Tick();
            Assert.Single(await db.Jobs.Where(j => j.ResourceId == site.Id && j.Kind == "Inspect").ToListAsync());
            Assert.Single(await db.Notifications.Where(n => n.OrganizationId == org.Id).ToListAsync());
            var email = Assert.Single(await db.OutgoingEmails.Where(e => e.OrganizationId == org.Id).ToListAsync());
            Assert.Equal(subscribed.Id, email.RecipientUserId); Assert.DoesNotContain(subscribed.Email, email.PayloadProtected);
            Assert.True((await db.Websites.AsNoTracking().SingleAsync(w => w.Id == site.Id)).NextCheckAt > now.AddHours(5));
            // A preference change after enqueue still suppresses delivery.
            subscribed.CertificateEmails = false; await db.SaveChangesAsync();
            var sender = fixture.Services.GetRequiredService<NoExternalEmail>(); var before = sender.Sent;
            using var deliveryScope = fixture.Services.CreateScope(); await deliveryScope.ServiceProvider.GetRequiredService<EmailOutbox>().Deliver(email.Id, default);
            Assert.Equal(before, sender.Sent); Assert.Equal("", (await db.OutgoingEmails.AsNoTracking().SingleAsync(e => e.Id == email.Id)).PayloadProtected);
            using var later = fixture.Services.CreateScope(); await later.ServiceProvider.GetRequiredService<OperationalMaintenance>().Run(now.AddDays(7), default);
            Assert.Equal(2, await db.Notifications.CountAsync(n => n.OrganizationId == org.Id));
            Assert.Single(await db.Jobs.Where(j => j.ResourceId == site.Id && j.Kind == "Inspect").ToListAsync());
        }
        finally { await Cleanup(db, org.Id, site.Id, [subscribed.Id, optedOut.Id]); }
    }

    [Fact]
    public async Task RemovedMembersDoNotReceiveQueuedWorkspaceMail()
    {
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var org = new Organization(); var user = new User { Email = "former-member@example.com", EmailConfirmed = true };
        db.Organizations.Add(org); db.Users.Add(user); db.Memberships.Add(new() { OrganizationId = org.Id, UserId = user.Id }); await db.SaveChangesAsync();
        try
        {
            var key = "delivery-test:" + Guid.NewGuid(); var outbox = scope.ServiceProvider.GetRequiredService<EmailOutbox>();
            outbox.Enqueue(user.Email!, "Operational reminder", "Private workspace details", key, organizationId: org.Id, recipientUserId: user.Id); await db.SaveChangesAsync();
            var email = await db.OutgoingEmails.SingleAsync(e => e.DeduplicationKey == key);
            await db.Memberships.Where(m => m.OrganizationId == org.Id).ExecuteDeleteAsync();
            var sender = fixture.Services.GetRequiredService<NoExternalEmail>(); var before = sender.Sent; await outbox.Deliver(email.Id, default);
            Assert.Equal(before, sender.Sent); Assert.Equal("", email.PayloadProtected);
        }
        finally { await Cleanup(db, org.Id, Guid.Empty, [user.Id]); }
    }

    [Fact]
    public async Task OperationalEmailsAreCappedWithoutLosingWorkspaceNotifications()
    {
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDb>(); var now = DateTimeOffset.UtcNow;
        var org = new Organization(); var user = new User { Email = "many-sites@example.com", EmailConfirmed = true };
        var sites = Enumerable.Range(0, 25).Select(i => new Website { OrganizationId = org.Id, Hostname = $"site-{i}.example.com", ExpiresAt = now.AddHours(12), Fingerprint = "observed", NextCheckAt = now.AddMinutes(-1) }).ToArray();
        db.Organizations.Add(org); db.Users.Add(user); db.Memberships.Add(new() { OrganizationId = org.Id, UserId = user.Id }); db.Websites.AddRange(sites); await db.SaveChangesAsync();
        try
        {
            using var tick = fixture.Services.CreateScope(); await tick.ServiceProvider.GetRequiredService<OperationalMaintenance>().Run(now, default);
            Assert.Equal(25, await db.Notifications.CountAsync(n => n.OrganizationId == org.Id));
            Assert.Equal(20, await db.OutgoingEmails.CountAsync(e => e.RecipientUserId == user.Id));
        }
        finally
        {
            var ids = sites.Select(w => w.Id).ToArray(); await db.Jobs.Where(j => ids.Contains(j.ResourceId)).ExecuteDeleteAsync();
            await Cleanup(db, org.Id, Guid.Empty, [user.Id]);
        }
    }
    [Fact]
    public async Task RenewalAndExhaustedJobNoticesAreDurableAndDoNotClaimAutomaticInstallation()
    {
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDb>(); var now = DateTimeOffset.UtcNow;
        var org = new Organization(); var site = new Website { OrganizationId = org.Id, Hostname = "renewal.example.com", NextCheckAt = now.AddDays(1) };
        var issued = new CertificateOrder { OrganizationId = org.Id, WebsiteId = site.Id, State = "Issued", RenewalDueAt = now.AddSeconds(-1) };
        var failed = new CertificateOrder { OrganizationId = org.Id, WebsiteId = site.Id, State = "Verifying" };
        var job = new Job { Kind = "AcmeVerify", ResourceId = failed.Id, State = "Failed", Attempts = 5, DeduplicationKey = "maintenance-test:" + Guid.NewGuid() };
        db.Organizations.Add(org); db.Websites.Add(site); db.Orders.AddRange(issued, failed); db.Jobs.Add(job); await db.SaveChangesAsync();
        try
        {
            for (var i = 0; i < 2; i++) { using var tick = fixture.Services.CreateScope(); await tick.ServiceProvider.GetRequiredService<OperationalMaintenance>().Run(now, default); }
            var notices = await db.Notifications.Where(n => n.OrganizationId == org.Id).ToListAsync(); Assert.Equal(2, notices.Count);
            Assert.Contains(notices, n => n.Message.Contains("remain manual", StringComparison.Ordinal));
            Assert.Equal("Failed", (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == failed.Id)).State);
        }
        finally { await db.Jobs.Where(j => j.Id == job.Id).ExecuteDeleteAsync(); await Cleanup(db, org.Id, site.Id, []); }
    }

    private static async Task Cleanup(AppDb db, Guid org, Guid site, string[] users)
    {
        var mailIds = await db.OutgoingEmails.Where(e => e.OrganizationId == org).Select(e => e.Id).ToArrayAsync();
        await db.Jobs.Where(j => mailIds.Contains(j.ResourceId) || j.ResourceId == site).ExecuteDeleteAsync();
        await db.OutgoingEmails.Where(e => e.OrganizationId == org).ExecuteDeleteAsync();
        await db.Organizations.Where(o => o.Id == org).ExecuteDeleteAsync(); await db.Users.Where(u => users.Contains(u.Id)).ExecuteDeleteAsync();
    }
}
