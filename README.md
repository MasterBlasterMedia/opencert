# OpenCert

**Free website security, made simple.**

OpenCert is free, self-hostable certificate-management software built with React, ASP.NET Core 10, PostgreSQL, a separate worker, and a customer-side key utility. It uses established certificate authorities; it is not a public root CA. Domain registration and hosting are separate costs.

**Development release. Do not use this build for unattended production certificate management.** See [VERIFICATION.md](VERIFICATION.md) for measured results and unfinished release gates. No independent security audit has been performed.

## Working today

- Passwordless email codes and intentional magic-link confirmation, using Resend or a private local development mailbox.
- Enforced TOTP, one-use recovery codes, passkeys with required user verification, recent-authentication gates, and revocable server sessions.
- Explicit Google, Microsoft, GitHub, Apple, and Discord account connection middleware. Providers appear only when configured; real provider callbacks still require validation with operator credentials.
- Personal workspaces, invitations, server-enforced roles, ownership transfer, profiles, verified email changes and guarded deletion.
- Persistent website onboarding, public DNS/hosting hints, live TLS inspection, and honest status labels.
- Signed CSR validation and Certes-based ACME DNS-01 order, verification, public-chain download and revocation workflows. The default CA is Let's Encrypt **staging**; local Pebble issuance/revocation is tested, while real staging issuance remains an acceptance gate.
- Scheduled website checks, expiration and manual-renewal reminders using CA renewal guidance (ARI) with an actual-validity fallback, request-failure notifications and configurable operational email.
- A durable PostgreSQL job queue with lease renewal, stale-worker write fencing, automatic CA polling, persisted rate-limit deadlines, and an encrypted email outbox.
- Local `csr`, `inspect`, and `install` key utility commands. Customer certificate private keys stay on the customer machine.

Automatic DNS changes, enrolled agent operation and unattended renewal are still being developed. Saving a setup preference or issuing a certificate never marks live HTTPS or automatic renewal as verified.

## Local development

Prerequisites: .NET SDK matching `global.json`, Node 24, PostgreSQL 18, Python 3, and Chromium for Playwright. Install dependencies:

```sh
dotnet restore OpenCert.sln --locked-mode
npm ci --prefix apps/web
npm exec --prefix apps/web -- playwright install chromium
```

Create a disposable PostgreSQL database and set its connection string in your shell. Use development mode only on your machine:

```sh
export ConnectionStrings__Database='Host=localhost;Database=opencert;Username=YOUR_LOCAL_ROLE;Password=YOUR_LOCAL_PASSWORD'
export ASPNETCORE_ENVIRONMENT=Development
export DOTNET_ENVIRONMENT=Development
export Web__Origin=http://localhost:5173
./scripts/dev.sh
```

Open `http://localhost:5173`. Development email is written to private files in `.local/mail/`. Never serve that directory or commit its contents. There are no default accounts or authentication bypass endpoints.

For a container-based local environment, generate a random `POSTGRES_PASSWORD` in an untracked `.env` using `.env.example` as a template, set `LOCAL_UID` and `LOCAL_GID` to your user/group IDs, create `.local/mail` with mode `0700`, then run `docker compose up --build`. Open `http://localhost:8080`. Migrations run as a separate one-shot service. The Compose configuration binds the application only to loopback and is **not** a production deployment template.

## Checks

```sh
dotnet build OpenCert.sln --no-restore
dotnet test tests/OpenCert.UnitTests --no-restore
# Use a separate empty database whose name ends in _tests or _ci:
OPENCERT_PERSISTENCE_DATABASE="$ConnectionStrings__Database" dotnet test tests/OpenCert.PersistenceTests --no-restore
npm run build --prefix apps/web
npm run lint --prefix apps/web
npm test --prefix apps/web
# With the disposable local API, worker and web server running:
python3 tests/OpenCert.IntegrationTests/test_api.py
npm run e2e --prefix apps/web
```

The ACME integration harness requires Docker (or `OPENCERT_CONTAINER_ENGINE=podman`), built Debug binaries, and an explicit disposable database named with `_ci` or `_tests`. Run `bash scripts/test-acme.sh` with `ConnectionStrings__Database` set to that database. It starts an isolated API/worker on port 5081 and loopback Pebble/DNS services, performs actual DNS proof validation, and cleans up the processes it creates. It never disables CA challenge validation or TLS hostname checks. Keep ports 5081, 8053, 8055, 14000 and 15000 free.

Use the [deployment guide](docs/DEPLOYMENT.md) for Render/Neon, [authentication guide](docs/AUTHENTICATION.md) for provider setup, [architecture](docs/architecture/DECISIONS.md), and [threat model](docs/security/THREAT_MODEL.md) for boundaries. Deployment configuration is a reviewable proposal; it does not mean services are running.

## Contributing and license

See [CONTRIBUTING.md](CONTRIBUTING.md) and [SECURITY.md](SECURITY.md). Source code is licensed under [Apache License 2.0](LICENSE). Third-party dependencies retain their own licenses. A formal dependency/license review is a release gate.
