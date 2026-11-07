using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using OpenCert.Domain;
using OpenCert.Infrastructure;
using Xunit;
namespace OpenCert.UnitTests;

public sealed class SecurityPolicyTests
{
    [Theory]
    [InlineData(" HTTPS://Example.COM/ ", "example.com")]
    [InlineData("bücher.de", "xn--bcher-kva.de")]
    [InlineData("Example.com.", "example.com")]
    public void NormalizesPublicHostnames(string input, string expected) => Assert.Equal(expected, DomainName.Normalize(input));
    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("https://example.com:443")]
    [InlineData("example.com/path")]
    [InlineData("user@example.com")]
    [InlineData("example.com?x=1")]
    [InlineData("a..com")]
    [InlineData("-a.com")]
    [InlineData("a-.com")]
    [InlineData("*.example.com")]
    [InlineData("metadata.internal")]
    [InlineData("https://example.com#fragment")]
    [InlineData("[::1]")]
    [InlineData("example.com\\@127.0.0.1")]
    [InlineData("2130706433")]
    [InlineData("a.123")]
    public void RejectsUnsafeHostnames(string input) => Assert.Throws<ArgumentException>(() => DomainName.Normalize(input));
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.100.100.200")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.0.1")]
    [InlineData("198.18.0.1")]
    [InlineData("192.0.2.1")]
    [InlineData("203.0.113.1")]
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("fc00::1")]
    [InlineData("fe80::1")]
    [InlineData("2001:db8::1")]
    [InlineData("2002:7f00:1::")]
    [InlineData("64:ff9b::a00:1")]
    [InlineData("2001::1")]
    public void RejectsNonpublicAddresses(string ip) => Assert.False(PublicNetwork.IsPublic(IPAddress.Parse(ip)));
    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("8.8.8.8")]
    [InlineData("2606:4700:4700::1111")]
    public void AcceptsPublicAddresses(string ip) => Assert.True(PublicNetwork.IsPublic(IPAddress.Parse(ip)));
    [Fact] public void RenewalUsesActualLifetime() { var start = DateTimeOffset.UtcNow; Assert.Equal(start.AddDays(20), RenewalPolicy.Due(start, start.AddDays(30))); Assert.Equal(start.AddHours(16), RenewalPolicy.Due(start, start.AddHours(24))); }
    [Fact] public void RetryIsBounded() => Assert.Equal(TimeSpan.FromHours(6), RenewalPolicy.RetryDelay(100));
    [Fact] public void StaleObservationCannotRemainProtected() => Assert.Equal("Unable to Verify", WebsiteStatus.Describe(true, DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow));
    [Fact] public void UncheckedWebsiteIsNotProtected() => Assert.Equal("Setup Required", WebsiteStatus.Describe(null, null, null, DateTimeOffset.UtcNow));
    [Fact] public void UntrustedCertificateIsNotProtected() => Assert.Equal("Not Protected", WebsiteStatus.Describe(false, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    [Fact]
    public void CertificateRequestRequiresMatchingSignedSan()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); var request = new CertificateRequest("CN=example.com", key, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder(); san.AddDnsName("example.com"); request.CertificateExtensions.Add(san.Build()); var pem = request.CreateSigningRequestPem();
        Assert.NotEmpty(CsrPolicy.Validate(pem, ["example.com"])); Assert.Throws<ArgumentException>(() => CsrPolicy.Validate(pem, ["victim.com"]));
        Assert.Throws<ArgumentException>(() => CsrPolicy.Validate(key.ExportPkcs8PrivateKeyPem(), ["example.com"]));
    }
    [Fact] public void ProviderInferenceRequiresSuffixBoundary() { Assert.Null(HostingDiscovery.Detect(["evil-onrender.com.attacker.com"])); Assert.Equal("Render", HostingDiscovery.Detect(["website.onrender.com"])); }
}
