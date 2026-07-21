---
id: HAP-8
title: Self-assessment — API + UI (assessment-self.html)
epic: E2-assessment
wave: 1
fr: [FR-007, FR-062, FR-066]
risk: L3                # trigger: read/write path over Assessments/AssessmentScores tables
status: todo
estimate: {dev: L, qa: M}
worklog: []
closure: null
---
## Story
As an Individual in an open cycle, I complete my monthly self-assessment — one dimension per section with level descriptors inline, pre-populated from last cycle, optional evidence — under an explicit "development, not performance management" statement, so a no-change month takes seconds and my data enters the system only through the seam.

## Context
- Spec: "Module 1: Assessment Scoring Workflow" FR-007; FR-062 (pre-population); FR-066 (purpose limitation in-app); User Story 1 acceptance scenarios; SC-007 (WCAG 2.2 AA on this flow).
- Plan: data-model.md Assessment + AssessmentScore (**EF migration #4** — includes the `unmoderated` flag for HAP-10); contracts/api.md "Self scope" (`GET/PUT /api/me/assessment*`, `POST …/submit`; self-view not audited); research D1 (all queries via `Hap.Api/Authorization/AssessmentReads`).
- Mockup: `docs/design/mockups/assessment-self.html` — binding for layout/IA/states incl. the **incomplete state (5 of 7 dimensions, projected floor L0)**. Components (DESIGN.md A8): **LevelSelectorCard**, **ProgressStepper**, **PurposeBanner**; A4 forms; A1 app type roles.
- Files: seam gateway extensions in `backend/src/Hap.Api/Authorization/`, endpoints, `app/src/screens/assessment-self/**`, `app/src/components/{LevelSelectorCard,ProgressStepper,PurposeBanner}/**`.
- **Serialise with: HAP-7 (migration chain).**
- Blocked by: HAP-5, HAP-7
- Parallelisable: no
- **HAP-5 handoff — type relocation (do this here):** HAP-5 defined `Assessment`/`AssessmentScore` as seam-internal types *inside* `Hap.Api.Authorization` (no DbSet, no migration — registering a DbSet without its migration fails verify's idempotence gate). This story adds migration #4 + the DbSets, so it must **relocate those types to `Hap.Domain`** (so `HapDbContext` in `Hap.Infrastructure` can register them without a layer inversion) and **extend the seam-boundary guard** (`Hap.Architecture.Tests/SeamBoundaryTests`) to the DbSet form + the new domain definition folder allowlist. The `IAssessmentStore` port lives in the seam; implement it here against the DbSets — it is the ONLY type that touches the `Assessments`/`AssessmentScores` DbSets.
- **HAP-5 Q-015 — RESOLVED, block LIFTED (owner ruling 2026-07-21, [DR-0005](../decisions/DR-0005-above-bu-direct-report-read.md)):** the owner ratified **ALLOW** for the one-hop above-BU direct read — a direct line-manager reads their immediate direct report's individual score regardless of tier (moderation); transitive/subtree reads stay denied; the broad above-BU view stays aggregates-only. The seam already implements this, so **cross-person individual-read endpoints MAY now be wired through the seam** (the prior hard-block is lifted for the synthetic build). Self-scope `/api/me/assessment*` was never blocked.
  - **Task inherited by this story (it relocates the Assessment types + touches the seam):** reframe `OrgGraphRealDirectoryTests.PINNED_ungranted_above_BU_hierarchy_leader_CAN_read_immediate_direct_report_pending_Q014_G1` — rename/comment it from "pending Q-014 / flips to deny" to "**ratified per DR-0005**" (the assertion is unchanged; it now documents intended behaviour, not a residual). This is an L3 seam-test change — it rides this story's L3 panel.
  - **Real-data caveat (Q-014 deferred, not a blocker):** on real org shapes, a BU Lead's BU-wide individual read needs an explicit `BuDelegate` grant or the Q-014 anchor; without it they are treated as an ordinary Manager (direct reports only, fail-closed under-grant). Fine for the synthetic build. See QUESTIONS.md Q-014.

## Acceptance criteria
- [ ] `GET /api/me/assessment` returns the 7 dimensions + descriptors from framework data (no hard-coded content), prior-cycle scores pre-populated when they exist (FR-062 test: cycle N+1 shows cycle N values), and the purpose-limitation copy key (FR-066).
- [ ] `PUT …/scores` upserts partial progress (0–3 validated per dimension); reopening restores in-progress values (User Story 1 scenario 3 test); `POST …/submit` transitions InProgress→Submitted; writes after submit → 409.
- [ ] All data access goes through the seam gateway — architecture test still green; every new query lives in `Hap.Api/Authorization` (`Category=PrivacyReporting`).
- [ ] UI implements the mockup: one dimension per section, four LevelSelectorCards with descriptor text, ProgressStepper showing "x of 7" + projected floor, PurposeBanner visible without scrolling on the first section, evidence textarea per dimension, save + submit buttons per A4 (one primary).
- [ ] Mockup non-happy state: with 5 of 7 dimensions scored, ProgressStepper shows 5/7 and projected floor L0 (component test).
- [ ] vitest-axe passes on the full flow; keyboard-only completion possible (radio-group semantics on LevelSelectorCard) — SC-007.
- [ ] Strings externalised; tokens only from tokens.css.
- [ ] QA (adversarial, fresh agent — mandatory L3 attempts): as each of the seven seeded roles, attempt `GET/PUT` of ANOTHER person's assessment via the self endpoints (must be impossible — 404/401, `Category=PrivacyReporting`); verify self-view writes no IndividualView audit row but the data path is seam-only; document attempts here.
- [ ] `./scripts/verify.sh` green.

## Attempts / notes
