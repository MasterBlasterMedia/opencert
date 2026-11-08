using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCert.Infrastructure;
using Xunit;
namespace OpenCert.PersistenceTests;
public sealed class TestDatabase : IAsyncLifetime
{
    public ServiceProvider Services { get; private set; } = null!;
    public async Task InitializeAsync()
    {
        var connection = Environment.GetEnvironmentVariable("OPENCERT_PERSISTENCE_DATABASE") ?? throw new InvalidOperationException("Set OPENCERT_PERSISTENCE_DATABASE to an isolated test database.");
        var name = new Npgsql.NpgsqlConnectionStringBuilder(connection).Database;
        if (name is null || !(name.EndsWith("_tests", StringComparison.Ordinal) || name.EndsWith("_ci", StringComparison.Ordinal))) throw new InvalidOperationException("Persistence tests require a database name ending in _tests or _ci.");
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddDbContext<AppDb>(o => o.UseNpgsql(connection));
        services.AddIdentityCore<User>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version3).AddEntityFrameworkStores<AppDb>();
        services.AddScoped<JobQueue>(); Services = services.BuildServiceProvider();
        using var scope = Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDb>(); await db.Database.MigrateAsync();
    }
    public async Task DisposeAsync() => await Services.DisposeAsync();
}
public sealed class JobLeaseTests(TestDatabase fixture) : IClassFixture<TestDatabase>
{
    [Fact]
    public async Task ConcurrentConsumersClaimEachJobOnce()
    {
        using var setup = fixture.Services.CreateScope(); var db = setup.ServiceProvider.GetRequiredService<AppDb>();
        var jobs = Enumerable.Range(0, 3).Select(_ => new Job { Kind = "LeaseTest", DeduplicationKey = "lease-test:" + Guid.NewGuid(), DueAt = DateTimeOffset.UtcNow.AddYears(-10) }).ToArray();
        db.Jobs.AddRange(jobs); await db.SaveChangesAsync();
        try
        {
            var claims = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ => { using var scope = fixture.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<JobQueue>().Lease(Guid.NewGuid(), default); }));
            Assert.Equal(3, claims.Count(x => x is not null)); Assert.Equal(3, claims.Where(x => x is not null).Select(x => x!.Id).Distinct().Count());
        }
        finally { var ids = jobs.Select(j => j.Id).ToArray(); await db.Jobs.Where(j => ids.Contains(j.Id)).ExecuteDeleteAsync(); }
    }
    [Fact]
    public async Task RepeatedWorkerCrashesHaveABoundedRetryCount()
    {
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var job = new Job { Kind = "LeaseTest", DeduplicationKey = "lease-test:" + Guid.NewGuid(), State = "Running", Attempts = 5, LeaseOwner = Guid.NewGuid(), LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(-1) };
        db.Jobs.Add(job); await db.SaveChangesAsync();
        try { Assert.Null(await scope.ServiceProvider.GetRequiredService<JobQueue>().Lease(Guid.NewGuid(), default)); Assert.Equal("Failed", (await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == job.Id)).State); }
        finally { await db.Jobs.Where(j => j.Id == job.Id).ExecuteDeleteAsync(); }
    }
    [Fact]
    public async Task DeferredPollingPersistsItsDeadlineWithoutConsumingTheFailureBudget()
    {
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDb>(); var queue = scope.ServiceProvider.GetRequiredService<JobQueue>();
        var owner = Guid.NewGuid(); var job = new Job { Kind = "LeaseTest", DeduplicationKey = "lease-test:" + Guid.NewGuid(), State = "Running", Attempts = 3, LeaseOwner = owner, LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(1) };
        db.Jobs.Add(job); await db.SaveChangesAsync();
        try
        {
            await Assert.ThrowsAsync<LeaseLostException>(() => queue.Defer(job, Guid.NewGuid(), TimeSpan.FromMinutes(2), default));
            await queue.Defer(job, owner, TimeSpan.FromMinutes(2), default);
            var saved = await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == job.Id);
            Assert.Equal("Pending", saved.State); Assert.Equal(2, saved.Attempts); Assert.Null(saved.LeaseUntil); Assert.True(saved.DueAt > DateTimeOffset.UtcNow.AddSeconds(110));
            Assert.Null(await queue.Lease(Guid.NewGuid(), default));
        }
        finally { await db.Jobs.Where(j => j.Id == job.Id).ExecuteDeleteAsync(); }
    }
    [Fact]
    public async Task StolenLeasePreventsResourceWritesCompletionFailureAndRenewal()
    {
        using var firstScope = fixture.Services.CreateScope(); var first = firstScope.ServiceProvider.GetRequiredService<AppDb>(); var queue = firstScope.ServiceProvider.GetRequiredService<JobQueue>();
        var owner = Guid.NewGuid(); var nextOwner = Guid.NewGuid(); var org = new Organization();
        var site = new Website { OrganizationId = org.Id, Hostname = "lease.example.com", Diagnostic = "original" };
        var job = new Job { Kind = "LeaseTest", ResourceId = site.Id, DeduplicationKey = "lease-test:" + Guid.NewGuid(), State = "Running", LeaseOwner = owner, LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(1) };
        first.Organizations.Add(org); first.Websites.Add(site); first.Jobs.Add(job); await first.SaveChangesAsync(); first.ActiveLease = new(job.Id, owner);
        try
        {
            using var secondScope = fixture.Services.CreateScope(); var second = secondScope.ServiceProvider.GetRequiredService<AppDb>(); var otherQueue = secondScope.ServiceProvider.GetRequiredService<JobQueue>();
            await second.Jobs.Where(j => j.Id == job.Id).ExecuteUpdateAsync(s => s.SetProperty(j => j.LeaseUntil, DateTimeOffset.UtcNow.AddSeconds(-1)));
            var stolen = await otherQueue.Lease(nextOwner, default); Assert.Equal(job.Id, stolen?.Id);
            site.Diagnostic = "stale result";
            await Assert.ThrowsAsync<LeaseLostException>(() => first.SaveChangesAsync());
            await Assert.ThrowsAsync<LeaseLostException>(() => queue.Complete(job, owner, default));
            Assert.False(await queue.Renew(job.Id, owner, default)); await queue.Fail(job, owner, "stale failure", default);
            Assert.Equal("original", (await second.Websites.AsNoTracking().SingleAsync(w => w.Id == site.Id)).Diagnostic);
            Assert.Equal("Running", (await second.Jobs.AsNoTracking().SingleAsync(j => j.Id == job.Id)).State);
            second.ActiveLease = new(job.Id, nextOwner); var current = await second.Websites.SingleAsync(w => w.Id == site.Id); current.Diagnostic = "current result"; await second.SaveChangesAsync(); await otherQueue.Complete(stolen!, nextOwner, default);
            Assert.Equal("current result", (await second.Websites.AsNoTracking().SingleAsync(w => w.Id == site.Id)).Diagnostic);
        }
        finally { first.ActiveLease = null; await first.Jobs.Where(j => j.Id == job.Id).ExecuteDeleteAsync(); await first.Organizations.Where(o => o.Id == org.Id).ExecuteDeleteAsync(); }
    }
}
