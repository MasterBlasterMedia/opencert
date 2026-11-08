using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OpenCert.Application;
namespace OpenCert.Infrastructure;

public sealed class EmailOutbox(AppDb db, IDataProtectionProvider protection, IEmailDelivery delivery)
{
    private readonly IDataProtector protector = protection.CreateProtector("OpenCert.EmailOutbox.v1");
    private sealed record Payload(string To, string Subject, string Body);
    // Caller saves the message and business operation in the same database transaction.
    public void Enqueue(string to, string subject, string body, string key, DateTimeOffset? expiresAt = null, Guid? organizationId = null, string? recipientUserId = null)
    {
        var message = new OutgoingEmail { PayloadProtected = protector.Protect(JsonSerializer.Serialize(new Payload(to, subject, body))), DeduplicationKey = key, ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddDays(1), OrganizationId = organizationId, RecipientUserId = recipientUserId };
        db.OutgoingEmails.Add(message);
        db.Jobs.Add(new Job { Kind = "EmailSend", ResourceId = message.Id, DeduplicationKey = "email:" + key });
    }
    public async Task Deliver(Guid id, CancellationToken ct)
    {
        var message = await db.OutgoingEmails.FindAsync([id], ct);
        if (message is null || message.SentAt is not null || message.PayloadProtected.Length == 0) return;
        if (message.ExpiresAt <= DateTimeOffset.UtcNow) { message.PayloadProtected = ""; await db.SaveChangesAsync(ct); return; }
        var payload = JsonSerializer.Deserialize<Payload>(protector.Unprotect(message.PayloadProtected))!;
        if (message.RecipientUserId is { } userId && message.OrganizationId is { } organizationId)
        {
            // Re-check preferences and membership at delivery, not just when the alert was queued.
            if (!await db.Users.AnyAsync(u => u.Id == userId && u.CertificateEmails && u.EmailConfirmed && u.Email == payload.To, ct) || !await db.Memberships.AnyAsync(m => m.UserId == userId && m.OrganizationId == organizationId, ct))
            { message.PayloadProtected = ""; await db.SaveChangesAsync(ct); return; }
        }
        await db.RequireLeaseAsync(ct);
        await delivery.Send(payload.To, payload.Subject, payload.Body, message.DeduplicationKey, ct);
        message.SentAt = DateTimeOffset.UtcNow; message.PayloadProtected = ""; await db.SaveChangesAsync(ct);
    }
}
