# OpenCert architecture

Status: implementation in progress; acceptance is tracked in VERIFICATION.md.

## Boundaries

A React/TypeScript/Vite client calls an ASP.NET Core 10 modular monolith. A separate .NET worker uses PostgreSQL durable jobs. A customer-side .NET agent owns local certificate keys. PostgreSQL stores Identity, tenants, website observations, orders, public certificates, job leases, notifications and audit events. No customer certificate private keys are accepted by the API.

Use same-origin /api through Vite locally. The Render proposal serves the frontend and API from one origin in the same container. Cookies are HttpOnly, Secure in production, SameSite=Lax; all unsafe browser requests require an antiforgery header. No bearer tokens in localStorage. Identity security stamps and a revocable session table are checked on requests.

Email login consumes a bounded, expiring challenge in a transaction and binds it to the initiating browser. Magic-link GET is inert; confirmation POST consumes it. Email access never skips enrolled MFA. Provider identities are keyed by issuer/provider subject; matching email never links accounts implicitly. Identity implements TOTP and WebAuthn; recent strong authentication gates credential and server changes.

## Authorization

Every customer resource belongs to an organization. Each API resource query requires membership and the action's role; a client-supplied organization UUID is never authority. Ownership transfers require a transactional last-owner check. Workers operate only on persisted authorized job payloads. Agents receive scoped, expiring assignments; no command strings or arbitrary paths from the control plane.

## Certificate lifecycle

Website registration -> DNS/hosting discovery -> setup recommendation. Public DNS observations do not prove control. ACME validates domain control. A CSR must be signed, use allowed key algorithms and match the requested names. Order -> challenge pending -> CA ready -> issued -> installation pending -> live verified. Manual CSR renewal requires another CSR and manual installation; it is never called automatic. An observed valid live certificate can be healthy without an OpenCert order. Hosting detection is explicitly an inference.

ACME defaults to Let's Encrypt staging. Production requires explicit CA directory and subscriber agreement acceptance. Central ACME account keys use authenticated encryption; customer certificate private keys are generated locally. DNS adapters may mutate only authorized _acme-challenge TXT records, preserving unrelated values. ACME rate limits and Retry-After guide durable retries.

## Persistence and scheduling

EF migrations are a separate deployment step using a direct Neon connection. Runtime may use a tested pooled connection with TLS VerifyFull. Jobs are claimed atomically with PostgreSQL FOR UPDATE SKIP LOCKED, a lease owner and lease deadline. Leases renew while work is active. Resource writes lock and verify the current lease in the same transaction; stale workers cannot save results or complete stolen jobs. External CA/email requests remain at-least-once across network/crash boundaries, using stable CA account keys, saved remote order URLs and email idempotency keys where supported. Successful pending CA polls defer the same durable job using Retry-After without consuming its failure budget. Payloads contain resource IDs, not secrets. Certificate renewal timing derives from actual validity and ARI when available; no assumed 90-day duration.

ASP.NET Data Protection keys are shared in PostgreSQL, encrypted at rest with an operator-supplied certificate whose private key is outside the database. Development uses a clearly labeled local-only alternative. Secrets never enter source control. Render filesystem is not persistent state.

## Network safety

Normalize hostnames using IDNA and reject URLs with credentials, ports, paths, query strings or fragments. HTTPS diagnostics resolve DNS, reject non-public addresses, connect to a validated pinned address with SNI set to the hostname, enforce TLS chain and hostname validation, and never follow redirects. Bounded timeouts and rate limits prevent scan amplification. An egress firewall remains defense in depth.

## Release gates

Build, unit, integration, browser and accessibility checks precede release. Real provider callbacks, DNS permissions, ACME staging issuance, agent deployment and production cross-origin browser behavior have separate acceptance gates. No production-readiness claim until those pass. Independent review is required before any audit claim.
