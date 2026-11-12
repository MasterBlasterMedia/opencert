using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using OpenCert.Infrastructure;
using Xunit;
namespace OpenCert.PersistenceTests;

public sealed class RenewalInformationTests(TestDatabase fixture) : IClassFixture<TestDatabase>
{
    [Fact]
    public async Task PreviouslyIssuedCertificatesReceiveExactlyOneDurableGuidanceJob()
    {
        using var setup = fixture.Services.CreateScope(); var db = setup.ServiceProvider.GetRequiredService<AppDb>();
        var (org, order) = await CreateOrder(db); var now = DateTimeOffset.UtcNow;
        try
        {
            async Task Tick() { using var scope = fixture.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<OperationalMaintenance>().Run(now, default); }
            await Task.WhenAll(Tick(), Tick()); await Tick();
            var job = Assert.Single(await db.Jobs.Where(j => j.ResourceId == order.Id).ToListAsync());
            Assert.Equal("AcmeRenewalInfo", job.Kind); Assert.Equal("Pending", job.State);
            Assert.NotNull((await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id)).NextRenewalInfoAt);
        }
        finally { await db.Jobs.Where(j => j.ResourceId == order.Id).ExecuteDeleteAsync(); await db.Organizations.Where(o => o.Id == org.Id).ExecuteDeleteAsync(); }
    }

    [Fact]
    public async Task GuidancePersistsAcrossScopesAndChangedWindowsMoveTheReminder()
    {
        using var setup = fixture.Services.CreateScope(); var db = setup.ServiceProvider.GetRequiredService<AppDb>();
        var (org, order) = await CreateOrder(db); using var server = new RenewalServer();
        try
        {
            var first = await Poll(order.Id, server);
            Assert.Equal("Ari", first.RenewalSource); Assert.Equal("Issued", first.State);
            Assert.InRange(first.RenewalDueAt!.Value, server.Start, server.End);
            Assert.InRange(first.NextRenewalInfoAt!.Value - first.RenewalInfoCheckedAt!.Value, TimeSpan.FromMinutes(59), TimeSpan.FromMinutes(61));
            var again = await Poll(order.Id, server); Assert.Equal(first.RenewalDueAt, again.RenewalDueAt);
            server.Start = DateTimeOffset.UtcNow.AddHours(-2); server.End = server.Start.AddHours(1);
            var urgent = await Poll(order.Id, server); Assert.True(urgent.RenewalDueAt < DateTimeOffset.UtcNow.AddSeconds(1));
            Assert.NotEqual(first.RenewalDueAt, urgent.RenewalDueAt);
            Assert.False(await db.AcmeAccounts.AnyAsync(a => a.OrganizationId == org.Id));
            Assert.All(server.Requests, request => { Assert.Equal(HttpMethod.Get, request.Method); Assert.Equal("acme.example.test", request.Url.Host); });
            Assert.Contains(server.Requests, r => r.Url.AbsolutePath.StartsWith("/renewal-info/", StringComparison.Ordinal));
        }
        finally { await db.Organizations.Where(o => o.Id == org.Id).ExecuteDeleteAsync(); }
    }

    [Fact]
    public async Task BadGuidanceAndRateLimitsKeepIssuedCertificatesAndSavedReminders()
    {
        using var setup = fixture.Services.CreateScope(); var db = setup.ServiceProvider.GetRequiredService<AppDb>();
        var (org, order) = await CreateOrder(db); using var server = new RenewalServer();
        try
        {
            var good = await Poll(order.Id, server);
            server.Body = "{not valid JSON";
            var malformed = await Poll(order.Id, server);
            Assert.Equal(good.RenewalDueAt, malformed.RenewalDueAt); Assert.Equal("Issued", malformed.State); Assert.Equal(1, malformed.RenewalInfoFailures); Assert.NotNull(malformed.RenewalInfoError);
            server.Body = null; server.End = server.Start;
            var invalid = await Poll(order.Id, server); Assert.Equal(good.RenewalDueAt, invalid.RenewalDueAt); Assert.Equal(2, invalid.RenewalInfoFailures);
            server.Status = HttpStatusCode.TooManyRequests; server.RetryAfter = TimeSpan.FromDays(2);
            var before = DateTimeOffset.UtcNow;
            var limited = await Poll(order.Id, server); Assert.Equal("Issued", limited.State); Assert.Equal(good.RenewalDueAt, limited.RenewalDueAt);
            Assert.True(limited.NextRenewalInfoAt >= before.AddDays(2));
            server.Status = HttpStatusCode.OK; server.End = server.Start.AddDays(1); server.RetryAfter = TimeSpan.FromHours(1);
            var recovered = await Poll(order.Id, server); Assert.Null(recovered.RenewalInfoError); Assert.Equal(0, recovered.RenewalInfoFailures);
        }
        finally { await db.Organizations.Where(o => o.Id == org.Id).ExecuteDeleteAsync(); }
    }

    [Fact]
    public async Task UnsupportedCaUsesValidityAndExpiredOrRevokedCertificatesMakeNoRequests()
    {
        using var setup = fixture.Services.CreateScope(); var db = setup.ServiceProvider.GetRequiredService<AppDb>();
        var (org, order) = await CreateOrder(db); using var server = new RenewalServer { Supported = false };
        try
        {
            var unsupported = await Poll(order.Id, server);
            Assert.Null(unsupported.NextRenewalInfoAt); Assert.Equal("Validity", unsupported.RenewalSource); Assert.NotNull(unsupported.RenewalDueAt); Assert.NotNull(unsupported.RenewalInfoError);
            var requests = server.Requests.Count;
            order.State = "Revoked"; await db.SaveChangesAsync();
            Assert.Null((await Poll(order.Id, server)).NextRenewalInfoAt); Assert.Equal(requests, server.Requests.Count);
            order.State = "Issued"; order.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1); await db.SaveChangesAsync();
            Assert.Null((await Poll(order.Id, server)).NextRenewalInfoAt); Assert.Equal(requests, server.Requests.Count);
        }
        finally { await db.Organizations.Where(o => o.Id == org.Id).ExecuteDeleteAsync(); }
    }

    private async Task<CertificateOrder> Poll(Guid id, RenewalServer server)
    {
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        // Deliberately differs from the persisted order: old certificates must keep using their issuing CA.
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Acme:Directory"] = "https://other.example.test/directory" }).Build();
        var orders = new AcmeOrders(db, scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>(), config, new DevelopmentEnvironment(), server);
        await new AcmeRenewalInformation(db, orders).Poll(id, default);
        return await db.Orders.AsNoTracking().SingleAsync(o => o.Id == id);
    }
    private static async Task<(Organization, CertificateOrder)> CreateOrder(AppDb db)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=ari.example.com", key, HashAlgorithmName.SHA256);
        var ski = new X509SubjectKeyIdentifierExtension(request.PublicKey, false);
        request.CertificateExtensions.Add(ski);
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromSubjectKeyIdentifier(ski));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var org = new Organization(); var site = new Website { OrganizationId = org.Id, Hostname = "ari.example.com", NextCheckAt = DateTimeOffset.UtcNow.AddDays(1) };
        var order = new CertificateOrder { OrganizationId = org.Id, WebsiteId = site.Id, State = "Issued", CertificatePem = certificate.ExportCertificatePem(), ExpiresAt = new DateTimeOffset(certificate.NotAfter.ToUniversalTime()), Directory = "https://acme.example.test/directory" };
        db.Organizations.Add(org); db.Websites.Add(site); db.Orders.Add(order); await db.SaveChangesAsync(); return (org, order);
    }
    private sealed class DevelopmentEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "OpenCert.Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    private sealed class RenewalServer : HttpMessageHandler, IHttpClientFactory
    {
        public DateTimeOffset Start { get; set; } = DateTimeOffset.UtcNow.AddDays(2);
        public DateTimeOffset End { get; set; } = DateTimeOffset.UtcNow.AddDays(3);
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public TimeSpan RetryAfter { get; set; } = TimeSpan.FromHours(1);
        public bool Supported { get; set; } = true;
        public string? Body { get; set; }
        public List<(HttpMethod Method, Uri Url)> Requests { get; } = [];
        public HttpClient CreateClient(string name) => new(new AcmeBackoffHandler { InnerHandler = this }, disposeHandler: false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.Method, request.RequestUri!));
            var directory = request.RequestUri!.AbsolutePath == "/directory";
            var body = directory ? Supported ? "{\"renewalInfo\":\"https://acme.example.test/renewal-info\"}" : "{}" : Body ?? JsonSerializer.Serialize(new { suggestedWindow = new { start = Start, end = End } });
            var response = new HttpResponseMessage(directory ? HttpStatusCode.OK : Status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(RetryAfter); return Task.FromResult(response);
        }
    }
}
