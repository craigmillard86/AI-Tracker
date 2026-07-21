#!/usr/bin/env bash
# Joins measured worklogs (story frontmatter) with the session cost log
# (.claude/cost-log.csv, gitignored) → per-story minutes, tokens, $.
# Three signals stay separate; gaps are calibration data (CLAUDE.md §12).
set -euo pipefail
cd "$(dirname "$0")/.."

costlog=.claude/cost-log.csv

printf '%-10s %-9s %-9s %-12s %-12s %-10s\n' STORY DEV_MINS QA_MINS IN_TOKENS OUT_TOKENS COST_USD
for f in $(ls docs/backlog/HAP-*.md 2>/dev/null | sort -t- -k2 -n); do
  id=$(awk -F': ' '$1=="id" {print $2; exit}' "$f")
  dev=$(awk '/^  - \{phase: dev/ {if (match($0,/mins: [0-9]+/)) s+=substr($0,RSTART+6,RLENGTH-6)} END{print s+0}' "$f")
  qa=$(awk '/^  - \{phase: qa/ {if (match($0,/mins: [0-9]+/)) s+=substr($0,RSTART+6,RLENGTH-6)} END{print s+0}' "$f")
  if [ -f "$costlog" ]; then
    read -r tin tout cost < <(awk -F',' -v s="$id" 'NR>1 && $4==s {i+=$5; o+=$6; c+=$7} END{printf "%d %d %.4f", i+0, o+0, c+0}' "$costlog")
  else
    tin=0; tout=0; cost=0
  fi
  printf '%-10s %-9s %-9s %-12s %-12s %-10s\n' "$id" "$dev" "$qa" "$tin" "$tout" "$cost"
done

[ -f "$costlog" ] || echo "note: $costlog not found — no sessions logged yet (hook writes it at session end)"
