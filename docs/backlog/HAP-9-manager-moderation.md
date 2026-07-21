---
id: HAP-9
title: Manager moderation — review queue, divergence rules, transparency (assessment-moderation.html)
epic: E2-assessment
wave: 1
fr: [FR-008, FR-009, FR-010, FR-011, FR-012, FR-063, FR-069]
risk: L3                # trigger: read path over Assessments/AssessmentScores + individual-view audit writes
status: todo
estimate: {dev: L, qa: M}
worklog: []
closure: null
---
## Story
As a Manager, I review each direct report's self-scores and evidence side by side, adopt or diverge (comment forced at Δ≥2, carry-forward default), producing the moderated score of record — and my report sees exactly what I decided and why.

## Context
- Spec: "Module 1: Assessment Scoring Workflow" FR-008..FR-012, FR-063 (carry-forward default); User Story 2 scenarios; calibration delta (FR-011) is computed data surfaced later (HAP-11) — this story records both scores.
- Plan: contracts/api.md "Manager scope" (`GET /api/team/reviews`, `GET /api/team/members/{id}/assessment` **[A]**, `PUT /api/team/reviews/{id}`), audit fails closed (research D1); data-model.md AssessmentScore invariant (**comment required when |self − manager| ≥ 2**).
- Mockup: `docs/design/mockups/assessment-moderation.html` — binding incl. the **Δ≥2 forced-comment state** and carry-forward defaults shown. Components (A8): **DivergenceFlag**, **ComparisonRow**; A4 tables/forms.
- Files: seam gateway + audit hook in `backend/src/Hap.Api/Authorization/`, manager endpoints, `app/src/screens/assessment-moderation/**`, `app/src/components/{DivergenceFlag,ComparisonRow}/**`. Individual result view (`GET /api/me/assessment/result`) + its screen section per FR-012.
- No migration (tables exist from HAP-8).
- **HAP-7 handoff — the post-close submission lock (Q-017a, binding):** a manager's moderation write is a submission-class write. It MUST consult `Cycle.AllowsSubmission(hasLateOverride)` (+ `CycleService.HasLateOverrideAsync`) and reject a post-close moderation (423/409) unless a late override exists — the same lock the self-assessment path uses (HAP-8). Post-close rejection exists only if every submission-class write path checks the primitive.
- Blocked by: HAP-8
- Parallelisable: no

## Acceptance criteria
- [ ] `GET /api/team/reviews` lists exactly the caller's active direct reports' assessments with states and on_leave flags (FR-069 flag display only; close-out behaviour is HAP-10); non-managers get 403/empty per contract.
- [ ] `GET /api/team/members/{id}/assessment` returns self scores + evidence for own directs; writes exactly one `IndividualView` AuditLog row per call, and audit failure fails the request (`Category=PrivacyReporting`).
- [ ] `PUT /api/team/reviews/{id}`: defaults adopt self-score; where prior-cycle moderated score exists and self-score unchanged, default is carry-forward (FR-063 test); Δ≥2 without comment → 422 (FR-009); success transitions Submitted→Moderated with moderated_by recorded.
- [ ] Moderated score is the score of record: both self and manager scores persist per dimension (FR-010/011 row-shape test).
- [ ] After moderation, `GET /api/me/assessment/result` (as the individual) returns per-dimension manager scores, comments, divergence values (FR-012); 404 before moderation.
- [ ] UI implements the mockup: review queue, ComparisonRow per dimension (self vs manager, both values printed), DivergenceFlag at Δ≥1, forced-comment field error state at Δ≥2, calibration delta line, carry-forward defaults pre-filled.
- [ ] vitest-axe passes; strings externalised; tokens only.
- [ ] QA (adversarial, fresh agent — mandatory L3 attempts): as each seeded role, attempt to read an assessment of a person OUTSIDE the caller's chain via manager endpoints (404, no audit row leak — `Category=PrivacyReporting`); attempt moderation of a non-direct (rejected); verify every successful individual view produced exactly one audit row (count test); document here.
- [ ] `./scripts/verify.sh` green.

## Attempts / notes

**SPEC AUDIT 2026-07-21 (pre-start edit, story was todo):** FR-069 added to frontmatter — this story ships the leave-status *display* (FR-069); HAP-10 also cites FR-069 for leave handling at close, while departure *escalation* is FR-070 (intentional dual citation for change-log traceability; panel nit A5).
