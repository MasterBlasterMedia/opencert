using Microsoft.EntityFrameworkCore;
using OpenCert.Domain;
namespace OpenCert.Infrastructure;

public sealed class JobQueue(AppDb db)
{
    public async Task<Job?> Lease(Guid worker, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow; var until = now.AddMinutes(5);
        var rows = await db.Jobs.FromSqlInterpolated($"""
            WITH candidate AS (
              SELECT "Id" FROM "Jobs" WHERE ("State" = 'Pending' AND "DueAt" <= {now})
                 OR ("State" = 'Running' AND "LeaseUntil" < {now})
              ORDER BY "DueAt" FOR UPDATE SKIP LOCKED LIMIT 1
            )
            UPDATE "Jobs" SET "State" = 'Running', "LeaseOwner" = {worker}, "LeaseUntil" = {until}, "Attempts" = "Attempts" + 1
            FROM candidate WHERE "Jobs"."Id" = candidate."Id" RETURNING "Jobs".*
            """).AsNoTracking().ToListAsync(ct);
        return rows.SingleOrDefault();
    }
    public async Task Complete(Job job, Guid worker, CancellationToken ct) => await db.Jobs.Where(x => x.Id == job.Id && x.LeaseOwner == worker && x.LeaseUntil > DateTimeOffset.UtcNow && x.State == "Running").ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "Complete").SetProperty(x => x.CompletedAt, DateTimeOffset.UtcNow).SetProperty(x => x.LeaseUntil, (DateTimeOffset?)null), ct);
    public async Task Fail(Job job, Guid worker, string message, CancellationToken ct)
    {
        var state = job.Attempts >= 5 ? "Failed" : "Pending"; var due = DateTimeOffset.UtcNow + RenewalPolicy.RetryDelay(job.Attempts);
        await db.Jobs.Where(x => x.Id == job.Id && x.LeaseOwner == worker && x.LeaseUntil > DateTimeOffset.UtcNow && x.State == "Running").ExecuteUpdateAsync(s => s.SetProperty(x => x.State, state).SetProperty(x => x.Error, message).SetProperty(x => x.DueAt, due).SetProperty(x => x.LeaseUntil, (DateTimeOffset?)null), ct);
    }
}
