namespace OpenCert.Application;

public record HttpsObservation(bool? Trusted, DateTimeOffset? ExpiresAt, string? Issuer, string? Fingerprint, string Message);
public record HostingObservation(string? HostingProvider, string? DnsProvider, string[] Cnames, string[] NameServers, string[] Caa, string Message);
public interface IHttpsInspector { Task<HttpsObservation> Inspect(string hostname, CancellationToken cancellationToken); }
public interface IHostingDiscovery { Task<HostingObservation> Discover(string hostname, CancellationToken cancellationToken); }
public interface IEmailDelivery { Task Send(string to, string subject, string text, string idempotencyKey, CancellationToken cancellationToken); }
