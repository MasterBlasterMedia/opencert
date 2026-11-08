# OpenCert verification and release gates

Last updated: 2026-11-08. This is an implementation and evidence ledger, not a security certification.

## Verified locally

| Area | Evidence |
| --- | --- |
| Build | .NET solution builds with zero warnings; React/TypeScript production build succeeds |
| Authentication | Black-box API journey verifies email browser binding, single use, enforced MFA, session revocation and CSRF |
| Recovery | Concurrent consumers of the same recovery code yield exactly one success; rotation invalidates earlier codes |
| Account lifecycle | Email change is bound to the initiating session, verifies the new email, revokes other sessions; MFA removal and re-enrollment pass |
| Teams | Invitation email binding and replay rejection; Viewer write denial; role changes; last-owner guard; ownership transfer and immediate loss of former owner's privileges |
| Browser | Chromium registers and uses a virtual user-verifying passkey, rejects assertion replay and removes it; email onboarding persists across reload |
| UI accessibility | Automated axe checks on homepage and signed-in empty dashboard, light/dark homepage; mobile homepage has no horizontal overflow |
| Policy tests | Hostname normalization, IP restrictions, provider suffix boundaries, signed CSR/SAN matching, actual-lifetime renewal calculation, stale-status handling |
| Dependencies | npm audit reports zero vulnerabilities after patched tooling updates; NuGet reports no vulnerable direct/transitive packages |
| Database | Three EF migrations applied to disposable PostgreSQL 18 databases |
| Proxy boundary | Middleware tests verify explicit proxy trust, forged-header rejection, fixed host/port, HTTPS enforcement and bounded hop processing |
| Durable jobs | Four PostgreSQL tests cover concurrent claims, lost-lease write/completion rejection, bounded crash recovery and persisted polling deadlines |
| Local ACME | Pebble performs real DNS-01 validation, issuance and revocation; concurrent submissions create one active order; a domain without DNS proof is refused; CA polling persists between worker cycles |
| Local installation | Matching key accepted; wrong key, linked targets/parents and shared private-key permissions rejected; previous public chain retained atomically |

Current automated counts: 65 unit cases, 4 PostgreSQL persistence cases, 3 Vitest cases, 5 black-box API journeys, 3 Playwright journeys, 2 isolated Pebble ACME journeys. API and worker container images build locally; the Render Blueprint passes the current JSON schema. The published baseline passed both GitHub Actions jobs ([run 42425443994](https://github.com/MasterBlasterMedia/opencert/actions/runs/42425443994)); the expanded ACME/persistence workflow requires a new run. Automated accessibility checks are not a full WCAG 2.2 AA audit.

## Implemented, not externally verified

- Google/Microsoft/GitHub/Apple/Discord middleware and explicit account linking. Unlinked identities first create/verify an email account, then connect intentionally; provider email claims never merge accounts. Real provider applications/callbacks and Apple developer-account setup remain unverified.
- Resend delivery adapter and encrypted durable outbox. Local tests use a private development mailbox; sender-domain verification and real delivery remain outstanding.
- Certes DNS-01 is verified against a local Pebble CA. No successful real Let's Encrypt staging issuance has been recorded yet.
- TLS diagnostics with public-address validation and pinned socket connection. Internet failures must remain inconclusive, never become a green status.
- Local customer key utility with CSR, inspect and install commands. Full server adapters and operational deployment validation remain outstanding.

## Incomplete acceptance gates

| Requirement | Remaining work |
| --- | --- |
| DNS automation | Cloudflare, GoDaddy and Route 53 scoped credentials, TXT preservation/cleanup, live least-privilege tests |
| Agent automation | Enrollment, key rotation, scoped outbound assignments, typed server configuration/reload, rollback, revocation and local permissions |
| Renewal | Durable periodic renewal scheduling, ACME ARI and live issuance/installation renewal cycles. Order polling already honors Retry-After and stale-worker database writes are fenced |
| Alerts | Expiry/failure scheduling, preferences, deduplication and real email delivery |
| Administration | Explicit secure administrator bootstrap, abuse controls, operational dashboards |
| Security assurance | OAuth attack simulations/live callbacks, DNS/ACME staging tests, hostile network tests, secret scanning, egress policy and independent review |
| Privacy and retention | Final retention/export/deletion policy; certificate-history workspaces currently cannot be deleted |
| Deployment | Validated images, CI, branch migration on Neon, Render services, HTTPS/cookie/proxy behavior and rollback drill |
| Release | Dependency/license review, full browser/accessibility matrix, operator privacy/terms/security contacts |

## External state

The source is published at [MasterBlasterMedia/opencert](https://github.com/MasterBlasterMedia/opencert). The Neon project was created and read back through its connected account. No OpenCert Render service is live. The database named production has not received application migrations. No production certificate, unattended renewal, provider callback or independent audit is claimed.
