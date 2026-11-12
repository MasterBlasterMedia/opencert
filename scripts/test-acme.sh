#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
: "${ConnectionStrings__Database:?Supply a disposable PostgreSQL database ending in _ci or _tests}"
python3 - <<'PY'
import os, re
connection = os.environ['ConnectionStrings__Database']
match = re.search(r'(?:^|;)\s*Database\s*=\s*([^;]+)', connection, re.I)
if not match or not match[1].endswith(('_ci', '_tests')):
    raise SystemExit('Refusing ACME integration tests without an explicitly named disposable database.')
PY
engine="${OPENCERT_CONTAINER_ENGINE:-docker}"
dotnet_bin="${OPENCERT_DOTNET:-dotnet}"
run_dir="$(mktemp -d /tmp/opencert-acme-ci.XXXXXX)"
chmod 700 "$run_dir"
name="opencert-acme-ci-$$"
api_pid=''
worker_pid=''
cleanup() {
  if [[ -n "$api_pid" ]]; then kill "$api_pid" 2>/dev/null || true; fi
  if [[ -n "$worker_pid" ]]; then kill "$worker_pid" 2>/dev/null || true; fi
  "$engine" rm -f "$name-ca" "$name-dns" >/dev/null 2>&1 || true
  rm -rf "$run_dir"
}
trap cleanup EXIT INT TERM
# These test-only services listen on loopback. DNS validation remains enabled.
"$engine" run -d --name "$name-dns" --network host ghcr.io/letsencrypt/pebble-challtestsrv:2.10.1 \
  -dnsserver 127.0.0.1:8053 -management 127.0.0.1:8055 -http01 '' -https01 '' -tlsalpn01 '' -doh '' -defaultIPv6 '' >/dev/null
"$engine" run -d --name "$name-ca" --network host \
  -e PEBBLE_VA_NOSLEEP=1 -v "$PWD/tests/acme/pebble.json:/test/config/opencert.json:ro" \
  ghcr.io/letsencrypt/pebble:2.10.1 -config test/config/opencert.json -strict -dnsserver 127.0.0.1:8053 >/dev/null
"$engine" cp "$name-ca:/test/certs/pebble.minica.pem" "$run_dir/root.pem"
export DOTNET_ENVIRONMENT=Development ASPNETCORE_ENVIRONMENT=Development
export Email__ResendApiKey=''
export Acme__Directory=https://localhost:14000/dir Acme__DevelopmentRootCertificate="$run_dir/root.pem" Acme__ContactEmail=acme-test@example.com
export Logging__LogLevel__Default=Warning ASPNETCORE_URLS=http://localhost:5081
export OPENCERT_TEST_URL=http://localhost:5081 OPENCERT_DOTNET="$dotnet_bin"
"$dotnet_bin" apps/api/bin/Debug/net10.0/OpenCert.Api.dll --migrate
"$dotnet_bin" apps/api/bin/Debug/net10.0/OpenCert.Api.dll > "$run_dir/api.log" 2>&1 & api_pid=$!
"$dotnet_bin" apps/worker/bin/Debug/net10.0/OpenCert.Worker.dll > "$run_dir/worker.log" 2>&1 & worker_pid=$!
ready=false
for attempt in $(seq 1 30); do
  if curl --fail --silent --output /dev/null --cacert "$run_dir/root.pem" https://localhost:14000/dir && curl --fail --silent --output /dev/null http://localhost:5081/health/ready; then ready=true; break; fi
  sleep 1
done
if [[ "$ready" != true ]]; then echo 'Isolated ACME services did not become ready.' >&2; exit 1; fi
python3 tests/OpenCert.IntegrationTests/test_acme.py
