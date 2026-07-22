# DR-0008 — Cost attribution is a SessionEnd batch over per-agent transcripts

**Date:** 2026-07-22 · **Status:** Accepted · **Scope:** Constitution "The join" + Art. (money mechanics) (PATCH: 1.2.1 → 1.2.2) + CLAUDE.md §§4, 12 + `scripts/cost_log.py` + `.claude/settings.json`
**Story key:** n/a (owner-directed tooling amendment) · **Origin:** owner observation 2026-07-22 (cost log had duplicate rows) · **Supersedes:** the *mechanism* of DR-0007 (the per-story/session-lead intent stands)

## Context

DR-0007 moved cost attribution to a **`SubagentStop`** hook that was to write one row per teammate completion, tagged to that agent's story via its worktree branch. DR-0007's own "Validation status" flagged the load-bearing assumption as unconfirmed: *"whether `SubagentStop`'s `transcript_path` is the subagent's own transcript … confirmed empirically … if the runtime differs … adjusted as a follow-up."* The runtime differs. Observed on 2026-07-22:

- On `SubagentStop`, `transcript_path` is the **shared main-session transcript**, and `cwd` is the **main repo**, not the stopped agent's worktree. The hook therefore has no view of a single agent's isolated spend or its branch.
- Result: every `SubagentStop` re-summed the whole session and appended a row → **1,625 near-duplicate rows**, all tagged to one *dominant* story key (1,428 to `HAP-3`), with **`cost_usd` always `0.0000`** (the transcript records carry no `costUSD` field). The per-story cost signal was unusable.

This is that follow-up. It keeps DR-0007's *goal* (per-story cost + a separate session-lead row) and replaces only the *mechanism* with one that is actually measurable.

## Decision

Cost attribution becomes a **single `SessionEnd` batch pass** (the `SubagentStop` wiring is removed):

1. Per-agent transcripts are read **at rest**. Claude Code writes each subagent's full transcript to `<project>/<session_id>/subagents/agent-<id>.jsonl` (with `usage` and the story text). At session end the hook scans each, sums usage, and tags it by the **`HAP-<n>` key dominating its own transcript** (branch column takes the dominant full `HAP-n-fr-x-slug` when present). Rows are grouped → **one row per story**.
2. The **main** transcript is summed into a single **`session-lead`** row (`branch = main`) — the orchestration loop's own spend.
3. **Cost is computed**, per usage record, from a maintained `$/MTok` rate table keyed on the record's own `model` (so the lead's mixed fable+opus transcript is priced correctly). Priced token classes: input, output, cache-creation, cache-read. The token *columns* show input+cache-creation and output (new context); cache-read is priced into cost but not shown as a token count — matching the prior column semantics. Costs are **estimates** for internal calibration, never billed figures; the rate table is updated when published rates change.
4. The 7-column CSV schema is **unchanged** (`timestamp,session_id,branch,story,input_tokens,output_tokens,cost_usd`) so `scripts/telemetry.sh` keeps joining on `story` (column 4). The CSV stays gitignored, is written **once at session end**, and is never hand-edited. The hook still exits 0 on every failure path.

The 1,625 corrupt rows written under the DR-0007 mechanism are **purged** to the header (removing provably-broken telemetry is not the "smoothing over gaps" §13 forbids — no valid history is lost).

## Consequences

- `telemetry.sh`'s worklog↔cost join now has one honest per-story row per session to sum, plus `story = session-lead` for orchestration overhead — exactly the split DR-0007 intended, now with real numbers.
- Attribution is finalised **at session end**, not incrementally per completion. Acceptable: the CSV is calibration data, not a live dashboard.
- Tagging is heuristic (dominant `HAP-<n>` key in the agent's own transcript). An agent that legitimately spans stories (e.g. the Phase-0 drift sweep) is attributed to the story it referenced most; this is minor overhead noise, not load-bearing.
- Validated before commit against this session's real transcripts: one row per story HAP-1…HAP-9 + a session-lead row, sensible nonzero costs, zero duplicates — the validation DR-0007 lacked.
- Constitution "The join" and the money-mechanics clause are reworded from "one row per agent on completion (via the branch)" to "one row per story at session end (from per-agent transcripts) plus a session-lead row." Version 1.2.2 (PATCH — telemetry-mechanism refinement, no article added/removed). CLAUDE.md §4 and §12 updated in the same commit.

## Supersedes

The **mechanism** of DR-0007 (per-`SubagentStop` incremental writes tagged via the writer's branch). DR-0007's per-story + session-lead *intent* is retained and realised here. DR-0007 is marked `Superseded (mechanism) by DR-0008`.
