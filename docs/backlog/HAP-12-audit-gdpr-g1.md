---
id: HAP-12
title: GATE G1 readiness — audit completeness, right-of-access export, retention
epic: E2-assessment
wave: 1
fr: [FR-050, FR-051, FR-052, FR-053]
risk: L3                # trigger: audit-log write/read paths + GDPR retention/erasure/export
status: todo
estimate: {dev: M, qa: S}
worklog: []
closure: null
---
## Story
As the platform owner, every individual-level view is provably audited, any employee can receive a full export of their data, and raw scores older than the retention period are erased on schedule — completing the evidence set for the human-witnessed G1 privacy gate.

**GATE: closing this story completes G1 readiness (constitution Art. VII). Closure notes MUST say so prominently. The story flags the gate; only the owner passes it, witnessing quickstart.md "V3 — Privacy spot-checks" across all seven roles.**

## Context
- Spec: "Data Management & Audit" FR-050 (audit completeness incl. who-viewed), FR-051 (right-of-access export), FR-052 (3y raw / indefinite aggregates), FR-053 (immutability); SC-005 (zero reads outside chain, all views logged).
- Plan: data-model.md "Audit & GDPR" (retention = erasure job over old AssessmentScore values + `RetentionErasure` audit rows; no new tables); contracts/api.md `GET /api/me/export`, `[PA] GET /api/admin/audit`, `[PA] POST /api/admin/retention/run`; quickstart.md "V3" + "Gate readiness".
- Files: `backend/src/Hap.Infrastructure/Audit/**` (reader/search), export + retention endpoints, retention job service. No migration.
- Blocked by: HAP-11
- Parallelisable: yes, with HAP-13 (disjoint files; HAP-13 owns the next migration-chain slot)

## Acceptance criteria
- [ ] Audit completeness sweep (`Category=PrivacyReporting`): an integration test walks every [A]-marked endpoint from contracts/api.md as an authorised caller and asserts exactly one IndividualView row per call with correct actor/subject; a second sweep as unauthorised callers asserts zero audit rows and zero data.
- [ ] `GET /api/admin/audit?subject=&action=&from=` returns filtered audit rows for Platform Admin only; no mutation endpoint exists (route-table assertion).
- [ ] `GET /api/me/export` returns every datum held about the caller — profile, org links, all cycles' self+manager scores, evidence, comments, moderation metadata — validated against a hand-assembled expected export for one synth user (FR-051); the export itself writes an `Export` audit row.
- [ ] `POST /api/admin/retention/run`: raw self/manager score values and evidence older than 3 years are nulled (rows retained for aggregate integrity), one `RetentionErasure` audit row per affected assessment; snapshots untouched (FR-052 test with back-dated synth cycle).
- [ ] Retention is idempotent: second run affects zero rows (test).
- [ ] quickstart.md V3 script executes clean end-to-end on the synth stack (the G1 rehearsal); its steps are automated as an integration test suite where feasible and the remainder documented as the witnessed-run script.
- [ ] Closure notes flag G1 readiness prominently and list the evidence (suite names, V3 doc).
- [ ] Wiki + guide (DR-0003): create `docs/wiki/audit-and-gdpr.md`; no user-guide page (admin-facing).
- [ ] `./scripts/verify.sh` green.

## Attempts / notes
