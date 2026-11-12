using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Certes;
using Certes.Acme;
using Microsoft.EntityFrameworkCore;
using OpenCert.Domain;
namespace OpenCert.Infrastructure;

public sealed class AcmeRenewalInformation(AppDb db, AcmeOrders orders)
{
    public async Task<TimeSpan?> Poll(Guid id, CancellationToken ct)
    {
        var order = await db.Orders.SingleAsync(o => o.Id == id, ct);
        var now = DateTimeOffset.UtcNow;
        if (order.State != "Issued" || order.CertificatePem is null || order.ExpiresAt is not { } expires || expires <= now)
        { order.NextRenewalInfoAt = null; await db.SaveChangesAsync(ct); return null; }
        using var certificate = X509Certificate2.CreateFromPem(order.CertificatePem);
        order.RenewalDueAt ??= RenewalPolicy.Due(new DateTimeOffset(certificate.NotBefore.ToUniversalTime()), new DateTimeOffset(certificate.NotAfter.ToUniversalTime()));
        try
        {
            var context = orders.RenewalContext(order);
            var info = await context.GetRenewalInfo(new CertificateChain(order.CertificatePem).GetRenewalInfoCertificateId()).WaitAsync(ct);
            var window = info.SuggestedWindow;
            // Keep the chosen instant for an unchanged window instead of moving the reminder on each poll.
            var suggested = RenewalPolicy.Suggested(window.Start, window.End, expires, now, Random.Shared.NextDouble());
            if (order.RenewalSource != "Ari" || !SameDatabaseInstant(order.RenewalWindowStart, window.Start) || !SameDatabaseInstant(order.RenewalWindowEnd, window.End)) order.RenewalDueAt = suggested;
            order.RenewalWindowStart = window.Start; order.RenewalWindowEnd = window.End; order.RenewalSource = "Ari";
            order.RenewalInfoFailures = 0; order.RenewalInfoError = null;
            order.NextRenewalInfoAt = now + RenewalPolicy.InformationPollDelay(info.RetryAfter);
        }
        catch (NotSupportedException)
        {
            order.NextRenewalInfoAt = null; order.RenewalInfoError = "This CA does not offer renewal guidance. The saved renewal reminder remains active.";
        }
        catch (Exception error) when (!ct.IsCancellationRequested && error is AcmeException or HttpRequestException or TaskCanceledException or CryptographicException or JsonException or AcmeBackoffException or ArgumentException)
        {
            order.RenewalInfoFailures = Math.Min(12, order.RenewalInfoFailures + 1);
            order.RenewalInfoError = "CA renewal guidance is temporarily unavailable. The last saved renewal reminder remains active.";
            var delay = TimeSpan.FromHours(Math.Min(24, 6 * Math.Pow(2, order.RenewalInfoFailures - 1)));
            order.NextRenewalInfoAt = error is AcmeBackoffException backoff && backoff.RetryAt > now + delay ? backoff.RetryAt : now + delay;
        }
        order.RenewalInfoCheckedAt = now; await db.SaveChangesAsync(ct);
        return order.NextRenewalInfoAt is { } next ? next - now : null;
    }
    // PostgreSQL stores microseconds; sub-microsecond JSON timestamps must not trigger rescheduling on every poll.
    private static bool SameDatabaseInstant(DateTimeOffset? saved, DateTimeOffset received) => saved?.UtcTicks / 10 == received.UtcTicks / 10;
}
