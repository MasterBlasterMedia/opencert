using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
namespace OpenCert.Infrastructure;

public sealed record WebIngress(Uri Origin, bool Development, ForwardedHeadersOptions Forwarding)
{
    public static WebIngress Create(IConfiguration config, IHostEnvironment environment)
    {
        var development = environment.IsDevelopment();
        var value = config["Web:Origin"] ?? (development ? "http://localhost:5173" : null);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var origin) || origin.Scheme is not ("http" or "https") || origin.UserInfo.Length != 0 || origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0 || (!development && origin.Scheme != "https"))
            throw new InvalidOperationException("Web__Origin must be one absolute browser origin, using HTTPS outside Development.");
        if (string.Equals(config["ASPNETCORE_FORWARDEDHEADERS_ENABLED"], "true", StringComparison.OrdinalIgnoreCase) || string.Equals(config["FORWARDEDHEADERS_ENABLED"], "true", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Use explicit Security__TrustedProxies or Security__TrustedProxyNetworks instead of unrestricted forwarded headers.");
        var options = new ForwardedHeadersOptions { ForwardLimit = 1 };
        // Empty trust lists mean trust everyone in the framework. Keep forwarding disabled unless explicit entries exist.
        options.KnownProxies.Clear(); options.KnownIPNetworks.Clear();
        foreach (var entry in Entries(config["Security:TrustedProxies"]))
        {
            if (!IPAddress.TryParse(entry, out var ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) throw new InvalidOperationException("Trusted proxies must be explicit IP addresses.");
            options.KnownProxies.Add(ip);
        }
        foreach (var entry in Entries(config["Security:TrustedProxyNetworks"]))
        {
            if (!System.Net.IPNetwork.TryParse(entry, out var network) || network.PrefixLength == 0) throw new InvalidOperationException("Trusted proxy networks must be explicit CIDRs, never an all-addresses network.");
            options.KnownIPNetworks.Add(network);
        }
        if (options.KnownProxies.Count + options.KnownIPNetworks.Count > 0)
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        if (config["Security:ForwardLimit"] is { } limit)
        {
            if (!int.TryParse(limit, out var count) || count is < 1 or > 5) throw new InvalidOperationException("Security__ForwardLimit must be between 1 and 5.");
            options.ForwardLimit = count;
        }
        return new(origin, development, options);
    }

    public async Task Enforce(HttpContext context, RequestDelegate next)
    {
        // Hosts and callback authorities always come from the configured origin; X-Forwarded-Host is deliberately unused.
        if (!string.Equals(context.Request.Host.Host, Origin.IdnHost, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest; return;
        }
        var health = context.Request.Path is { Value: "/health/live" or "/health/ready" } && HttpMethods.IsGet(context.Request.Method);
        if (!Development && !health && (!context.Request.IsHttps || (context.Request.Host.Port ?? 443) != Origin.Port))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { title = "HTTPS through the configured public origin is required." }); return;
        }
        await next(context);
    }

    private static IEnumerable<string> Entries(string? value) => (value ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
