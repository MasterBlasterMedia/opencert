using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using OpenCert.Domain;
namespace OpenCert.Infrastructure;

public static class CsrPolicy
{
    public static void ValidateIssued(string certificatePem, string csrPem, string[] names)
    {
        Validate(csrPem, names);
        var request = CertificateRequest.LoadSigningRequestPem(csrPem, HashAlgorithmName.SHA256, CertificateRequestLoadOptions.UnsafeLoadCertificateExtensions);
        using var certificate = X509Certificate2.CreateFromPem(certificatePem);
        if (!certificate.PublicKey.ExportSubjectPublicKeyInfo().SequenceEqual(request.PublicKey.ExportSubjectPublicKeyInfo())) throw new ArgumentException("The issued certificate does not match the submitted public key.");
        var sans = certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>().ToArray();
        if (sans.Length != 1 || sans[0].EnumerateIPAddresses().Any() || !sans[0].EnumerateDnsNames().Select(n => DomainName.Normalize(n, true)).Order().SequenceEqual(names.Select(n => DomainName.Normalize(n, true)).Order())) throw new ArgumentException("The issued certificate names do not match the request.");
        if (certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow || certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow.AddMinutes(5) || certificate.Extensions.OfType<X509BasicConstraintsExtension>().Any(x => x.CertificateAuthority)) throw new ArgumentException("The issued certificate has invalid validity or CA constraints.");
    }
    public static byte[] Validate(string pem, string[] names)
    {
        if (pem.Length > 24000 || pem.Contains("PRIVATE KEY", StringComparison.Ordinal)) throw new ArgumentException("Upload only a certificate request (CSR). Keep your private key on your server.");
        var expected = names.Select(n => DomainName.Normalize(n, true)).Distinct().Order().ToArray();
        if (expected.Length is < 1 or > 20 || expected.Length != names.Length) throw new ArgumentException("Use between 1 and 20 distinct website names.");
        CertificateRequest request;
        try { request = CertificateRequest.LoadSigningRequestPem(pem, HashAlgorithmName.SHA256, CertificateRequestLoadOptions.UnsafeLoadCertificateExtensions); }
        catch (CryptographicException) { throw new ArgumentException("The CSR signature or format is invalid."); }
        using var rsa = request.PublicKey.GetRSAPublicKey(); using var ec = request.PublicKey.GetECDsaPublicKey();
        if (!(rsa is { KeySize: >= 2048 and <= 8192 } || ec is { KeySize: 256 or 384 })) throw new ArgumentException("Use ECDSA P-256/P-384 or RSA 2048–8192.");
        var sans = request.CertificateExtensions.OfType<X509SubjectAlternativeNameExtension>().ToArray();
        if (sans.Length != 1 || sans[0].EnumerateIPAddresses().Any()) throw new ArgumentException("The CSR must contain DNS names in one Subject Alternative Name extension.");
        var actual = sans[0].EnumerateDnsNames().Select(n => DomainName.Normalize(n, true)).Order().ToArray();
        if (!actual.SequenceEqual(expected)) throw new ArgumentException("The CSR website names must exactly match this request.");
        foreach (var extension in request.CertificateExtensions)
            if (extension is X509BasicConstraintsExtension constraints && constraints.CertificateAuthority) throw new ArgumentException("CA certificate requests are not supported.");
        var fields = PemEncoding.Find(pem); return Convert.FromBase64String(pem[fields.Base64Data]);
    }
}
