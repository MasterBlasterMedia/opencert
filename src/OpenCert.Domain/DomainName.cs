using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace OpenCert.Domain;

public static class DomainName
{
    public static string Normalize(string input, bool allowWildcard = false)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 1024) throw new ArgumentException("Enter a website address, such as example.com.");
        var value = input.Trim();
        if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) value = value[8..];
        else if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) value = value[7..];
        value = value.TrimEnd('/').TrimEnd('.');
        var wildcard = value.StartsWith("*.", StringComparison.Ordinal);
        if (wildcard) { if (!allowWildcard) throw new ArgumentException("Add the main website first. Wildcards are available in certificate settings."); value = value[2..]; }
        if (value.IndexOfAny(['/', '\\', ':', '@', '?', '#', '%', '*']) >= 0) throw new ArgumentException("Enter only the website name, without a port, path or sign-in details.");
        try { value = new IdnMapping { UseStd3AsciiRules = true }.GetAscii(value).ToLowerInvariant(); }
        catch (ArgumentException) { throw new ArgumentException("This website name is not valid."); }
        var labels = value.Split('.');
        if (value.Length > 253 || labels.Length < 2 || IPAddress.TryParse(value, out _) || labels.Any(l => l.Length is < 1 or > 63 || l[0] == '-' || l[^1] == '-' || l.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) || labels[^1].All(char.IsDigit))
            throw new ArgumentException("Enter a full public website name, such as example.com.");
        if (new[] { "localhost", "local", "internal", "test", "invalid", "onion", "home", "arpa" }.Contains(labels[^1])) throw new ArgumentException("Only public website names are supported.");
        return wildcard ? "*." + value : value;
    }
}

public static class PublicNetwork
{
    // An allow-oriented policy. IPv6 transitions and special-purpose networks are deliberately excluded.
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) return IsPublic(address.MapToIPv4());
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return !(bytes[0] is 0 or 10 or 127 || bytes[0] >= 224 ||
                bytes[0] == 100 && bytes[1] is >= 64 and <= 127 ||
                bytes[0] == 169 && bytes[1] == 254 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
                bytes[0] == 192 && (bytes[1] is 0 or 168 || bytes[1] == 88 && bytes[2] == 99) ||
                bytes[0] == 198 && (bytes[1] is 18 or 19 || bytes[1] == 51 && bytes[2] == 100) ||
                bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113);
        }
        return address.AddressFamily == AddressFamily.InterNetworkV6 && (bytes[0] & 0xe0) == 0x20 &&
            !(bytes[0] == 0x20 && bytes[1] == 0x01 && (bytes[2] < 0x02 || bytes[2] == 0x0d && bytes[3] == 0xb8)) &&
            !(bytes[0] == 0x20 && bytes[1] == 0x02) &&
            !(bytes[0] == 0x3f && bytes[1] == 0xff);
    }
}

public static class RenewalPolicy
{
    public static DateTimeOffset Due(DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        if (notAfter <= notBefore) throw new ArgumentException("Certificate validity is invalid.");
        return notBefore + (notAfter - notBefore) * (2d / 3d);
    }
    public static TimeSpan RetryDelay(int attempts) => TimeSpan.FromSeconds(Math.Min(21600, 30 * Math.Pow(2, Math.Clamp(attempts, 0, 16))));
}

public static class WebsiteStatus
{
    public static string Describe(bool? trusted, DateTimeOffset? expires, DateTimeOffset? checkedAt, DateTimeOffset now)
    {
        if (checkedAt is null) return "Setup Required";
        if (trusted is null || checkedAt < now.AddHours(-24)) return "Unable to Verify";
        if (expires <= now) return "Certificate Expired";
        if (trusted != true) return "Not Protected";
        if (expires < now.AddDays(14)) return "Expiring Soon";
        return "Protected";
    }
}
