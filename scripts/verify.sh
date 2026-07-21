#!/usr/bin/env bash
#
# verify.sh — the gate of record (constitution Art. IX.2, CLAUDE.md §7).
#
# Builds both stacks warnings-as-errors, stands up a disposable random-port
# Postgres, applies EF migrations idempotently, runs backend + frontend tests
# (always including the Category=PrivacyReporting suite), lint, typecheck, and
# asserts the built frontend makes no external font request. Non-zero exit on any
# failure. Requires only Docker + local .NET/Node toolchains — no network beyond
# package restore. Never edit this script to make a story pass (it is L2 itself).
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

# --- resolve the dotnet driver (Git Bash on Windows may not have it on PATH) ---
if command -v dotnet >/dev/null 2>&1; then
  DOTNET="dotnet"
elif [ -x "/c/Program Files/dotnet/dotnet.exe" ]; then
  DOTNET="/c/Program Files/dotnet/dotnet.exe"
else
  echo "ERROR: dotnet SDK not found on PATH or at 'C:\\Program Files\\dotnet'." >&2
  exit 1
fi

if ! command -v docker >/dev/null 2>&1; then
  echo "ERROR: docker not found — the disposable test database needs it." >&2
  exit 1
fi

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

PG_PORT=$(( (RANDOM % 20000) + 20000 ))
PG_NAME="hap-verify-$$"

cleanup() {
  docker rm -f "$PG_NAME" >/dev/null 2>&1 || true
}
trap cleanup EXIT

echo "==> [1/9] Backend build (warnings-as-errors)"
"$DOTNET" build backend/Hap.sln -c Release

echo "==> [2/9] Starting disposable Postgres on port ${PG_PORT} (container ${PG_NAME})"
docker run -d --name "$PG_NAME" \
  -e POSTGRES_USER=hap -e POSTGRES_PASSWORD=hap -e POSTGRES_DB=hap \
  -p "${PG_PORT}:5432" postgres:16-alpine >/dev/null

echo "    waiting for Postgres to accept connections..."
for i in $(seq 1 30); do
  if docker exec "$PG_NAME" pg_isready -U hap -d hap >/dev/null 2>&1; then
    echo "    Postgres ready"
    break
  fi
  if [ "$i" -eq 30 ]; then
    echo "ERROR: Postgres did not become ready within 30s." >&2
    exit 1
  fi
  sleep 1
done

export HAP_DB_CONNECTION="Host=localhost;Port=${PG_PORT};Database=hap;Username=hap;Password=hap"

echo "==> [3/9] Restoring local dotnet tools (dotnet-ef)"
"$DOTNET" tool restore

echo "==> [4/9] Applying EF migrations (idempotent — runs twice, second must no-op)"
"$DOTNET" ef database update \
  --project backend/src/Hap.Infrastructure \
  --startup-project backend/src/Hap.Infrastructure \
  --configuration Release
"$DOTNET" ef database update \
  --project backend/src/Hap.Infrastructure \
  --startup-project backend/src/Hap.Infrastructure \
  --configuration Release

echo "==> [5/9] Backend tests"
"$DOTNET" test backend/Hap.sln -c Release --no-build

echo "==> [6/9] Privacy/Reporting regression suite (always-on; passes with zero matches for now)"
"$DOTNET" test backend/Hap.sln -c Release --no-build --filter Category=PrivacyReporting

echo "==> [7/9] Frontend: install, lint, typecheck, test"
cd "$ROOT/app"
npm ci
npm run lint
npm run typecheck
npm run test

echo "==> [8/9] Frontend production build"
npm run build

echo "==> [9/9] Asserting no external font request in built output"
if grep -rq "fonts.googleapis" dist; then
  echo "ERROR: 'fonts.googleapis' found in built output — fonts must be self-hosted." >&2
  exit 1
fi

cd "$ROOT"
echo ""
echo "verify.sh: ALL GREEN"
