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
- **HAP-5 Q-015 — G1 owner decision + witness cases (binding, L3 round-3 correction 2026-07-21):** the visibility seam closed the gross transitive above-BU over-grant, but a documented RESIDUAL remains: an ungranted hierarchy above-BU leader (Portfolio/Group Leader — the generator seeds them no explicit grant) is classified `Manager` and CAN read their IMMEDIATE DIRECT report's individual score. Distinguishing them from an ordinary Manager needs the Q-014 "leads this unit" anchor, so it is not resolvable in code now. **The G1 witness MUST explicitly show, and the owner MUST rule on, the above-BU-hierarchy-leader DIRECT-report read:** the V3 spot-checks must include `HAP-PF-01 → HAP-GRP-01` (Portfolio Leader reading their direct-report Group Leader) and `HAP-GRP-01 → HAP-BUL-01` (Group Leader reading their direct-report BU Lead), which **currently return Allowed** — the owner decides whether that one-hop read is acceptable or must flip to denied once Q-014 supplies the anchor (does an above-BU leader read a direct report's individual score, or aggregates only?). Pinned by `OrgGraphRealDirectoryTests.PINNED_ungranted_above_BU_hierarchy_leader_CAN_read_immediate_direct_report_pending_Q014_G1`. See QUESTIONS.md Q-015 ruling + round-3 correction.

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
