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
        var names = args.Skip(2).Select(x => DomainName.Normalize(x, true)).Distinct().ToArray(); var directory = Path.GetFullPath(args[1]); EnsureNoLinks(directory);
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
        using var cert = X509Certificate2.CreateFromPem(File.ReadAllText(args[1])); Console.WriteLine($"Subject: {cert.Subject}\nIssuer: {cert.Issuer}\nExpires (UTC): {cert.NotAfter.ToUniversalTime():O}\nSHA256: {cert.GetCertHashString(HashAlgorithmName.SHA256)}");
    }
    else
    {
        if (args.Length != 3) throw new ArgumentException("Specify the key directory and issued certificate.");
        var directory = Path.GetFullPath(args[1]); EnsureNoLinks(directory); var keyPath = Path.Combine(directory, "private-key.pem"); EnsureNoLinks(keyPath);
        if (!Directory.Exists(directory) || new DirectoryInfo(directory).LinkTarget is not null || !File.Exists(keyPath) || new FileInfo(keyPath).LinkTarget is not null) throw new ArgumentException("Use a real directory containing your local private key, without symbolic links.");
        const UnixFileMode sharedPermissions = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
        if (!OperatingSystem.IsWindows() && ((File.GetUnixFileMode(directory) | File.GetUnixFileMode(keyPath)) & sharedPermissions) != 0) throw new ArgumentException("Restrict the key directory to mode 700 and private key to mode 600 before installation.");
        var pem = File.ReadAllText(args[2]); if (pem.Contains("PRIVATE KEY", StringComparison.Ordinal)) throw new ArgumentException("The certificate file must contain only public certificates.");
        using var cert = X509Certificate2.CreateFromPem(pem, File.ReadAllText(keyPath));
        if (cert.NotAfter.ToUniversalTime() <= DateTime.UtcNow || cert.NotBefore.ToUniversalTime() > DateTime.UtcNow) throw new ArgumentException("The certificate is outside its validity period.");
        var target = Path.Combine(directory, "fullchain.pem"); EnsureNoLinks(target);
        var backup = Path.Combine(directory, "previous-fullchain.pem"); EnsureNoLinks(backup);
        var temporary = Path.Combine(directory, Guid.NewGuid() + ".tmp"); WriteNew(temporary, pem);
        var backupTemporary = Path.Combine(directory, Guid.NewGuid() + ".tmp");
        try
        {
            if (File.Exists(target)) { WriteNew(backupTemporary, File.ReadAllText(target)); File.Move(backupTemporary, backup, overwrite: true); }
            File.Move(temporary, target, overwrite: true);
        }
        finally { File.Delete(temporary); File.Delete(backupTemporary); }
        Console.WriteLine("Certificate and local key match. Certificate saved atomically. Configure/reload your web server locally, then verify public HTTPS in OpenCert. Automatic renewal is not configured by this command.");
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

static void EnsureNoLinks(string path)
{
    for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        if (new FileInfo(current).LinkTarget is not null || new DirectoryInfo(current).LinkTarget is not null) throw new ArgumentException("Symbolic links and reparse points are not allowed in key or certificate paths.");
}
