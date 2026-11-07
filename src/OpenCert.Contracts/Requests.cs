namespace OpenCert.Contracts;

public record EmailStart(string Email);
public record EmailFinish(Guid FlowId, string Code);
public record CodeInput(string Code);
public record WebsiteInput(string Hostname);
public record SetupInput(string Method);
public record NameInput(string Name);
public record OrderInput(string CsrPem, string[] Hostnames, bool AcceptTerms);
public record PasskeyInput(string CredentialJson, string Name);
public record InviteInput(string Email, string Role);

public record TokenInput(string Token);
public record RoleInput(string Role);
public record TransferInput(string UserId);
public record ConfirmInput(string Confirmation);
