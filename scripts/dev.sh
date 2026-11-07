#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
: "${ConnectionStrings__Database:?Set the connection string for a disposable local PostgreSQL database}"
export ASPNETCORE_ENVIRONMENT=Development DOTNET_ENVIRONMENT=Development
export Web__Origin="${Web__Origin:-http://localhost:5173}"
export Email__DevelopmentDirectory="$PWD/.local/mail"
mkdir -p "$Email__DevelopmentDirectory"
chmod 700 "$Email__DevelopmentDirectory"
dotnet build OpenCert.sln --no-restore
dotnet apps/api/bin/Debug/net10.0/OpenCert.Api.dll --migrate
pids=()
cleanup() { for pid in "${pids[@]}"; do kill "$pid" 2>/dev/null || true; done; }
trap cleanup EXIT INT TERM
ASPNETCORE_URLS=http://localhost:5080 dotnet apps/api/bin/Debug/net10.0/OpenCert.Api.dll & pids+=("$!")
dotnet apps/worker/bin/Debug/net10.0/OpenCert.Worker.dll & pids+=("$!")
npm run dev --prefix apps/web -- --port 5173 & pids+=("$!")
wait -n "${pids[@]}"
