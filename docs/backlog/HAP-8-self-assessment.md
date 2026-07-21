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
- **HAP-5 Q-015 hard-block — G1 precondition (binding, L3 ruling 2026-07-21, corrected round 3):** the cross-person individual-read cap is only PARTLY closed. **Closed:** the gross TRANSITIVE/subtree above-BU over-grant (a leader reading individuals several hops down). **NOT closed (residual):** an ungranted hierarchy above-BU leader (Portfolio/Group Leader — no explicit grant) is classified `Manager` and CAN read their IMMEDIATE DIRECT report's individual score (Group Leader → direct-report BU Lead; Portfolio Leader → direct-report Group Leader) — telling them apart from an ordinary Manager needs the Q-014 anchor. Whether clause-2 should deny that one-hop read is an **unresolved G1 OWNER DECISION**. BU-wide reads require an explicit `BuDelegate` grant or a ratified Q-014 anchor; other transitive reads fail closed. **This story's `/api/me/assessment*` endpoints are SELF-scope (caller == subject, always permitted) and are NOT blocked.** But **no story may wire a live CROSS-PERSON individual-read endpoint (manager/BU-lead/leader reads) until the BU-tier cap lands (Q-014/Q-015), and G1 cannot certify individual-score access until the owner rules on the above-BU-hierarchy-leader direct-report read.** See QUESTIONS.md Q-015 ruling + round-3 correction.

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
