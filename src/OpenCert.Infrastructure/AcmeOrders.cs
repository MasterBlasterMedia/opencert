using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Certes;
using Certes.Acme.Resource;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
namespace OpenCert.Infrastructure;

public sealed class AcmeOrders(AppDb db, IDataProtectionProvider protection, IConfiguration config, IHostEnvironment environment, IHttpClientFactory httpClients)
{
    public string DirectoryUrl => config["Acme:Directory"] ?? "https://acme-staging-v02.api.letsencrypt.org/directory";
    public bool IsStaging => DirectoryUrl != "https://acme-v02.api.letsencrypt.org/directory";
    private void ValidateDirectory(string directory)
    {
        if (!environment.IsDevelopment() && directory is not ("https://acme-staging-v02.api.letsencrypt.org/directory" or "https://acme-v02.api.letsencrypt.org/directory")) throw new InvalidOperationException("Unsupported ACME directory.");
    }
    internal AcmeContext RenewalContext(CertificateOrder order)
    {
        ValidateDirectory(order.Directory);
        // ARI is a public GET. It must not depend on access to an account signing key.
        var directory = new Uri(order.Directory);
        return new AcmeContext(directory, http: new Certes.Acme.AcmeHttpClient(directory, httpClients.CreateClient("Acme")));
    }
    private async Task<AcmeContext> Context(CertificateOrder order)
    {
        await db.RequireLeaseAsync();
        var directory = order.Directory;
        var protector = protection.CreateProtector("OpenCert.AcmeAccounts.v1");
        ValidateDirectory(directory);
        // Persist the account key before making an external request so crash recovery reuses the identity.
        var account = await db.AcmeAccounts.SingleOrDefaultAsync(a => a.OrganizationId == order.OrganizationId && a.Directory == directory);
        if (account is null)
        {
            account = new AcmeAccount { OrganizationId = order.OrganizationId, Directory = directory, KeyProtected = protector.Protect(KeyFactory.NewKey(KeyAlgorithm.ES256).ToPem()) };
            db.AcmeAccounts.Add(account); await db.SaveChangesAsync();
        }
        var key = KeyFactory.FromPem(protector.Unprotect(account.KeyProtected));
        var acme = new AcmeContext(new Uri(directory), key, new Certes.Acme.AcmeHttpClient(new Uri(directory), httpClients.CreateClient("Acme")));
        await acme.NewAccount(config["Acme:ContactEmail"] ?? throw new InvalidOperationException("Configure Acme__ContactEmail before requesting a certificate."), true);
        return acme;
    }
    public async Task Create(Guid id)
    {
        var order = await db.Orders.SingleAsync(o => o.Id == id);
        if (order.State is "Issued" or "Revoked" or "Failed") return;
        var acme = await Context(order); var names = JsonSerializer.Deserialize<string[]>(order.HostnamesJson)!;
        var remote = order.AcmeUrl is null ? await acme.NewOrder(names) : acme.Order(new Uri(order.AcmeUrl));
        if (order.AcmeUrl is null) { order.AcmeUrl = remote.Location.ToString(); await db.SaveChangesAsync(); }
        var challenges = new List<object>();
        foreach (var authorization in await remote.Authorizations())
        {
            var resource = await authorization.Resource();
            if (resource.Status == AuthorizationStatus.Valid) continue;
            var challenge = await authorization.Dns();
            challenges.Add(new { hostname = resource.Identifier.Value, name = "_acme-challenge." + resource.Identifier.Value, type = "TXT", value = acme.AccountKey.DnsTxt(challenge.Token) });
        }
        order.ChallengesJson = JsonSerializer.Serialize(challenges); order.State = "AwaitingVerification"; order.Diagnostic = "Add these DNS records, then select Verify ownership. Keep all values when names share a record.";
        await db.SaveChangesAsync();
    }
    public async Task<TimeSpan?> VerifyAndIssue(Guid id)
    {
        var order = await db.Orders.SingleAsync(o => o.Id == id);
        if (order.State is "Issued" or "Revoked" or "Failed") return null;
        if (order.CreatedAt < DateTimeOffset.UtcNow.AddDays(-7))
        {
            order.State = "Failed"; order.Diagnostic = "This certificate request expired. Create a new request."; await db.SaveChangesAsync(); return null;
        }
        var retrySeconds = 5;
        var acme = await Context(order); var remote = acme.Order(new Uri(order.AcmeUrl ?? throw new InvalidOperationException("The order is still being prepared.")));
        foreach (var authorization in await remote.Authorizations())
        {
            var state = await authorization.Resource();
            if (state.Status == AuthorizationStatus.Valid) continue;
            if (state.Status != AuthorizationStatus.Pending) { order.State = "Failed"; order.Diagnostic = "The certificate authority rejected or expired domain verification. Check the record and create a new request."; await db.SaveChangesAsync(); return null; }
            retrySeconds = Math.Max(retrySeconds, authorization.RetryAfter);
            var challenge = await authorization.Dns();
            if ((await challenge.Resource()).Status == ChallengeStatus.Pending) await challenge.Validate();
            retrySeconds = Math.Max(retrySeconds, challenge.RetryAfter);
        }
        var resource = await remote.Resource();
        if (resource.Status == OrderStatus.Invalid) { order.State = "Failed"; order.Diagnostic = "The certificate authority rejected this order. Create a new request after checking DNS."; await db.SaveChangesAsync(); return null; }
        if (resource.Status == OrderStatus.Ready)
        {
            var csr = CsrPolicy.Validate(order.CsrPem, JsonSerializer.Deserialize<string[]>(order.HostnamesJson)!);
            await remote.Finalize(csr); resource = await remote.Resource();
        }
        if (resource.Status != OrderStatus.Valid)
        {
            order.State = "Verifying"; order.Diagnostic = "The certificate authority is checking your request. OpenCert will check again automatically."; await db.SaveChangesAsync();
            return TimeSpan.FromSeconds(Math.Max(retrySeconds, remote.RetryAfter));
        }
        var chain = await remote.Download(); var pem = chain.ToPem();
        CsrPolicy.ValidateIssued(pem, order.CsrPem, JsonSerializer.Deserialize<string[]>(order.HostnamesJson)!);
        order.CertificatePem = pem;
        using var certificate = X509Certificate2.CreateFromPem(order.CertificatePem);
        order.ExpiresAt = new DateTimeOffset(certificate.NotAfter.ToUniversalTime()); order.State = "Issued";
        order.RenewalDueAt = OpenCert.Domain.RenewalPolicy.Due(new DateTimeOffset(certificate.NotBefore.ToUniversalTime()), order.ExpiresAt.Value);
        order.NextRenewalInfoAt = DateTimeOffset.UtcNow;
        db.Jobs.Add(new Job { Kind = "AcmeRenewalInfo", ResourceId = order.Id, DeduplicationKey = "ari:" + order.Id });
        order.Diagnostic = order.Staging ? "Test certificate issued. Browsers do not trust staging certificates. Install only in your test environment." : "Certificate issued. Install it on your server, then check live HTTPS. Renewal and installation remain manual.";
        db.Notifications.Add(new() { OrganizationId = order.OrganizationId, Message = $"Certificate issued for {string.Join(", ", JsonSerializer.Deserialize<string[]>(order.HostnamesJson)!)}. Installation is still required." });
        await db.SaveChangesAsync(); return null;
    }
    public async Task Revoke(Guid id)
    {
        var order = await db.Orders.SingleAsync(o => o.Id == id);
        if (order.State == "Revoked") return;
        if (order.CertificatePem is null) throw new InvalidOperationException("Only an issued certificate can be revoked.");
        var acme = await Context(order); using var certificate = X509Certificate2.CreateFromPem(order.CertificatePem);
        await acme.RevokeCertificate(certificate.RawData, RevocationReason.Unspecified, null);
        order.State = "Revoked"; order.Diagnostic = "The CA accepted revocation. Replace this certificate wherever it is installed."; await db.SaveChangesAsync();
    }
}
