# DR-0007 — Cost rows are written per agent on completion; the lead's row at session end

**Date:** 2026-07-21 · **Status:** Superseded (mechanism) by [DR-0008](DR-0008-cost-attribution-sessionend-batch.md) 2026-07-22 — the per-story + session-lead *intent* stands; the per-`SubagentStop` *mechanism* below did not work at runtime (`transcript_path` is the shared main transcript, not the subagent's), producing 1,625 duplicate zero-cost rows. Read DR-0008 for the mechanism now in force. · **Scope:** Constitution "The join" + Art. (money mechanics) (PATCH: 1.2.0 → 1.2.1) + CLAUDE.md §§4, 12 + `scripts/cost_log.py` + `.claude/settings.json`
**Story key:** n/a (owner-directed tooling amendment) · **Origin:** owner request 2026-07-21

## Context

The cost log was written by a single `SessionEnd` hook that summed the transcript and tagged the row by the *current* git branch. The session lead runs on `main`, so that one row was always `branch=main, story=untagged` — and the real per-story spend, which happens inside each teammate's own worktree/branch, was never captured at all. Tagging "via the branch name" only works when the writer is *on* the story branch, which the lead never is. The branch-per-story join (constitution "The join") therefore produced no per-story cost data.

## Decision

Cost attribution moves to **agent-completion granularity**:

1. A **`SubagentStop`** hook writes **one row per teammate/subagent completion**, tagged to that agent's story (`HAP-<n>`), derived from the worktree-branch pattern dominating its transcript (falling back to the dominant story key, then the cwd branch, then `untagged`).
2. The existing **`SessionEnd`** hook writes **one row for the session lead** (the main orchestration loop), tagged `story = session-lead` — the lead's spend is orchestration cost, not one story's.
3. The same script (`scripts/cost_log.py`) serves both events, branching on the payload's `hook_event_name`. The CSV schema is unchanged (7 columns); lead rows are distinguished by `story = session-lead`. The CSV stays gitignored and never hand-edited; the hook still exits 0 on every failure path so it can never block a stop or session end.

## Consequences

- `telemetry.sh`'s worklog↔cost join now has real per-story cost rows to join against; filter `story = session-lead` to separate orchestration overhead from per-story delivery cost.
- Constitution "The join" and the money-mechanics clause are updated to describe per-agent-completion + session-lead writes (was "extracts HAP-N from the branch … per session"). Version 1.2.1 (PATCH — telemetry-mechanism refinement, no article added/removed). CLAUDE.md §4 and §12 updated in the same commit.
- **Validation status:** the script's story-attribution and row-writing logic were unit-tested against synthetic `SubagentStop` and `SessionEnd` payloads before this commit. Whether `SubagentStop` fires for this session's teammates, and whether its `transcript_path` is the subagent's own transcript, are runtime facts confirmed empirically against the first teammate completion after this commit; if the runtime differs, the fallback chain still writes a best-effort row and the wiring is adjusted as a follow-up (the CSV is gitignored calibration data, so an imperfect row is never load-bearing).

## Supersedes

Nothing. Refines the telemetry mechanism established at ratification.
