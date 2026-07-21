---
id: HAP-11
title: Rollups & BU dashboard (dashboard-bu.html)
epic: E2-assessment
wave: 1
fr: [FR-013, FR-015, FR-016, FR-017, FR-018, FR-019, FR-041]
risk: L3                # trigger: aggregate read paths over AssessmentScores via the seam
status: todo
estimate: {dev: L, qa: M}
worklog: []
closure: null
---
## Story
As a BU Lead (and Group/Portfolio/Executive at their scopes), I see moderated maturity rolled up per dimension — mean, floor distribution, trend, completion — with suppression rendered honestly, so leadership reads real signal without ever being able to infer an individual's score.

## Context
- Spec: "Module 1: Rollups & Analytics" FR-013, FR-015..019; FR-041 (cross-module counts — initiative counts render "—" until HAP-13 ships; leave a stub slot per plan); User Story 3 + 5 scenarios; SC-006, SC-008 (<5s dashboards, <30s rollups).
- Plan: research D4 (open cycle computed live via seam; closed cycles read snapshots); contracts/api.md "BU Lead scope" `GET /api/bus/{buId}/dashboard` **[S]**, "Group/Portfolio/Executive" `GET /api/org/{nodeType}/{nodeId}/rollup` **[S]**, `GET /api/me/team/summary` **[S]**.
- Mockup: `docs/design/mockups/dashboard-bu.html` — binding incl. **suppressed aggregate state** ("— (group too small, n=3 < 4)"), **overdue-updates callout (3 flagged — stub until HAP-13/14)**, and the **maturity-gap callout** (weakest dimension with zero initiatives — initiative part stubbed). Components (A8): **StatTile**, **DimensionBar**, **TrendSparkline**, **SuppressedCell**; A2 maturity ramp; A4 cards.
- Files: rollup read endpoints via `backend/src/Hap.Api/Authorization/` gateway, `app/src/screens/dashboard-bu/**`, `app/src/components/{StatTile,DimensionBar,TrendSparkline,SuppressedCell}/**`.
- No migration.
- Blocked by: HAP-10
- Parallelisable: no

## Acceptance criteria
- [ ] Dashboard endpoint returns per-dimension mean + floor distribution + completion % + unmoderated % for the caller's BU; closed cycles from snapshots, open cycle live via seam (test both paths agree for a just-closed cycle).
- [ ] Trend series returns per-dimension means across all closed cycles (FR-017); distribution view returns floor-level histogram per dimension (FR-018).
- [ ] Scope enforcement: BU Lead of BU-A requesting BU-B's dashboard → 404; Group Leader gets `org/…/rollup` for own group only; Executive gets AllHig (role-matrix test, `Category=PrivacyReporting`).
- [ ] Suppression: engineered synth nodes (n=3 team, sub-4 BU, complement cases) return `{suppressed, reason}`; UI renders SuppressedCell with reason text, never zero/blank (FR-071; mockup state).
- [ ] UI implements the mockup: floor level + mean StatTiles with trend arrows, seven DimensionBars with values printed at bar end, TrendSparkline per A8 (aria-hidden, values in adjacent text), completion tile, stubbed initiative-count slots labelled per mockup.
- [ ] Maturity ramp colours from tokens.css only; every level badge prints the level number (A2 rule); vitest-axe passes.
- [ ] Dashboard responds < 5s against full synth data (integration timing assertion, generous bound in CI: < 5000ms).
- [ ] QA (adversarial, fresh agent — mandatory L3 attempts): (a) as each seeded role attempt individual-score access via every dashboard/rollup endpoint (must be impossible); (b) attempt sub-4 aggregate via team-summary + BU + org rollups incl. differencing pairs; (c) recompute one BU's mean from raw moderated scores and compare to the dashboard figure (must match exactly); all documented here (`Category=PrivacyReporting`).
- [ ] `./scripts/verify.sh` green.

## Attempts / notes
