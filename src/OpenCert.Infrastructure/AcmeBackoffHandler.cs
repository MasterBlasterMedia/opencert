using System.Net;
namespace OpenCert.Infrastructure;

public sealed class AcmeBackoffException(DateTimeOffset retryAt) : Exception("The certificate authority requested a retry delay.")
{
    public DateTimeOffset RetryAt { get; } = retryAt;
}
public sealed class AcmeBackoffHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = await base.SendAsync(request, ct);
        if (response.StatusCode is not (HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)) return response;
        var now = DateTimeOffset.UtcNow;
        var header = response.Headers.RetryAfter;
        var delay = header?.Delta ?? TimeSpan.FromMinutes(1);
        var retryAt = header?.Date ?? (delay >= DateTimeOffset.MaxValue - now ? DateTimeOffset.MaxValue : now + delay);
        response.Dispose();
        throw new AcmeBackoffException(retryAt > now ? retryAt : now.AddSeconds(1));
    }
}
