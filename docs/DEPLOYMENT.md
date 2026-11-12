# Deployment and operations

This is a deployment proposal for a development release, not a statement that the release gates have passed. Read ../VERIFICATION.md first.

## Topology

Serve the built React client and ASP.NET API from one HTTPS Render web service. Deploy a separate worker from the same source revision. Use Neon PostgreSQL for persistent state and the encrypted shared Data Protection key ring. Customer certificate keys stay on customer machines. No persistent Render disk is needed for application state.

`render.yaml` uses a free web instance and a paid `0.5c-512mb` worker plan. **Applying it incurs worker costs. Obtain operator approval and check the current Render price first.** A free web service can sleep and is not an uptime guarantee. This proposal has automatic deployment disabled. No paid resources have been provisioned by the development process.

## Required configuration

| Variable | Purpose |
| --- | --- |
| `Web__Origin` | Required exact HTTPS browser origin, e.g. `https://YOUR-SERVICE.onrender.com` |
| `Security__TrustedProxies` / `Security__TrustedProxyNetworks` | Comma-separated documented ingress IPs or CIDRs; empty means forwarded headers are ignored |
| `Security__ForwardLimit` | Trusted proxy hop limit, 1 by default, maximum 5 |
| `ConnectionStrings__Database` | Npgsql connection string; production requires `SSL Mode=VerifyFull` |
| `Security__KeyEncryptionCertificateBase64` | Base64 PKCS#12 wrapping certificate with its private key, held in Render secrets |
| `Security__KeyEncryptionPassword` | Password protecting that PKCS#12 file |
| `Email__ResendApiKey` / `Email__From` | Resend key and verified sender |
| `Acme__Directory` | Staging directory initially; never silently change to production |
| `Acme__ContactEmail` | Operator-controlled CA contact address |

Instead of the base64 setting, an external secret file can be referenced by `Security__KeyEncryptionCertificate`. Use the same wrapping key and password for API, worker and migration processes. Back up it separately from PostgreSQL; losing it can make authenticator/ACME secrets unreadable. Restrict secret-management access. Do not store the wrapping key in the database it protects.

Do not reuse a customer certificate key as the wrapping key. Rotation must retain old wrapping certificates while old key-ring entries remain in use; the current single-certificate configuration requires a planned maintenance/reencryption procedure before rotation and a tested backup. Do not claim rotation is automated.

## Database and migrations

Create a separate Neon staging branch. Apply all migrations there with a **direct** connection, then run the integration checks against disposable data. `--migrate` is explicit: application startup never silently changes schema. Use a direct production connection for the migration step after reviewing the generated migration and backup/restore plan. Runtime pooling must be tested separately; no pooler compatibility claim is made yet.

```sh
# Supply secrets in the process environment through your host/secret manager.
dotnet OpenCert.Api.dll --migrate
```

Migrations on free Render web services must run separately because Render pre-deploy commands require a paid service. Avoid concurrent migration runs; EF's provider migration lock and a single release operator are required. Do not put database credentials in the build context or Docker build arguments. Production connection strings are parsed and checked for `VerifyFull`, not accepted by substring matching.

## Reverse proxy and public HTTPS

Render terminates public TLS. Configure `Security__TrustedProxies` and/or `Security__TrustedProxyNetworks` with the actual documented ingress addresses for the deployment. The API processes forwarded client IP and scheme only from these sources, within `Security__ForwardLimit`. It ignores forwarded host/prefix headers and rejects hosts other than `Web__Origin`. Outside Development, non-health requests must resolve to HTTPS and the configured public port. Read-only health GETs may use internal HTTP with the expected Host header. `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` and all-addresses proxy networks are rejected.

An empty proxy list leaves forwarding disabled; it never enables trust of every source. Do not guess Render ingress ranges or use an unrestricted private-network range to make authentication work. Verify the scheme, host, client-IP chain, health checks and real provider callback through the deployed ingress before enabling public sign-in. Middleware tests cover forged headers and unknown hosts, but Render-specific behavior remains an external deployment gate.

This follows Microsoft's [forwarded-header guidance](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0) and [unknown-proxy hardening](https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/8/forwarded-headers-unknown-proxies?view=aspnetcore-10.0).

Use the same origin for browser and API. A separate static site on an unrelated `onrender.com` site is not an equivalent authentication topology. Separate custom subdomains require explicit CORS/cookie configuration and browser verification.

## Deploy and verify

1. Complete the release gates, review costs, configure verified email and publish operator privacy/terms/security contacts.
2. Build the API and worker images from one reviewed revision and run CI. `deploy/api.Dockerfile` packages the client; `deploy/worker.Dockerfile` packages the worker.
3. Validate `render.yaml` against Render's current schema using `node scripts/validate-blueprint.mjs` from `apps/web`, and validate it in Render before applying.
4. Apply migrations to a staging branch, test, then migrate the intended deployment database with a direct connection.
5. Create the approved services. Set secret values through Render; the worker references the web service's shared secret variables.
6. Wait for the exact deploy to become live. Check `/health/ready`, actual email sign-in, passkey RP/origin behavior, TOTP, the `/api/v1/status` worker heartbeat and a persisted website check.
7. Complete a Let's Encrypt staging issuance and installation on a domain/server you control. An issued certificate alone does not verify public HTTPS or renewal.

`/health/live` checks process availability; `/health/ready` checks database reachability and requires all application migrations to be applied. Public `/api/v1/status` distinguishes API, database and worker. Structured logs omit secret values; avoid increasing framework request/token logging in production. Add monitoring and alerts before real customer use.

## Scheduled checks and reminders

The worker schedules public HTTPS checks every six hours and checks due schedules once a minute. Expiration notices use the last observed certificate and thresholds of 30, 14, 7 and 1 day, then expiration. A failed live check does not erase the last observed certificate; the UI labels that historical observation. Each certificate/threshold generates at most one workspace notice.

Manual renewal reminders initially use two-thirds of the certificate’s actual validity period. After issuance, the worker fetches public ACME Renewal Information (ARI) from the issuing CA and persists a randomly selected instant within its suggested window, bounded by certificate expiration. An unchanged window keeps the saved instant, including across restarts. Past windows trigger an immediate reminder. Existing issued certificates receive a guidance job during maintenance after the fifth migration.

ARI polling follows Retry-After with a one-minute minimum, one-day maximum and six-hour default, as allowed by [RFC 9773](https://www.rfc-editor.org/rfc/rfc9773.html#section-4.3.2). Failed guidance requests retain the saved reminder and back off from six to twenty-four hours; HTTP 429/503 deadlines can extend that wait. Unsupported CAs retain validity-based reminders. Expired and revoked certificates are not queried. ACME order operations also persist HTTP 429/503 retry deadlines. Public ARI lookups do not require decrypting an account signing key.

The UI displays the reminder date, its source and guidance lookup failures. Issuance and installation remain manual; this is not an unattended renewal implementation. Full agent renewal, replacement-order signaling and public CA validation remain release gates.

Users can disable operational certificate email in account settings. Security messages remain mandatory. Operational email is capped at 20 per recipient per day; workspace notifications remain available when the cap suppresses email. Before delivery, the worker re-checks current workspace membership, the verified email and the email preference. Reminders and preference tests use the private local mail sink; no real-delivery claim is made.

## Rollback, backup and recovery

Keep the preceding compatible image and source revision. A Render rollback does not reverse database migrations or external DNS/CA operations. Prefer backward-compatible schema changes; restore into a separate Neon branch and validate before replacing a live database. Do not drop schema to force an older application to start.

Test restoration of PostgreSQL **and** the wrapping key together. Verify that existing sessions, authenticator secrets and ACME accounts can be read after a restart. Revoking an application session does not revoke a provider grant, and deleting a workspace does not remove external DNS records or installed certificates.

Primary references checked 2026-11-07: [Blueprint fields](https://render.com/docs/blueprint-spec), [service types](https://render.com/docs/service-types), [deploy lifecycle](https://render.com/docs/deploys), [health checks](https://render.com/docs/health-checks), [background workers](https://render.com/docs/background-workers).
