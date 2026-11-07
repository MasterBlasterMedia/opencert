using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using OpenCert.Application;
namespace OpenCert.Infrastructure;

public sealed class EmailOutbox(AppDb db, IDataProtectionProvider protection, IEmailDelivery delivery)
{
    private readonly IDataProtector protector = protection.CreateProtector("OpenCert.EmailOutbox.v1");
    private sealed record Payload(string To, string Subject, string Body);
    // Caller saves the message and business operation in the same database transaction.
    public void Enqueue(string to, string subject, string body, string key, DateTimeOffset? expiresAt = null)
    {
        var message = new OutgoingEmail { PayloadProtected = protector.Protect(JsonSerializer.Serialize(new Payload(to, subject, body))), DeduplicationKey = key, ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddDays(1) };
        db.OutgoingEmails.Add(message);
        db.Jobs.Add(new Job { Kind = "EmailSend", ResourceId = message.Id, DeduplicationKey = "email:" + key });
    }
    public async Task Deliver(Guid id, CancellationToken ct)
    {
        var message = await db.OutgoingEmails.FindAsync([id], ct);
        if (message is null || message.SentAt is not null) return;
        if (message.ExpiresAt <= DateTimeOffset.UtcNow) { message.PayloadProtected = ""; await db.SaveChangesAsync(ct); return; }
        var payload = JsonSerializer.Deserialize<Payload>(protector.Unprotect(message.PayloadProtected))!;
        await delivery.Send(payload.To, payload.Subject, payload.Body, message.DeduplicationKey, ct);
        message.SentAt = DateTimeOffset.UtcNow; message.PayloadProtected = ""; await db.SaveChangesAsync(ct);
    }
}
