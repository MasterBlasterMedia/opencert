using Microsoft.EntityFrameworkCore;
using OpenCert.Domain;
namespace OpenCert.Infrastructure;

public sealed class JobQueue(AppDb db)
{
    public async Task<Job?> Lease(Guid worker, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow; var until = now.AddMinutes(5);
        await db.Jobs.Where(x => x.State == "Running" && x.LeaseUntil < now && x.Attempts >= 5).ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "Failed").SetProperty(x => x.Error, "The operation exhausted its worker-recovery attempts."), ct);
        var rows = await db.Jobs.FromSqlInterpolated($"""
            WITH candidate AS (
              SELECT "Id" FROM "Jobs" WHERE "Attempts" < 5 AND (("State" = 'Pending' AND "DueAt" <= {now})
                 OR ("State" = 'Running' AND "LeaseUntil" < {now}))
              ORDER BY "DueAt" FOR UPDATE SKIP LOCKED LIMIT 1
            )
            UPDATE "Jobs" SET "State" = 'Running', "LeaseOwner" = {worker}, "LeaseUntil" = {until}, "Attempts" = "Attempts" + 1
            FROM candidate WHERE "Jobs"."Id" = candidate."Id" RETURNING "Jobs".*
            """).AsNoTracking().ToListAsync(ct);
        return rows.SingleOrDefault();
    }
    public async Task Complete(Job job, Guid worker, CancellationToken ct)
    {
        var changed = await db.Jobs.Where(x => x.Id == job.Id && x.LeaseOwner == worker && x.LeaseUntil > DateTimeOffset.UtcNow && x.State == "Running").ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "Complete").SetProperty(x => x.CompletedAt, DateTimeOffset.UtcNow).SetProperty(x => x.LeaseUntil, (DateTimeOffset?)null), ct);
        if (changed != 1) throw new LeaseLostException();
    }
    public async Task<bool> Renew(Guid jobId, Guid worker, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return await db.Jobs.Where(x => x.Id == jobId && x.LeaseOwner == worker && x.LeaseUntil > now && x.State == "Running").ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseUntil, now.AddMinutes(5)), ct) == 1;
    }
    public async Task Defer(Job job, Guid worker, TimeSpan delay, CancellationToken ct)
    {
        // A successful CA poll that is still pending is not a failed attempt.
        var now = DateTimeOffset.UtcNow;
        var due = delay >= DateTimeOffset.MaxValue - now ? DateTimeOffset.MaxValue : now + delay;
        var changed = await db.Jobs.Where(x => x.Id == job.Id && x.LeaseOwner == worker && x.LeaseUntil > DateTimeOffset.UtcNow && x.State == "Running")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "Pending").SetProperty(x => x.DueAt, due).SetProperty(x => x.Attempts, x => x.Attempts > 0 ? x.Attempts - 1 : 0).SetProperty(x => x.LeaseUntil, (DateTimeOffset?)null), ct);
        if (changed != 1) throw new LeaseLostException();
    }
    public async Task Fail(Job job, Guid worker, string message, CancellationToken ct, DateTimeOffset? notBefore = null)
    {
        var state = job.Attempts >= 5 ? "Failed" : "Pending"; var due = DateTimeOffset.UtcNow + RenewalPolicy.RetryDelay(job.Attempts);
        if (notBefore > due) due = notBefore.Value;
        await db.Jobs.Where(x => x.Id == job.Id && x.LeaseOwner == worker && x.LeaseUntil > DateTimeOffset.UtcNow && x.State == "Running").ExecuteUpdateAsync(s => s.SetProperty(x => x.State, state).SetProperty(x => x.Error, message).SetProperty(x => x.DueAt, due).SetProperty(x => x.LeaseUntil, (DateTimeOffset?)null), ct);
    }
}
