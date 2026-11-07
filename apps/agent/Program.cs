using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using OpenCert.Domain;

if (args.Length < 1 || args[0] is not ("csr" or "inspect" or "install"))
{
    Console.WriteLine("OpenCert Agent\n  csr <new-directory> <hostname> [additional-hostname ...]\n  inspect <certificate.pem>\n  install <existing-key-directory> <certificate.pem>\nKeys stay on this machine. Commands do not contact the control plane."); return;
}
try
{
    if (args[0] == "csr")
    {
        if (args.Length < 3) throw new ArgumentException("Specify a new directory and website names.");
        var names = args.Skip(2).Select(x => DomainName.Normalize(x, true)).Distinct().ToArray(); var directory = Path.GetFullPath(args[1]);
        if (Directory.Exists(directory) || File.Exists(directory)) throw new ArgumentException("Choose a new directory to avoid replacing an existing key.");
        Directory.CreateDirectory(directory); if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); var request = new CertificateRequest(new X500DistinguishedName("CN=" + names[0]), key, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder(); foreach (var name in names) san.AddDnsName(name); request.CertificateExtensions.Add(san.Build());
        WriteNew(Path.Combine(directory, "private-key.pem"), key.ExportPkcs8PrivateKeyPem()); WriteNew(Path.Combine(directory, "request.pem"), request.CreateSigningRequestPem());
        Console.WriteLine($"CSR saved to {Path.Combine(directory, "request.pem")}. Upload only request.pem; keep private-key.pem secret.");
    }
    else if (args[0] == "inspect")
    {
        if (args.Length != 2) throw new ArgumentException("Specify a certificate file.");
        using var cert = X509Certificate2.CreateFromPemFile(args[1]); Console.WriteLine($"Subject: {cert.Subject}\nIssuer: {cert.Issuer}\nExpires (UTC): {cert.NotAfter.ToUniversalTime():O}\nSHA256: {cert.GetCertHashString(HashAlgorithmName.SHA256)}");
    }
    else
    {
        if (args.Length != 3) throw new ArgumentException("Specify the key directory and issued certificate.");
        var directory = Path.GetFullPath(args[1]); var keyPath = Path.Combine(directory, "private-key.pem");
        if (!Directory.Exists(directory) || new DirectoryInfo(directory).LinkTarget is not null || !File.Exists(keyPath) || new FileInfo(keyPath).LinkTarget is not null) throw new ArgumentException("Use a real directory containing your local private key, without symbolic links.");
        var pem = File.ReadAllText(args[2]); using var cert = X509Certificate2.CreateFromPem(pem, File.ReadAllText(keyPath));
        if (cert.NotAfter.ToUniversalTime() <= DateTime.UtcNow || cert.NotBefore.ToUniversalTime() > DateTime.UtcNow) throw new ArgumentException("The certificate is outside its validity period.");
        var target = Path.Combine(directory, "fullchain.pem"); if (File.Exists(target) && new FileInfo(target).LinkTarget is not null) throw new ArgumentException("The target must not be a symbolic link.");
        var temporary = Path.Combine(directory, Guid.NewGuid() + ".tmp"); WriteNew(temporary, pem);
        if (File.Exists(target)) File.Copy(target, Path.Combine(directory, "previous-fullchain.pem"), overwrite: true);
        File.Move(temporary, target, overwrite: true); Console.WriteLine("Certificate and local key match. Certificate saved atomically. Configure/reload your web server locally, then verify public HTTPS in OpenCert. Automatic renewal is not configured by this command.");
    }
}
catch (Exception e) when (e is ArgumentException or IOException or CryptographicException or UnauthorizedAccessException) { Console.Error.WriteLine(e.Message); Environment.ExitCode = 1; }
static void WriteNew(string path, string text)
{
    var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
    if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    using var stream = new FileStream(path, options);
    using var writer = new StreamWriter(stream); writer.Write(text);
}
