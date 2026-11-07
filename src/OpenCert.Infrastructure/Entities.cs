using Microsoft.AspNetCore.Identity;
namespace OpenCert.Infrastructure;

public sealed class User : IdentityUser { public string DisplayName { get; set; } = "Website owner"; }
public sealed class Organization { public Guid Id { get; set; } = Guid.NewGuid(); public string Name { get; set; } = "My websites"; public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow; }
public sealed class Membership { public Guid OrganizationId { get; set; } public string UserId { get; set; } = ""; public string Role { get; set; } = "Owner"; }
public sealed class Website
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid OrganizationId { get; set; }
    public string Hostname { get; set; } = ""; public string SetupMethod { get; set; } = "Undecided";
    public bool? HttpsTrusted { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? CheckedAt { get; set; }
    public string? Issuer { get; set; }
    public string? Fingerprint { get; set; }
    public string? HostingProvider { get; set; }
    public string? DnsProvider { get; set; }
    public string Diagnostic { get; set; } = "Check your website to find the next step."; public string DiscoveryJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class LoginFlow
{
    public Guid Id { get; set; } = Guid.NewGuid(); public string Email { get; set; } = ""; public string CodeHash { get; set; } = ""; public string BrowserHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow; public int Attempts { get; set; }
    public bool Consumed { get; set; }
    public string? MfaUserId { get; set; }
    public bool MfaCompleted { get; set; }
    public int MfaAttempts { get; set; }
}
public sealed class UserSession
{
    public Guid Id { get; set; } = Guid.NewGuid(); public string UserId { get; set; } = ""; public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddHours(12); public DateTimeOffset? StrongAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
public sealed class RecoveryCode { public Guid Id { get; set; } = Guid.NewGuid(); public string UserId { get; set; } = ""; public string Hash { get; set; } = ""; }
public sealed class CertificateOrder
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid OrganizationId { get; set; }
    public Guid WebsiteId { get; set; }
    public string HostnamesJson { get; set; } = "[]"; public string CsrPem { get; set; } = ""; public string? AcmeUrl { get; set; }
    public string State { get; set; } = "Queued"; public string ChallengesJson { get; set; } = "[]"; public string? CertificatePem { get; set; }
    public string? Diagnostic { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow; public DateTimeOffset? ExpiresAt { get; set; }
    public bool Staging { get; set; } = true; public string Directory { get; set; } = "";
}
public sealed class Job
{
    public Guid Id { get; set; } = Guid.NewGuid(); public string Kind { get; set; } = ""; public Guid ResourceId { get; set; }
    public string DeduplicationKey { get; set; } = ""; public string State { get; set; } = "Pending"; public int Attempts { get; set; }
    public DateTimeOffset DueAt { get; set; } = DateTimeOffset.UtcNow; public DateTimeOffset? LeaseUntil { get; set; }
    public Guid? LeaseOwner { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
public sealed class Notification { public Guid Id { get; set; } = Guid.NewGuid(); public Guid OrganizationId { get; set; } public string Message { get; set; } = ""; public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow; public bool Read { get; set; } }
public sealed class AuditEvent { public long Id { get; set; } public Guid? OrganizationId { get; set; } public string? UserId { get; set; } public string Action { get; set; } = ""; public string? ResourceId { get; set; } public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow; }
public sealed class WorkerHeartbeat { public string Id { get; set; } = "worker"; public DateTimeOffset At { get; set; } }

public sealed class AcmeAccount { public Guid Id { get; set; } = Guid.NewGuid(); public Guid OrganizationId { get; set; } public string Directory { get; set; } = ""; public string KeyProtected { get; set; } = ""; }
public sealed class AuthChallenge { public Guid Id { get; set; } = Guid.NewGuid(); public string? UserId { get; set; } public string Purpose { get; set; } = ""; public string BrowserHash { get; set; } = ""; public bool Consumed { get; set; } public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddMinutes(5); }

public sealed class Invitation
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid OrganizationId { get; set; }
    public string Email { get; set; } = ""; public string Role { get; set; } = "Viewer";
    public string TokenHash { get; set; } = ""; public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddDays(7);
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
public sealed class OutgoingEmail
{
    public Guid Id { get; set; } = Guid.NewGuid(); public string PayloadProtected { get; set; } = "";
    public string DeduplicationKey { get; set; } = ""; public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddDays(1); public DateTimeOffset? SentAt { get; set; }
}
public sealed class EmailChange
{
    public Guid Id { get; set; } = Guid.NewGuid(); public string UserId { get; set; } = ""; public Guid SessionId { get; set; }
    public string Email { get; set; } = ""; public string CodeHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow; public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddMinutes(10);
    public int Attempts { get; set; }
    public bool Consumed { get; set; }
}
