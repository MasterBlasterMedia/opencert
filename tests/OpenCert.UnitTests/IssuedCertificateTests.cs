using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using OpenCert.Infrastructure;
using Xunit;
namespace OpenCert.UnitTests;
public sealed class IssuedCertificateTests
{
    private static CertificateRequest Request(ECDsa key, string name)
    {
        var request = new CertificateRequest("CN=" + name, key, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder(); san.AddDnsName(name); request.CertificateExtensions.Add(san.Build()); return request;
    }
    [Fact]
    public void IssuedCertificateMustMatchCsrKeyAndNames()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = Request(key, "example.com"); var csr = request.CreateSigningRequestPem();
        using var valid = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        CsrPolicy.ValidateIssued(valid.ExportCertificatePem(), csr, ["example.com"]);
        using var wrongKey = Request(other, "example.com").CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var wrongName = Request(key, "other.example.com").CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        Assert.Throws<ArgumentException>(() => CsrPolicy.ValidateIssued(wrongKey.ExportCertificatePem(), csr, ["example.com"]));
        Assert.Throws<ArgumentException>(() => CsrPolicy.ValidateIssued(wrongName.ExportCertificatePem(), csr, ["example.com"]));
    }
}
