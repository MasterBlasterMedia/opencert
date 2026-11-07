# Threat model

Assets: identities and sessions, tenant records, DNS credentials, ACME account keys, customer certificate keys, agent credentials, deployment paths and audit records.

| Threat / boundary | Required control | Evidence required |
| --- | --- | --- |
| Email brute force/replay/scanners | browser binding, five attempts, expiration, atomic consumption, POST completion, generic responses | concurrent replay and MFA bypass tests |
| OAuth linking takeover | middleware state/PKCE, stable subjects, explicit recent-auth linking, no email auto-merge | controlled callback tests and real provider staging |
| Cross-tenant access | server membership checks in resource queries, role policy, compound ownership constraints | two-tenant API integration tests |
| Session theft/CSRF | secure cookie, antiforgery, exact CORS, revocation, recent assurance | browser and API tests |
| SSRF/rebinding | public address allow policy, pinned socket, SNI, no redirects, timeouts | private IPv4/IPv6 and mapped address tests |
| Malicious CSR | signature validation, exact SAN match, key strength policy, no private-key upload | malformed and wrong-name tests |
| DNS destruction | scoped TXT operations, preserve other values, exact cleanup IDs | provider simulators |
| Credential/database leak | authenticated encryption, external encryption key, redacted logs | inspection, rotation/recovery drill |
| Agent takeover | single-use enrollment, scoped token, expiry, replay prevention, local path allowlist | enrollment and unauthorized job tests |
| Command/path injection | no remote shell, locally configured typed deployment, reject links/path traversal | filesystem and command tests |
| Renewal race | atomic leases, fencing, idempotency, Retry-After/backoff | concurrent PostgreSQL tests |
| Audit manipulation | restricted append role plus externally retained signed checkpoints | operational configuration and tamper tests |
| Supply chain | pinned lockfiles, vulnerability scans, review of maintenance/licenses | CI reports |

Residual risk: DNS and CA availability, compromised customer hosts, email account compromise, external provider policy changes, database administrators and operator key custody. Automated checks are not a penetration test. Legal policies and license suitability require owner review. Unimplemented controls must stay visible in the verification report and block production claims.
