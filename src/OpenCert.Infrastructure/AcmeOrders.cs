using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Certes;
using Certes.Acme.Resource;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
namespace OpenCert.Infrastructure;

public sealed class AcmeOrders(AppDb db, IDataProtectionProvider protection, IConfiguration config, IHostEnvironment environment)
{
    public string DirectoryUrl => config["Acme:Directory"] ?? "https://acme-staging-v02.api.letsencrypt.org/directory";
    public bool IsStaging => DirectoryUrl != "https://acme-v02.api.letsencrypt.org/directory";
    private async Task<AcmeContext> Context(CertificateOrder order)
    {
        var protector = protection.CreateProtector("OpenCert.AcmeAccounts.v1");
        if (!environment.IsDevelopment() && DirectoryUrl is not ("https://acme-staging-v02.api.letsencrypt.org/directory" or "https://acme-v02.api.letsencrypt.org/directory")) throw new InvalidOperationException("Unsupported ACME directory.");
        // Persist the account key before making an external request so crash recovery reuses the identity.
        var account = await db.AcmeAccounts.SingleOrDefaultAsync(a => a.OrganizationId == order.OrganizationId && a.Directory == DirectoryUrl);
        if (account is null)
        {
            account = new AcmeAccount { OrganizationId = order.OrganizationId, Directory = DirectoryUrl, KeyProtected = protector.Protect(KeyFactory.NewKey(KeyAlgorithm.ES256).ToPem()) };
            db.AcmeAccounts.Add(account); await db.SaveChangesAsync();
        }
        if (order.Directory != DirectoryUrl) throw new InvalidOperationException("The order's CA directory differs from the current configuration.");
        var key = KeyFactory.FromPem(protector.Unprotect(account.KeyProtected));
        var acme = new AcmeContext(new Uri(DirectoryUrl), key);
        await acme.NewAccount(config["Acme:ContactEmail"] ?? throw new InvalidOperationException("Configure Acme__ContactEmail before requesting a certificate."), true);
        return acme;
    }
    public async Task Create(Guid id)
    {
        var order = await db.Orders.SingleAsync(o => o.Id == id);
        if (order.AcmeUrl is not null) return;
        var acme = await Context(order); var names = JsonSerializer.Deserialize<string[]>(order.HostnamesJson)!;
        var remote = await acme.NewOrder(names); order.AcmeUrl = remote.Location.ToString();
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
    public async Task VerifyAndIssue(Guid id)
    {
        var order = await db.Orders.SingleAsync(o => o.Id == id);
        if (order.State is "Issued" or "Revoked") return;
        var acme = await Context(order); var remote = acme.Order(new Uri(order.AcmeUrl ?? throw new InvalidOperationException("The order is still being prepared.")));
        foreach (var authorization in await remote.Authorizations())
        {
            var state = await authorization.Resource();
            if (state.Status == AuthorizationStatus.Valid) continue;
            if (state.Status == AuthorizationStatus.Invalid) { order.State = "Failed"; order.Diagnostic = "The certificate authority rejected domain verification. Check the record and create a new request."; await db.SaveChangesAsync(); return; }
            var challenge = await authorization.Dns();
            if ((await challenge.Resource()).Status == ChallengeStatus.Pending) await challenge.Validate();
        }
        var resource = await remote.Resource();
        if (resource.Status == OrderStatus.Invalid) { order.State = "Failed"; order.Diagnostic = "The certificate authority rejected this order. Create a new request after checking DNS."; await db.SaveChangesAsync(); return; }
        if (resource.Status == OrderStatus.Ready)
        {
            var csr = CsrPolicy.Validate(order.CsrPem, JsonSerializer.Deserialize<string[]>(order.HostnamesJson)!);
            await remote.Finalize(csr); resource = await remote.Resource();
        }
        if (resource.Status != OrderStatus.Valid)
        {
            order.State = "Verifying"; order.Diagnostic = "The certificate authority is checking your request. Check again shortly."; await db.SaveChangesAsync(); return;
        }
        var chain = await remote.Download(); order.CertificatePem = chain.ToPem();
        using var certificate = X509Certificate2.CreateFromPem(order.CertificatePem);
        order.ExpiresAt = new DateTimeOffset(certificate.NotAfter.ToUniversalTime()); order.State = "Issued";
        order.Diagnostic = order.Staging ? "Test certificate issued. Browsers do not trust staging certificates. Install only in your test environment." : "Certificate issued. Install it on your server, then check live HTTPS. Renewal and installation remain manual.";
        db.Notifications.Add(new() { OrganizationId = order.OrganizationId, Message = $"Certificate issued for {string.Join(", ", JsonSerializer.Deserialize<string[]>(order.HostnamesJson)!)}. Installation is still required." });
        await db.SaveChangesAsync();
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
