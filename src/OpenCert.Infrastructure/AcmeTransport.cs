using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
namespace OpenCert.Infrastructure;
public static class AcmeTransport
{
    public static HttpClientHandler Create(IConfiguration config, IHostEnvironment environment)
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        if (config["Acme:DevelopmentRootCertificate"] is not { Length: > 0 } rootFile) return handler;
        var directory = new Uri(config["Acme:Directory"] ?? "https://acme-staging-v02.api.letsencrypt.org/directory");
        if (!environment.IsDevelopment() || !directory.IsLoopback || directory.Scheme != "https") throw new InvalidOperationException("A development ACME root is permitted only for a loopback HTTPS test CA in Development.");
        // Trust exactly the local test CA without disabling name, validity or signature validation.
        handler.ServerCertificateCustomValidationCallback = (_, cert, serverChain, errors) =>
        {
            if (cert is null || (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != 0) return false;
            using var root = X509Certificate2.CreateFromPem(File.ReadAllText(rootFile)); using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust; chain.ChainPolicy.CustomTrustStore.Add(root);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            if (serverChain is not null) foreach (var element in serverChain.ChainElements.Cast<X509ChainElement>().Skip(1)) chain.ChainPolicy.ExtraStore.Add(element.Certificate);
            return chain.Build(cert);
        };
        return handler;
    }
}
