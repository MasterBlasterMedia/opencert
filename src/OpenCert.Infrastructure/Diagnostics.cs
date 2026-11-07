using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DnsClient;
using OpenCert.Application;
using OpenCert.Domain;

namespace OpenCert.Infrastructure;

public sealed class HttpsInspector : IHttpsInspector
{
    public async Task<HttpsObservation> Inspect(string hostname, CancellationToken cancellationToken)
    {
        hostname = DomainName.Normalize(hostname);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(hostname, deadline.Token);
            if (addresses.Length == 0 || addresses.Any(ip => !PublicNetwork.IsPublic(ip))) return new(null, null, null, null, "We cannot check addresses on private or reserved networks.");
            using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            // Connect to the checked IP, never resolve again during the connection.
            await socket.ConnectAsync(new IPEndPoint(addresses[0], 443), deadline.Token);
            using var stream = new NetworkStream(socket, ownsSocket: false);
            using var tls = new SslStream(stream, leaveInnerStreamOpen: false);
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = hostname, CertificateRevocationCheckMode = X509RevocationMode.Online }, deadline.Token);
            using var cert = new X509Certificate2(tls.RemoteCertificate!);
            return new(true, new DateTimeOffset(cert.NotAfter.ToUniversalTime()), cert.Issuer, cert.GetCertHashString(HashAlgorithmName.SHA256), "HTTPS is working with a trusted certificate for this website. Installation and renewal ownership have not been confirmed.");
        }
        catch (System.Security.Authentication.AuthenticationException) { return new(false, null, null, null, "The website's certificate could not be trusted for this name. Check your host's HTTPS settings."); }
        catch (Exception e) when (e is SocketException or IOException or OperationCanceledException or CryptographicException)
        { return new(null, null, null, null, "We couldn't complete the HTTPS check. Confirm your website is online, then try again."); }
    }
}
public sealed class HostingDiscovery : IHostingDiscovery
{
    private readonly LookupClient dns = new(new LookupClientOptions { Timeout = TimeSpan.FromSeconds(3), Retries = 1, UseCache = false });
    public async Task<HostingObservation> Discover(string hostname, CancellationToken cancellationToken)
    {
        hostname = DomainName.Normalize(hostname);
        try
        {
            var cnames = (await dns.QueryAsync(hostname, QueryType.CNAME, cancellationToken: cancellationToken)).Answers.CnameRecords().Select(r => r.CanonicalName.Value.TrimEnd('.')).ToArray();
            var names = (await dns.QueryAsync(hostname, QueryType.NS, cancellationToken: cancellationToken)).Answers.NsRecords().Select(r => r.NSDName.Value.TrimEnd('.')).ToArray();
            var caa = (await dns.QueryAsync(hostname, QueryType.CAA, cancellationToken: cancellationToken)).Answers.CaaRecords().Select(r => $"{r.Tag} {r.Value}").ToArray();
            return new(Detect(cnames), Detect(names), cnames, names, caa, "Provider suggestions are based on public DNS and may be incomplete. Your registrar, DNS provider and web host can be different companies.");
        }
        catch (Exception e) when (e is DnsResponseException or OperationCanceledException or SocketException)
        { return new(null, null, [], [], [], "We couldn't identify your provider. You can choose your host or follow the guided help."); }
    }
    public static string? Detect(IEnumerable<string> values)
    {
        var suffixes = new Dictionary<string, string> { ["onrender.com"] = "Render", ["vercel-dns.com"] = "Vercel", ["netlify.app"] = "Netlify", ["ns.cloudflare.com"] = "Cloudflare", ["domaincontrol.com"] = "GoDaddy DNS", ["amazonaws.com"] = "AWS" };
        foreach (var value in values) foreach (var pair in suffixes) if (value.Equals(pair.Key, StringComparison.OrdinalIgnoreCase) || value.EndsWith("." + pair.Key, StringComparison.OrdinalIgnoreCase)) return pair.Value;
        return null;
    }
}
