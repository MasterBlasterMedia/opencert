using System.Net;
using System.Net.Http.Headers;
using OpenCert.Domain;
using OpenCert.Infrastructure;
using Xunit;
namespace OpenCert.UnitTests;

public sealed class RenewalInformationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);
    [Fact]
    public void SuggestedTimeUsesTheWindowAndNeverReachesExpiration()
    {
        Assert.Equal(Now.AddHours(3), RenewalPolicy.Suggested(Now.AddHours(2), Now.AddHours(6), Now.AddDays(1), Now, .25));
        Assert.Equal(Now, RenewalPolicy.Suggested(Now.AddHours(-6), Now.AddHours(-2), Now.AddDays(1), Now, .5));
        Assert.Equal(Now.AddHours(3), RenewalPolicy.Suggested(Now.AddHours(2), Now.AddHours(10), Now.AddHours(4), Now, .5));
        Assert.Equal(Now, RenewalPolicy.Suggested(Now, Now.AddTicks(1), Now.AddTicks(1), Now, .9999999999999999));
    }
    [Theory]
    [InlineData(2, 2, 24, .5)]
    [InlineData(3, 2, 24, .5)]
    [InlineData(24, 25, 24, .5)]
    [InlineData(-5, -1, 0, .5)]
    [InlineData(1, 2, 24, -1)]
    [InlineData(1, 2, 24, 1)]
    [InlineData(1, 2, 24, double.NaN)]
    public void InvalidWindowOrSampleIsRejected(int start, int end, int expires, double sample) =>
        Assert.Throws<ArgumentException>(() => RenewalPolicy.Suggested(Now.AddHours(start), Now.AddHours(end), Now.AddHours(expires), Now, sample));
    [Theory]
    [InlineData(null, 21600)]
    [InlineData(-10, 60)]
    [InlineData(30, 60)]
    [InlineData(3600, 3600)]
    [InlineData(172800, 86400)]
    public void RenewalPollingUsesTheRfcBoundedInterval(int? seconds, int expected) =>
        Assert.Equal(TimeSpan.FromSeconds(expected), RenewalPolicy.InformationPollDelay(seconds is { } value ? TimeSpan.FromSeconds(value) : null));

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    public async Task ProviderRetryAfterBecomesADeadline(int status)
    {
        var before = DateTimeOffset.UtcNow;
        using var client = Client(status, new RetryConditionHeaderValue(TimeSpan.FromHours(3)));
        var error = await Assert.ThrowsAsync<AcmeBackoffException>(() => client.GetAsync("https://acme.example.com/order"));
        Assert.InRange(error.RetryAt, before.AddHours(3), DateTimeOffset.UtcNow.AddHours(3));
    }
    [Fact]
    public async Task RetryAfterDateAndMissingHeaderHaveSafeDefaults()
    {
        var future = DateTimeOffset.UtcNow.AddDays(2);
        using var dated = Client(429, new RetryConditionHeaderValue(future));
        Assert.Equal(future, (await Assert.ThrowsAsync<AcmeBackoffException>(() => dated.GetAsync("https://acme.example.com/order"))).RetryAt);
        var before = DateTimeOffset.UtcNow;
        using var missing = Client(503, null);
        Assert.InRange((await Assert.ThrowsAsync<AcmeBackoffException>(() => missing.GetAsync("https://acme.example.com/order"))).RetryAt, before.AddMinutes(1), DateTimeOffset.UtcNow.AddMinutes(1));
    }
    [Theory]
    [InlineData(200)]
    [InlineData(400)] // Certes must retain control of badNonce retries.
    [InlineData(403)]
    public async Task OtherResponsesReachTheAcmeClientUnchanged(int status)
    {
        using var client = Client(status, null); using var response = await client.GetAsync("https://acme.example.com/order");
        Assert.Equal(status, (int)response.StatusCode);
    }
    private static HttpClient Client(int status, RetryConditionHeaderValue? retry) =>
        new(new AcmeBackoffHandler { InnerHandler = new Reply(status, retry) });
    private sealed class Reply(int status, RetryConditionHeaderValue? retry) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = new HttpResponseMessage((HttpStatusCode)status); response.Headers.RetryAfter = retry;
            return Task.FromResult(response);
        }
    }
}
