using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenCert.Application;
using OpenCert.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders(); builder.Logging.AddJsonConsole();
builder.Services.AddOpenCert(builder.Configuration, builder.Environment); builder.Services.AddHostedService<Worker>();
await builder.Build().RunAsync();

public sealed class Worker(IServiceScopeFactory scopes, ILogger<Worker> logger) : BackgroundService
{
    private readonly Guid workerId = Guid.NewGuid();
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDb>(); var queue = scope.ServiceProvider.GetRequiredService<JobQueue>();
                var now = DateTimeOffset.UtcNow;
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Heartbeats\" (\"Id\", \"At\") VALUES ('worker', {now}) ON CONFLICT (\"Id\") DO UPDATE SET \"At\" = {now}", stoppingToken);
                var job = await queue.Lease(workerId, stoppingToken);
                if (job is null) { await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken); continue; }
                try
                {
                    switch (job.Kind)
                    {
                        case "Inspect":
                            var website = await db.Websites.FindAsync([job.ResourceId], stoppingToken);
                            if (website is null) break;
                            var discovery = await scope.ServiceProvider.GetRequiredService<IHostingDiscovery>().Discover(website.Hostname, stoppingToken);
                            var https = await scope.ServiceProvider.GetRequiredService<IHttpsInspector>().Inspect(website.Hostname, stoppingToken);
                            website.HostingProvider = discovery.HostingProvider; website.DnsProvider = discovery.DnsProvider; website.DiscoveryJson = JsonSerializer.Serialize(discovery);
                            website.HttpsTrusted = https.Trusted; website.ExpiresAt = https.ExpiresAt; website.Issuer = https.Issuer; website.Fingerprint = https.Fingerprint; website.Diagnostic = https.Message; website.CheckedAt = DateTimeOffset.UtcNow;
                            await db.SaveChangesAsync(stoppingToken); break;
                        case "EmailSend": await scope.ServiceProvider.GetRequiredService<EmailOutbox>().Deliver(job.ResourceId, stoppingToken); break;
                        case "AcmeCreate": await scope.ServiceProvider.GetRequiredService<AcmeOrders>().Create(job.ResourceId); break;
                        case "AcmeVerify": await scope.ServiceProvider.GetRequiredService<AcmeOrders>().VerifyAndIssue(job.ResourceId); break;
                        case "AcmeRevoke": await scope.ServiceProvider.GetRequiredService<AcmeOrders>().Revoke(job.ResourceId); break;
                        default: throw new InvalidOperationException("Unsupported job type.");
                    }
                    await queue.Complete(job, workerId, stoppingToken);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    logger.LogWarning("Job {JobId} ({Kind}) failed: {ErrorType}", job.Id, job.Kind, e.GetType().Name);
                    await queue.Fail(job, workerId, "The operation failed. Check the provider configuration and retry after the waiting period.", stoppingToken);
                    if (job.Kind.StartsWith("Acme", StringComparison.Ordinal))
                    {
                        var order = await db.Orders.FindAsync([job.ResourceId], stoppingToken);
                        if (order is not null) { order.Diagnostic = "The certificate operation failed. Check the CA contact and DNS settings. The worker retries temporary failures up to five times."; if (job.Attempts >= 5) order.State = "Failed"; await db.SaveChangesAsync(stoppingToken); }
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogError("Worker cycle failed: {ErrorType}", e.GetType().Name); await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
        }
    }
}
