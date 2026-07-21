---
id: HAP-19
title: Completion & data-quality reporting, CSV export, downstream read API
epic: E4-harris
wave: 2
fr: [FR-019, FR-038, FR-039, FR-040]
risk: L3                # trigger: completion/participation reads are a read path over Assessments — rounds up
status: todo
estimate: {dev: M, qa: S}
worklog: []
closure: null
---
## Story
As leadership, I see completion trends and per-BU data-quality scores, can export the register to CSV, and downstream tools can consume the register via a read API — closing the reporting loop around both modules.

## Context
- Spec: FR-019 (completion/participation per cycle, trended), FR-038 (data-quality score = update timeliness + value/governance completeness), FR-039 (CSV export), FR-040 (read API — register data only, never assessment data); SC-011.
- Plan: contracts/api.md `GET /api/team|bus/…/completion`, `GET /api/initiatives/export.csv`, `GET /api/reporting/register`; completion facts derive from CycleInvitation + Assessment states — counts only, no scores, but the query touches Assessments so the seam gateway serves it (risk rounds up to L3).
- Files: completion endpoints via `backend/src/Hap.Api/Authorization/` gateway, CSV writer + reporting endpoint (register side, plain), small UI additions to existing dashboard/register screens only if the mockups already show the slots (dashboard completion tile exists from HAP-11 — wire trend view; NO new screens).
- No migration.
- Blocked by: HAP-11, HAP-14
- Parallelisable: yes, with HAP-17 and HAP-18 (disjoint files; touches gateway namespace — Serialise with: none, HAP-17/18 don't touch it)

## Acceptance criteria
- [ ] Completion per team/BU/cycle: invited, submitted, moderated, unmoderated %, contractor-excluded counts — asserted against hand-computed synth values; denominators exclude persons deactivated before close, while a submitted leaver still appears in the submitted/moderated counts (scored population — FR-024/§3.5; test with the synth leaver where the two n's differ; panel B1); trend across closed cycles (FR-019).
- [ ] Completion endpoints return counts only — response-shape test proves no score field exists; scope rules identical to dashboards (role-matrix test, `Category=PrivacyReporting`).
- [ ] Data-quality score per BU (FR-038): 50% update timeliness (% active initiatives current) + 50% field completeness (% required value/governance fields populated) — provisional weights per QUESTIONS.md Q-005 (pending DR), held as configuration not code; computation unit-tested; surfaced on the BU dashboard slot and via API.
- [ ] `GET /api/initiatives/export.csv` streams the register with the list view's current filters applied; columns documented; opens in Excel (CRLF + UTF-8 BOM test).
- [ ] `GET /api/reporting/register` returns register data for authorised callers; a response-shape test proves NO assessment-derived field is reachable from any reporting endpoint (FR-040, `Category=PrivacyReporting`).
- [ ] QA (adversarial, fresh agent — mandatory L3 attempts): (a) as each seeded role attempt individual-level data via completion/reporting endpoints; (b) attempt sub-4 inference from completion counts (completion counts are participation facts, not scores — confirm no score-derived value leaks); (c) recompute one BU's completion % from raw invitation/assessment rows and match; document here.
- [ ] `./scripts/verify.sh` green.

## Attempts / notes

**SPEC AUDIT 2026-07-21 (pre-start edit, story was todo):** data-quality weights fixed at provisional 50/50 (Q-005, pending DR — hold as config); leaver completion-denominator rule added.
**L2 PANEL B1 (same day):** retention guard mirrored from HAP-10 — submitted leavers remain in scored counts; only the completion denominator excludes them.
