using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenCert.Infrastructure;
using Xunit;
namespace OpenCert.UnitTests;

public sealed class WebIngressTests
{
    private sealed class Environment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "OpenCert.Tests";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    private static WebIngress Settings(params (string Key, string? Value)[] entries)
    {
        var values = new Dictionary<string, string?> { ["Web:Origin"] = "https://opencert.example.com" };
        foreach (var (key, value) in entries) values[key] = value;
        return WebIngress.Create(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), new Environment());
    }
    private static async Task<HttpContext> Send(WebIngress settings, string remote, string host = "opencert.example.com", string forwarded = "203.0.113.25", string path = "/api/v1/auth/me", string scheme = "http")
    {
        var context = new DefaultHttpContext(); context.RequestServices = new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remote); context.Request.Host = new HostString(host);
        context.Request.Scheme = scheme; context.Request.Path = path; context.Request.Method = "GET";
        context.Request.Headers["X-Forwarded-For"] = forwarded; context.Request.Headers["X-Forwarded-Proto"] = "https";
        context.Request.Headers["X-Forwarded-Host"] = "attacker.example";
        context.Response.Body = new MemoryStream();
        var middleware = new ForwardedHeadersMiddleware(c => settings.Enforce(c, c => { c.Response.StatusCode = 204; return Task.CompletedTask; }), NullLoggerFactory.Instance, Options.Create(settings.Forwarding));
        await middleware.Invoke(context); return context;
    }
    [Fact]
    public async Task ExplicitProxyCanForwardHttpsAndClientAddressWithoutReplacingHost()
    {
        var context = await Send(Settings(("Security:TrustedProxies", "192.0.2.10")), "192.0.2.10");
        Assert.Equal(204, context.Response.StatusCode); Assert.True(context.Request.IsHttps);
        Assert.Equal("203.0.113.25", context.Connection.RemoteIpAddress!.ToString()); Assert.Equal("opencert.example.com", context.Request.Host.Host);
    }
    [Theory]
    [InlineData("192.0.2.99")]
    [InlineData("127.0.0.1")]
    public async Task UnknownProxiesCannotForgeClientAddressOrHttps(string remote)
    {
        var context = await Send(Settings(("Security:TrustedProxies", "192.0.2.10")), remote);
        Assert.Equal(400, context.Response.StatusCode); Assert.False(context.Request.IsHttps); Assert.Equal(remote, context.Connection.RemoteIpAddress!.ToString());
    }
    [Fact]
    public async Task EmptyTrustConfigurationNeverMeansTrustAll()
    {
        var context = await Send(Settings(), "192.0.2.10"); Assert.Equal(400, context.Response.StatusCode); Assert.False(context.Request.IsHttps);
    }
    [Theory]
    [InlineData("attacker.example")]
    [InlineData("opencert.example.com:8443")]
    public async Task UnexpectedHostOrPortIsRejectedEvenBehindTrustedProxy(string host)
    {
        Assert.Equal(400, (await Send(Settings(("Security:TrustedProxies", "192.0.2.10")), "192.0.2.10", host)).Response.StatusCode);
    }
    [Fact]
    public async Task ProxyChainIsConsumedFromTheTrustedEndWithinTheHopLimit()
    {
        var context = await Send(Settings(("Security:TrustedProxyNetworks", "192.0.2.0/24")), "192.0.2.10", forwarded: "198.51.100.9, 203.0.113.25");
        Assert.Equal("203.0.113.25", context.Connection.RemoteIpAddress!.ToString());
    }
    [Fact]
    public async Task DirectHttpsAndReadOnlyHealthProbesRemainAvailable()
    {
        Assert.Equal(204, (await Send(Settings(), "192.0.2.10", scheme: "https")).Response.StatusCode);
        Assert.Equal(204, (await Send(Settings(), "192.0.2.10", path: "/health/ready")).Response.StatusCode);
    }
    [Theory]
    [InlineData("Web:Origin", null)]
    [InlineData("Web:Origin", "http://opencert.example.com")]
    [InlineData("Web:Origin", "https://user@opencert.example.com")]
    [InlineData("Web:Origin", "https://opencert.example.com/path")]
    [InlineData("ASPNETCORE_FORWARDEDHEADERS_ENABLED", "true")]
    [InlineData("Security:TrustedProxyNetworks", "0.0.0.0/0")]
    [InlineData("Security:TrustedProxyNetworks", "::/0")]
    [InlineData("Security:ForwardLimit", "0")]
    public void UnsafeProductionConfigurationFailsAtStartup(string key, string? value) => Assert.Throws<InvalidOperationException>(() => Settings((key, value)));
}
