---
id: HAP-11
title: Rollups & BU dashboard (dashboard-bu.html)
epic: E2-assessment
wave: 1
fr: [FR-013, FR-015, FR-016, FR-017, FR-018, FR-019, FR-041]
risk: L3                # trigger: aggregate read paths over AssessmentScores via the seam
status: done
estimate: {dev: L, qa: M}
worklog:
  - {phase: dev, start: 2026-07-22T14:43:56Z, end: 2026-07-22T17:12:53Z, mins: 148}
  - {phase: qa, start: 2026-07-22T17:15:01Z, end: 2026-07-22T17:47:39Z, mins: 32}
closure:
  sha: 762d879
  date: 2026-07-22
  risk: L3
  files: 37  # seam rollup reads (RollupReads, RollupPipeline, HierarchySuppression, RollupEndpoints), CycleCloseProcessor refactor, dashboard UI (StatTile/DimensionBar/TrendSparkline/SuppressedCell + DashboardScreen), tests
  tests: backend 254+ (Category=PrivacyReporting incl. hierarchy-suppression fuzz + exact-oracle); frontend 138; no migration
  panel: [hap-code-reviewer, hap-domain-specialist, hap-red-team, hap-design-reviewer]  # L3+design; 2 rounds, clean at c56fdee
  qa: hap-qa PASS — differencing attacked on the REAL canonical directory (no leak), recompute-vs-dashboard matched 9dp (non-uniform fixture), SC-006 live path @~10k ≈186ms
  decisions: [Q-024 aggregate scope via FR-022 anchors (G1-ratify), Q-026 hierarchy-global suppression (no DR — hardens D2)]
  g1_residuals: "cross-cycle trend differencing; inherent k=4 floor; laminar-partition assumption — owner ratification at G1"
  open: "Q-025 (FR-018 per-dimension histogram) — owner ruling, provisional-in-effect; F2 carry-forward from HAP-10 now CLOSED (structural seam-boundary guard)"
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

### Attempt 1 (dev, 2026-07-22) — dev-hap11
No prior attempts (`git log --all --grep HAP-11` shows only the HAP-10 commits it depends on).

**Risk L3 confirmed.** Trigger (CLAUDE.md §7): new **aggregate read paths over `AssessmentScores` via the seam** — the live open-cycle dashboard computes rollups directly from moderated scores inside `Hap.Api/Authorization`. Also touches N<4/complement suppression on a read path and the frozen `RollupSnapshot` read. Panel: `hap-code-reviewer` + `hap-domain-specialist` + `hap-red-team` (+ `hap-design-reviewer` for the UI).

**Design decisions taken:**
- **Single computation, dual entry (research D4, "reuse don't fork"):** extracted the node-build + `RollupComputation` + `SuppressionEvaluator` steps out of `CycleCloseProcessor` into a shared seam pipeline (`RollupPipeline`). Close = auto-adopt (mutate) → pipeline → persist snapshots; live open-cycle dashboard = pipeline (no auto-adopt, no persist) → project. Because the pipeline is identical, live and snapshot agree by construction for a just-closed cycle (tested). Existing HAP-10 CycleClose tests are the regression net for the refactor.
- **F2 (central privacy req, closes HAP-10 carry-forward):** all aggregate reads project via a discriminated `AggregateReadResult` (`Published(figures)` | `Suppressed(reason)`). A suppressed node's figures are NEVER read into the DTO — an architecture/guard test asserts no dashboard/rollup/trend response can emit N/mean/floor-distribution for a suppressed node. The snapshot's public DbSet is only ever consumed through this projection.

**PROVISIONAL DECISION — flagged (Q-024, see docs/decisions/QUESTIONS.md):** aggregate-read **scope** for Group/Portfolio/Executive is resolved from the hierarchy anchors (`HierarchyRoleResolver.GroupLeaderOfGroupId` / `PortfolioLeaderOfPortfolioId` / `BuLeadOfBusinessUnitId`) per **FR-022** (leadership visibility derived from org hierarchy), plus explicit grants (`HigExecutive` → AllHig, `BuDelegate(bu)` → that BU). This deliberately consumes the hierarchy tier labels that the *individual-read* seam forbids (Q-014). Justification: (a) FR-022 mandates hierarchy-derived aggregate visibility; (b) exposure is bounded — aggregates are N<4 + complement suppressed, so no individual score is inferable even on a mis-scoped node; (c) the individual-read gate (`AssessmentReads`) is untouched and remains grant/structural-only. The residual risk is that `HierarchyRoleResolver`'s uniform-depth limitation could mis-scope a leader to a *sibling* group's (suppressed) aggregates — a confidentiality, not an individual-privacy, concern. **Owner ratification requested at G1.** If ruled restrictive, group/portfolio aggregate scope falls back to explicit grants only.

**INTERPRETATION — flagged (Q-025):** AC line "distribution view returns floor-level histogram per dimension (FR-018)" reconciled against what `RollupSnapshot` actually stores (per-dimension **mean** + one **node-level** floor-level distribution) and the binding mockup (per-dimension mean bars + a node-level floor-distribution KPI). Delivered: per-dimension means (FR-015) **and** the node's floor-level histogram (FR-016/018, count of people by floor level). A per-dimension level *histogram* is not stored and would not be reconcilable across the live/snapshot dual path, so it is not produced; the per-dimension "level" chip in the mockup is `round(mean)` computed in the UI. Provisional pending owner confirmation.

### Panel round 1 (2026-07-22) — verdicts
- **hap-design-reviewer: CONFORMS** (tokens / maturity ramp / colour-independence / focus / mockup layout).
- **hap-domain-specialist: CHANGES-REQUIRED** — BB1 (floor KPI uses round(mean), not FR-016 floor distribution) + BB2 (BU/org dashboard unreachable for its persona; §8.2).
- **hap-code-reviewer: CHANGES-REQUIRED** — BB2 + BB3 (F2 is convention, not structurally guarded).
- **hap-red-team: VIOLATION FOUND / G1 BLOCKED** — BR1 cross-level differencing: a node suppressed high in the tree is recoverable by summing PUBLISHED nodes lower down (`AllHig(14) − GroupA(11) = 3` recovers the suppressed sub-4 branch's N and per-dimension mean). Suppression was enforced one parent-child level at a time only. **Sacred (Art. VI, G1 M1).**

### Round-2 fix pass (2026-07-22) — dev-hap11
All four blocking + should-fixes; full panel + red-team re-attack to follow (BR1 touches the sacred close/suppression path).
- **BR1 (sacred):** suppression is now **hierarchy-global**. New `HierarchySuppression.Close` runs after the per-parent pass inside the shared `RollupPipeline` (binds BOTH live reads and the frozen close snapshot): (a) equal-membership collapse — single-child/no-slack chains share membership, so if any node on a chain is suppressed the whole chain is; (b) a fixpoint that models the tree's `parent = Σchildren + teamless-slack` identities (teamless = an always-unknown phantom), detects any suppressed node whose count is *determined* by the published set via those identities, and suppresses an additional (smallest-count) published node until nothing suppressed is recoverable. Protecting the count-linear-system also protects the mean (same known-set + structure). Red-team's `Executive_cannot_recover_a_suppressed_sub4_branch_by_differencing` + general multi-child/chain/trend cases added. It lives in HAP-10's shared evaluator so it **strengthens** HAP-10 verdicts — the HAP-10 CycleClose fixture has no cross-level leak (its two sibling suppressed teams share a parent with two unknowns), so its asserted verdicts stay unchanged; the strengthened rule is documented in docs/wiki/cycles-and-assessment.md. No new DR: this *hardens* research-D2's stated goal (close the differencing attack), it does not change its semantics.
- **BB1:** floor KPI derived from `figures.floorLevelDistribution` (%-at-L0 / %-at-L1+ per the mockup), not round(mean); the previously-dead `floorDistributionLabel/Entry` strings wired; half-L0 team test added.
- **BB2:** role-based default node — BU Lead → own BU (`fetchBuDashboard`), Group/Portfolio Leader → own node, Executive → AllHig (`fetchOrgRollup`), plain manager/individual → team summary; new `GET /api/me/dashboard` returns the caller's default node so the router-less shell reaches the persona's scope.
- **BB3:** `SeamBoundaryTests` extended — any `RollupSnapshots` / `Set<RollupSnapshot>` query surface outside the Authorization seam / Domain.Rollups / EF config / Migrations fails the build (structural F2, mirroring the assessment-table guard).
- Should-fixes: live-read `AsNoTracking`; AllHig-scoped request for a nonexistent BU id → 404 (existence check); GroupViewer grant wiring resolved (Q-024); design CTA + maturity-badge naming + gap-callout glyph; trend duplicate-current cleanup; explicit note that the suppressed cell intentionally omits the mockup's literal "n=3 < 4" count (F2 overrides that mockup detail).

### Panel round 2 (2026-07-22) — verdicts
- **hap-domain-specialist: SIGN-OFF.**
- **hap-red-team: SIGN-OFF — NO PATH FOUND.** Verified the greedy fixpoint against an independent EXACT linear-algebra solver (null-space / rational row-reduction over `parent = Σchildren + phantom`) across ~900k random trees — the guarantee holds; the greedy break-choice never under-suppresses on the fuzzed space.
- **hap-code-reviewer: APPROVED.**
- **hap-design-reviewer: CHANGES-REQUIRED (trivial)** — `.maturity-badge` used the card-title token role; A4's badge/chip recipe is small-label-tracked. One CSS token swap (keep `--radius-pill`).

### Round-3 final pass (2026-07-22) — dev-hap11
- **Design blocking (badge):** `.maturity-badge` switched from the card-title token role to `--type-label-size` / `--type-label-weight` / `--type-label-tracking` (A4 badge/chip recipe), `--radius-pill` kept, applied uniformly to the hero + per-dimension badges. Level NUMBER still printed (A2). No new size token (would need an addendum-update-first per A8).

### Design re-check (2026-07-22) — verdict
- **hap-design-reviewer: CONFORMS** (badge now on the A4 badge/chip token recipe). **Panel CLEAN at c56fdee** — domain SIGN-OFF · red-team SIGN-OFF (NO PATH FOUND, verified vs an independent exact linear-algebra solver over ~900k trees) · code APPROVED · design CONFORMS. Dev phase complete; handed to `hap-qa`.
- **Test hardening (suppression oracle):** added a MECHANISM-INDEPENDENT exact-oracle fuzz test — recoverability decided by exact rational Gaussian elimination over `parent = Σchildren + phantom` (a different mechanism from the algorithm's single-unknown propagation), ~20k random trees, asserting no suppressed node is exactly recoverable (`Category=PrivacyReporting`, joins the always-on suite). Plus a negative self-test asserting the test's own `DeterminedNodes` checker detects the BR1 pre-close leak (P2 recoverable from the INITIAL published set) — guards the checker against regressing to a vacuous ∅.
- **HAP-10 coverage:** added an adversarial CycleClose fixture — a sub-4 single-child branch under a PUBLISHED ancestor — asserting the FROZEN snapshot suppresses the whole branch (the cross-level `Close` runs identically at close), so "HAP-10 tests pass unchanged" is a real pass, not coverage-vacuous.
- **Docs/nits:** research.md D2 §13 annotated (hierarchy-global strengthening realises D2's own rationale; wiki is the as-built source of record, DR-0003); story `/api/me/scope` → `/api/me/dashboard` corrected.

### G1-readiness — accepted residuals (owner ratification at G1)
The red-team flagged three residuals that are ACCEPTED (not defects) but the G1 witness MUST see and the owner MUST ratify:
1. **Cross-cycle TREND differencing.** Per-snapshot suppression protects a single cycle; it does NOT close the classic period-over-period attack (a node published at N=5 this cycle and N=4 last cycle, where the single joiner is known, reveals the joiner's contribution). This is inherent to period reporting, not a suppression bug. Owner ruling needed: accept, or add trend-level suppression (a future story).
2. **The inherent k=4 floor.** A published N=4 aggregate is differenceable by a member who knows the other three — the designed minimum-group-size privacy floor (FR-014), not a leak. Recorded so G1 ratifies k=4 as the accepted floor.
3. **Laminar-partition assumption.** The whole defence assumes `Σchildren ≤ parent` (a strict tree; every person in exactly one child). True for the synthetic directory, but a real directory import allowing matrix / dual-manager / dotted-line-as-solid orgs would break the partition and need a data-integrity guard at import (a real-data-onboarding precondition, ties to Q-010/Q-014). Recorded for G1.

### QA (adversarial, fresh agent — 2026-07-22) — hap-qa

Fresh instance, no shared context with dev-hap11. Verified against the running code and the real canonical
synth directory (`Distributions.CanonicalSeed`), not dev's assertions. New tests added this window (QA work,
attributed here, not backdated to dev):
`backend/tests/Hap.Api.Tests/RollupDashboardQaAdversarialTests.cs` — 8 tests, all `Category=PrivacyReporting`.

**Acceptance criteria — literal, clause by clause:**
1. Dashboard endpoint (mean+floor+completion%+unmoderated%, closed=snapshot/open=live, agree on a
   just-closed cycle) — **PASS**. Re-verified independently with a NON-uniform per-person/per-dimension
   fixture (dev's own reconciliation test uses one uniform score for the whole population, which cannot
   catch a person/dimension mixup); live and snapshot matched to 9dp on every dimension, the floor
   distribution, completion% and unmoderated% (`Dashboard_figures_reconcile_to_raw_moderated_rows_with_nonuniform_scores_live_and_snapshot`).
2. Trend series (per-dimension means, FR-017) — **PASS**. Distribution-per-dimension (FR-018 literal text) —
   **PASS-WITH-CAVEAT, not a QA finding**: what ships is a node-level floor histogram + per-dimension MEANS,
   not a genuine per-dimension level histogram; this is Q-025's own documented gap (OWNER-RULING-NEEDED,
   provisional in effect) — correctly routed by dev, not something for QA to re-litigate. Confirmed the
   provisional shape is what the code actually delivers (inspection + `RollupDashboardTests`/mockup match).
3. Scope enforcement (BU/Group/Portfolio/Executive role matrix) — **PASS**. Re-verified with ALL SEVEN
   canonical seeded roles (dev's own leak-check exercises one role; the role-matrix AC test itself is
   thorough) against every rollup/dashboard/trend endpoint, plus a negative-path check dev's suite does not
   have: the generic `/api/org/{nodeType}/{nodeId}/rollup` route structurally accepts `nodeType=team` — could
   an addressed request reach an ARBITRARY team, bypassing "own team only"? Confirmed 404 for every one of
   the seven roles against both a large (n=7) and a tiny (n=2) real team
   (`Team_aggregates_are_never_reachable_via_the_addressed_org_route_for_any_role_or_size`).
4. Suppression (`{suppressed, reason}`, never zero/blank) — **PASS**. Verified against the REAL canonical
   engineered nodes (not a hand-built fixture): the sub-4 BU (BU20), the n=2 team (Team3ManagerRef, the
   generator's "n=3" edge case at the rollup-node level — see differencing note below), and confirmed the
   single-team BU (BU12) is correctly PUBLISHED (not suppressed) so the suite isn't a suppressed-branch-only
   pass (`Engineered_canonical_edge_nodes_read_as_expected_sub4_suppressed_single_team_published`).
5–6. UI/mockup/tokens/A2 badge/vitest-axe — **PASS** (verify.sh frontend suite green, 138/138; inspected
   `DimensionBar`/`StatTile`/`TrendSparkline`/`SuppressedCell`/`DashboardScreen.css` — no hex colours anywhere
   in the dashboard component tree, tokens-only; `levelAbbrev` prints the level NUMBER; TrendSparkline is
   `aria-hidden` with the series duplicated as adjacent visually-hidden text).
7. SC-006 (<5s) — **PASS, and re-measured on the path that matters.** Dev's own timing test times a 26-person
   hand-built fixture; the live pipeline recomputes the WHOLE tree (`BuildPersonInputsAsync`+`ComputeNodes`+
   `HierarchySuppression.Close`) on every open-cycle request regardless of which node is addressed, so the
   real bound is the FULL canonical directory, not a small fixture. Measured against the real ~10k-person /
   23-BU / ~2k-team-node canonical population with a cycle opened (full invitation universe):
   **AllHig live rollup ≈ 186ms, BU-scoped live dashboard ≈ 85ms** (bound 5000ms) —
   `Dashboard_responds_under_five_seconds_live_over_the_full_canonical_directory`. No "leak-local
   optimization" exists in the shipped code (`ReadLiveAsync` always computes the whole tree); at this
   population size none is needed — comfortably inside bound with >25x headroom.
8. `./scripts/verify.sh` — **GREEN** (232 backend `Hap.Api.Tests` + 13 `Hap.Domain.Tests` + 9
   `Hap.Architecture.Tests` + 138 frontend, production build, no external font).

**Mandatory L3 adversarial attempts (§9.3):**

(a) **Individual-score access via every dashboard/rollup endpoint, as EACH seeded role — attempted, no path
found.** Signed in as all seven canonical roles (Individual, Manager, BU Lead, Group Leader, Portfolio
Leader, HIG Executive, Platform Admin) against `/api/me/dashboard`, `/api/me/team/summary`,
`/api/org/allhig/rollup`, `/api/bus/{id}/dashboard`, `/api/org/group/{id}/rollup`, and every trend point
inside each response; scanned every 200 body for `personId`/`selfScore`/`managerScore`/`managerComment`/
`externalRef`/`assessmentId`/`email`/`jobTitle`/`displayName`/`onLeave`/`employeeType`/`isActive` — none
found on any response, for any role. Structural backstop confirmed by inspection: `NodeAggregateResponse`/
`AggregateFiguresResponse` carry no per-person field at all — a leak would need a NEW field, not a logic
bug (`Every_seeded_role_finds_no_individual_field_on_any_rollup_or_trend_endpoint`). Also attempted the
`/api/org/team/{id}/rollup` route directly for every role (see clause 3 above) — 404 always, no path found.

(b) **Sub-4 aggregate via team-summary + BU + org rollups, incl. differencing pairs — attempted against the
REAL canonical engineered nodes, no path found.** As the HIG Executive (most-privileged reader, sees every
published node): submitted+moderated exactly 4 REAL scored people in each of BU17/BU18/BU19 (the sub-4 BU
BU20's actual siblings under their actual group — published at exactly the floor, the sharpest version of
the attack) plus BU20's own 2 real members, then read the group total and all four BU dashboards. Checked
the arithmetic two ways, both algorithm-independent (not trusting `HierarchySuppression`'s own internals):
(i) counted suppressed siblings — never exactly 1 alongside a published group total; (ii) explicitly computed
`groupN − Σ(published sibling Ns)` and asserted it can never be a nonzero residual with only one sibling
unknown (`Group_total_minus_published_bu_siblings_cannot_isolate_the_engineered_sub4_bu` — **PASS, no leak,
BR1 holds on the real generator's wiring, not just the algorithm/ThinFixture**). Also directly attempted the
n=2 team (Team3ManagerRef, BU01) and the sub-4 BU (BU20) reads — both suppressed, `figures: null`, reason
`"N<4"`, exactly as designed. Note: the generator's "team of 4" org-of-7 edge case (BU04) is a headcount
label for the DIRECTORY population, not the rollup Team-node's own N — both of BU04's Team nodes (the
manager+3-reports team and the BU-Lead's-direct-reports team) resolve to N=3 under the rollup's
"team = manager's direct reports only" definition, so BU04 does not exercise a published-N=4-team/suppressed-
complement scenario at the Team level; this is a naming-vs-semantics observation, not a defect (the BU-level
sub4/differencing attack above is the meaningful, exercised surface). Equal-membership single-child collapse
was NOT independently re-derived against a new hand-built topology this window — dev's own
`HierarchySuppressionTests.A_deep_single_child_chain_collapses_as_one_membership_class` plus the
~900k-tree/~20k-tree independent-oracle fuzz tests already give strong, mechanism-independent coverage of
that specific case; re-deriving it would have been low marginal value against the time budget.

(c) **Recompute a BU's figures from raw moderated scores, compare to the dashboard — attempted with adversarial
non-uniform data, matched exactly.** Built an independently-designed fixture (4 reports, scores assigned
`(personIndex + dimensionIndex) % 4` so every dimension's mean AND every person's floor are genuinely
distinct — a dimension/person mixup cannot hide behind a repeated constant, unlike a uniform-score fixture).
Recomputed every dimension's mean, the floor distribution, and completion/unmoderated % independently from
raw `AssessmentScore` rows via a separate LINQ query against the live path AND the frozen snapshot after
close — matched to 9 decimal places on every figure, both paths, and the live/snapshot pair matched each
other exactly (`Dashboard_figures_reconcile_to_raw_moderated_rows_with_nonuniform_scores_live_and_snapshot`
— **PASS, no desync found**).

**G1 residuals re-checked (not re-litigated, confirmed as documented):** a suppressed cycle's trend point
carries no figures (period-over-period differencing residual is not worsened by exposing the raw prior
number) — confirmed via `Suppressed_cycles_contribute_no_trend_figures_and_a_published_floor_of_four_is_not_oversuppressed`.

**Negative-path tests added (QA work):** malformed/unrecognised node-type route segments and a non-guid BU id
all 404, never 500 (`Malformed_or_unrecognised_node_type_segments_404_never_500`).

**Red-team brief (mandatory L3).** Constructed the one concrete violation path CLAUDE.md §9.4 asks for an
attempt on — cross-level differencing against the real canonical tree (b, above) — and it does NOT succeed:
BR1's hierarchy-global closure suppresses at least one additional sibling whenever a group total is
published, so `parent − Σ(published siblings)` never isolates the engineered sub-4 BU. Examined and found no
other path: (i) every response DTO is structurally incapable of carrying a per-person field (compile-time
guarantee per `RollupReadGuardTests`, re-confirmed live over HTTP for all seven roles); (ii) the one
alternate ADDRESSED route that could have reached an arbitrary team (`/api/org/team/{id}/rollup`) is denied
for everyone, always; (iii) the live and frozen-snapshot figures reconcile exactly to raw rows, so there is
no drift a reader could exploit between what's stored and what's served. **Verdict: no violation found; BR1
holds on the real canonical directory's wiring, not just the algorithm.**

**Overall QA verdict: PASS.** No individual-score leak, no sub-4/complement leak, no desync, found across any
attempted path. AC clause 2's per-dimension-histogram half is knowingly unmet per the pre-existing,
owner-flagged Q-025 provisional (not a new QA finding). `./scripts/verify.sh` GREEN including the new tests.
Story remains `status: qa` pending owner/G1 ratification of Q-024/Q-025 — QA does not merge or close.
