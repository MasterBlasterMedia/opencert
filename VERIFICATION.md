# OpenCert verification and release gates

Last updated: 2026-11-07. This is an implementation and evidence ledger, not a security certification.

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
| Database | Three EF migrations applied to a disposable PostgreSQL 18 database |

Current automated counts: 48 xUnit cases, 3 Vitest cases, 5 black-box API journeys, 3 Playwright journeys. API and worker container images build locally; the Render Blueprint passes the current JSON schema. CI execution remains to be verified after source publication. Automated accessibility checks are not a full WCAG 2.2 AA audit.

## Implemented, not externally verified

- Google/Microsoft/GitHub/Apple/Discord middleware and explicit account linking. Unlinked identities first create/verify an email account, then connect intentionally; provider email claims never merge accounts. Real provider applications/callbacks and Apple developer-account setup remain unverified.
- Resend delivery adapter and encrypted durable outbox. Local tests use a private development mailbox; sender-domain verification and real delivery remain outstanding.
- Certes DNS-01 order creation, validation, finalization, chain retrieval and revocation. No successful real Let's Encrypt staging issuance has been recorded yet.
- TLS diagnostics with public-address validation and pinned socket connection. Internet failures must remain inconclusive, never become a green status.
- Local customer key utility with CSR, inspect and install commands. Full server adapters and operational deployment validation remain outstanding.

## Incomplete acceptance gates

| Requirement | Remaining work |
| --- | --- |
| DNS automation | Cloudflare, GoDaddy and Route 53 scoped credentials, TXT preservation/cleanup, live least-privilege tests |
| Agent automation | Enrollment, key rotation, scoped outbound assignments, typed server configuration/reload, rollback, revocation and local permissions |
| Renewal | Durable periodic scheduling, ACME ARI/Retry-After, actual issuance/installation cycles and stale-worker fencing |
| Alerts | Expiry/failure scheduling, preferences, deduplication and real email delivery |
| Administration | Explicit secure administrator bootstrap, abuse controls, operational dashboards |
| Security assurance | OAuth attack simulations/live callbacks, DNS/ACME staging tests, hostile network tests, secret scanning, egress policy and independent review |
| Privacy and retention | Final retention/export/deletion policy; certificate-history workspaces currently cannot be deleted |
| Deployment | Validated images, CI, branch migration on Neon, Render services, HTTPS/cookie/proxy behavior and rollback drill |
| Release | Dependency/license review, full browser/accessibility matrix, operator privacy/terms/security contacts |

## External state

The GitHub repository and Neon project were created and read back through their connected accounts. No OpenCert Render service is live. The database named production has not received application migrations. No production certificate, unattended renewal, provider callback or independent audit is claimed.
