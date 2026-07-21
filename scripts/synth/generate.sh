#!/usr/bin/env bash
#
# generate.sh — deterministic synthetic-directory generator wrapper (HAP-2, FR-020).
#
# Runs Hap.Synth and writes:
#   backend/src/Hap.Synth/output/directory.json   (DirectorySnapshot shape)
#   backend/src/Hap.Synth/output/seed-users.json  (one seeded user per role)
#
# Determinism: same seed => byte-identical output (asserted by Hap.Synth.Tests).
# Pass an alternative seed as the first argument to explore other populations;
# with no argument the seed is omitted and Program.cs defaults to
# Distributions.CanonicalSeed — the single source of truth for the canonical build.
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

# Optional seed override; empty means "let the generator use its canonical default".
SEED="${1:-}"
OUT_DIR="backend/src/Hap.Synth/output"

# Resolve the dotnet driver (Git Bash on Windows may not have it on PATH) —
# mirrors scripts/verify.sh.
if command -v dotnet >/dev/null 2>&1; then
  DOTNET="dotnet"
elif [ -x "/c/Program Files/dotnet/dotnet.exe" ]; then
  DOTNET="/c/Program Files/dotnet/dotnet.exe"
else
  echo "ERROR: dotnet SDK not found on PATH or at 'C:\\Program Files\\dotnet'." >&2
  exit 1
fi

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

SEED_ARGS=()
if [ -n "$SEED" ]; then
  SEED_ARGS=(--seed "$SEED")
fi

"$DOTNET" run --project backend/src/Hap.Synth -c Release -- \
  "${SEED_ARGS[@]}" \
  --out "$OUT_DIR/directory.json" \
  --seed-users "$OUT_DIR/seed-users.json"
