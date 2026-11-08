using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace OpenCert.Infrastructure;

public sealed class AppDb(DbContextOptions<AppDb> options, IDataProtectionProvider protection) : IdentityDbContext<User>(options), IDataProtectionKeyContext
{
    public JobLease? ActiveLease { get; set; }
    public async Task RequireLeaseAsync(CancellationToken ct = default)
    {
        if (ActiveLease is not { } lease) return;
        var now = DateTimeOffset.UtcNow;
        var job = await Jobs.FromSqlInterpolated($"SELECT * FROM \"Jobs\" WHERE \"Id\" = {lease.JobId} AND \"LeaseOwner\" = {lease.Owner} AND \"State\" = 'Running' AND \"LeaseUntil\" > {now} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
        if (job is null) throw new LeaseLostException();
    }
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        if (ActiveLease is null) return await base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
        if (Database.CurrentTransaction is not null) { await RequireLeaseAsync(ct); return await base.SaveChangesAsync(acceptAllChangesOnSuccess, ct); }
        await using var tx = await Database.BeginTransactionAsync(ct);
        await RequireLeaseAsync(ct); var count = await base.SaveChangesAsync(acceptAllChangesOnSuccess, ct); await tx.CommitAsync(ct); return count;
    }
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<OutgoingEmail> OutgoingEmails => Set<OutgoingEmail>();
    public DbSet<EmailChange> EmailChanges => Set<EmailChange>();
    public DbSet<AuthChallenge> AuthChallenges => Set<AuthChallenge>();
    public DbSet<AcmeAccount> AcmeAccounts => Set<AcmeAccount>();
    public DbSet<Organization> Organizations => Set<Organization>(); public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Website> Websites => Set<Website>(); public DbSet<LoginFlow> LoginFlows => Set<LoginFlow>();
    public DbSet<UserSession> Sessions => Set<UserSession>(); public DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();
    public DbSet<CertificateOrder> Orders => Set<CertificateOrder>(); public DbSet<Job> Jobs => Set<Job>();
    public DbSet<Notification> Notifications => Set<Notification>(); public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>(); public DbSet<WorkerHeartbeat> Heartbeats => Set<WorkerHeartbeat>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<Invitation>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Invitation>().HasIndex(x => new { x.OrganizationId, x.Email, x.CreatedAt });
        b.Entity<OutgoingEmail>().HasIndex(x => x.DeduplicationKey).IsUnique();
        b.Entity<EmailChange>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<EmailChange>().HasIndex(x => new { x.UserId, x.CreatedAt });
        b.Entity<AcmeAccount>().HasIndex(x => new { x.OrganizationId, x.Directory }).IsUnique();
        b.Entity<AcmeAccount>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Membership>().HasKey(x => new { x.OrganizationId, x.UserId });
        b.Entity<Membership>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Membership>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Website>().HasIndex(x => new { x.OrganizationId, x.Hostname }).IsUnique();
        b.Entity<Website>().HasAlternateKey(x => new { x.Id, x.OrganizationId });
        b.Entity<Website>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<CertificateOrder>().HasOne<Website>().WithMany().HasForeignKey(x => new { x.WebsiteId, x.OrganizationId }).HasPrincipalKey(x => new { x.Id, x.OrganizationId }).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Job>().HasIndex(x => x.DeduplicationKey).IsUnique(); b.Entity<Job>().HasIndex(x => new { x.State, x.DueAt });
        b.Entity<LoginFlow>().HasIndex(x => new { x.Email, x.CreatedAt });
        b.Entity<UserSession>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<RecoveryCode>().HasIndex(x => new { x.UserId, x.Hash }).IsUnique();
        b.Entity<RecoveryCode>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Notification>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<AuditEvent>().HasIndex(x => new { x.OrganizationId, x.At });
        // Includes the Identity authenticator secret. The external DP wrapping key is required in production.
        var protector = protection.CreateProtector("OpenCert.IdentityTokens.v1");
        b.Entity<IdentityUserToken<string>>().Property(x => x.Value).HasConversion(v => v == null ? null : protector.Protect(v), v => v == null ? null : protector.Unprotect(v));
    }
}

public sealed record JobLease(Guid JobId, Guid Owner);
public sealed class LeaseLostException : Exception { public LeaseLostException() : base("The job lease is no longer owned by this worker.") { } }
