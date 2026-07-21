# DR-0003 — As-built wiki and user documentation become mandated surfaces

**Date:** 2026-07-21 · **Status:** Accepted · **Scope:** Constitution Art. I & IV (MINOR: 1.1.2 → 1.2.0) + CLAUDE.md §§2, 10 + spec 001 (FR-072/FR-073)
**Story key:** n/a (governance amendment, pre-backlog)

## Context

The contract made documentation *agent work by default* (Art. III.1) but mandated no documentation deliverables. Nothing required as-built system documentation — after shipping, the only descriptions of the system would be the spec (intent, drifts from reality) and git history. And no FR anywhere required user documentation, despite the application being mandatory monthly use for every employee across ~23 BUs.

## Decision

1. **As-built wiki** — `docs/wiki/`, one page per subsystem (seam, org sync, cycles, scoring, register, submissions, notifications). New Art. I surface: authoritative for **HOW IT WORKS, AS BUILT**. Scoped as a *derived* surface — it explains shipped behaviour and never restates spec (WHAT/WHY), backlog (status), or decision records (why decided), preserving Art. I's one-truth-per-surface rule. Updated in the story closure commit (Art. IV Phase 4 / CLAUDE.md §10.2); a stale page is drift, caught by the same discipline as the change log.
2. **User documentation, both forms** — (a) in-app contextual help for the mandated flows, stored as versioned seeded content per the data-not-code rule (spec **FR-072**); (b) a printable end-user guide at `docs/user-guide/` (spec **FR-073**), updated at closure of any story changing user-facing behaviour. New Art. I surface: **HOW USERS OPERATE IT**.
3. Delivery: each UI story authors its screen's help content (L1); story **HAP-21** (plan 001, Wave 2) assembles the in-app help surface and the user-guide baseline.

## Consequences

- Constitution 1.2.0 (MINOR — material expansion of Art. I); CLAUDE.md repo map and closure step updated in the same commit per Governance.
- Closure now has five record-keeping elements before cleanup (story file, change-log row, wiki, user guide, board) — the four-box checklist wording is unchanged; wiki/user-guide fall under box 2's "record before cleanup" commit.
- Spec 001 gains FR-072/FR-073 (append-only numbering per DR-0002); plan 001 gains HAP-21.

## Supersedes

Nothing; extends constitution v1.1.2. Complements [DR-0001](DR-0001-risk-table-ordering.md), [DR-0002](DR-0002-spec-kit-layout.md).
