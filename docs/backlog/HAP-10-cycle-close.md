---
id: HAP-10
title: Cycle close — auto-adopt unmoderated, snapshots with frozen suppression, departure escalation
epic: E2-assessment
wave: 1
fr: [FR-068, FR-069, FR-070, FR-015, FR-016]
risk: L3                # trigger: writes moderated scores + rollup/suppression computation over AssessmentScores
status: qa
estimate: {dev: M, qa: S}
worklog:
  - {phase: dev, start: 2026-07-22T11:44:30Z, end: 2026-07-22T13:50:27Z, mins: 125}
  - {phase: qa, start: 2026-07-22T13:52:31Z, end: 2026-07-22T14:28:21Z, mins: 35}
closure: null
---
## Story
As the platform, closing a cycle auto-adopts unmoderated self-scores (flagged), computes mean and floor-level rollups into immutable snapshots with suppression verdicts frozen at close, and escalates a departed manager's pending reviews — so history is complete, honest, and can never retro-expose anyone.

## Context
- Spec: FR-068 (auto-adopt + unmoderated %, excluded from calibration delta), FR-069 (leave status), FR-070 (manager departure escalation), FR-015/016 (mean + floor computations); "Clarifications" bullets 1 and 5.
- Plan: research **D4** (RollupSnapshot per org node at close; trend reads snapshots) and **D2** (suppression verdict FROZEN in the snapshot); data-model.md RollupSnapshot (**EF migration #5**); scoring maths in `Hap.Domain/Scoring` (mean of 7; floor = min; distribution).
- Files: `backend/src/Hap.Domain/Scoring/**`, close orchestration in cycle service, snapshot writer via seam, `backend/src/Hap.Infrastructure/Persistence/**` (migration #5).
- **Serialise with: HAP-8 (migration chain).**
- Blocked by: HAP-9
- Parallelisable: no

## Acceptance criteria
- [x] `POST /api/cycles/{id}/close`: every Submitted-but-unmoderated assessment becomes AutoAdopted with self-scores copied to manager scores and `unmoderated=true` (FR-068); Moderated assessments untouched (test with mixed states). — **PASS.** `Close_auto_adopts_unmoderated_submissions_and_leaves_moderated_ones_untouched` (CycleCloseTests.cs) drives real submit/moderate/close over HTTP and asserts both branches by state + manager-score value. Verified by inspection: `Assessment.AutoAdopt()`/`AssessmentScore.AdoptSelf()` are forward-only from `Submitted` only.
- [x] Calibration delta computation excludes AutoAdopted rows (FR-068 test: delta identical with/without an auto-adopted member). — **PASS.** `Calibration_delta_is_identical_with_or_without_an_auto_adopted_member` (unit) + `Calibration_delta_is_unchanged_by_an_auto_adopted_member` (integration) both lock this; QA independently re-derived the same invariant across a 3-BU fixture in `Independent_recompute_matches_the_snapshot_for_team_bu_and_allhig_nodes` (M1's team has one moderated, divergent report — delta present and computed off the moderated row only).
- [x] Manager departed before close (pending reviews escalate to the manager's manager, moderate-until-close, still-unmoderated → auto-adopt). — **PASS.** `A_departed_managers_report_can_be_moderated_by_the_escalated_reviewer_others_auto_adopt` (CycleCloseSuppressionTests.cs) exercises the full FR-070 path against the synth leaver shape (DIR/MGRD/EMP_D1/EMP_D2): EMP_D1 escalated-moderated stands (`Moderated`, `Unmoderated=false`), EMP_D2 auto-adopts.
- [x] Scoring maths (Hap.Domain.Tests, pure, `Category=PrivacyReporting`): mean/floor/distribution + property test (floor ≤ every score, floor ≤ mean). — **PASS.** `RollupScoringTests.cs`, all `Category=PrivacyReporting`: exact-value mean/rounding tests, floor tests, and `Property_floor_is_at_or_below_every_dimension_score_and_the_mean` (5,000-iteration seeded sweep). Re-ran green as part of this pass's `Hap.Domain.Tests` run (79/79, then 13/13 filtered PrivacyReporting).
- [x] Snapshots written for every Team/BU/Group/Portfolio/AllHig node with the two populations tracked apart. — **PASS.** `Snapshots_carry_hand_computed_mean_floor_completion_and_unmoderated_pct` (4 synth people, hand-computed mean 1.5/2.25 split + floor distribution) and `A_mid_cycle_leaver_stays_in_the_scored_population_but_leaves_the_completion_base` (scored-n=3 ≠ completion-denom=3-of-a-different-3, mean 1.33 with leaver included) both pass. QA independently re-derived floor distribution + N for a THIRD, dev-independent fixture in `Independent_recompute_matches_the_snapshot_for_team_bu_and_allhig_nodes` (below).
- [x] Suppression verdicts frozen post-shrink; append-only. — **PASS.** `A_published_verdict_is_frozen_and_survives_shrinking_the_node_below_four_after_close` (deactivates 2 of MGRB's 4 post-close; snapshot N/Suppressed/Id unchanged) + `RollupSnapshotAppendOnlyTests` (no public setter/mutator; static analysis of no `RollupSnapshots.Remove/Update/ExecuteDelete` call anywhere in `backend/src`) + three live-DB trigger-rejection tests (raw UPDATE/DELETE/TRUNCATE, `Category=PrivacyReporting`). "As of close" reconciliation locked by `A_post_close_override_re_moderation_never_alters_the_frozen_snapshot` (byte-for-byte snapshot equality across a genuine post-close re-moderation).
- [x] Snapshot totals reconcile (team-homed carve-out, Q-023). — **PASS.** `Snapshot_totals_reconcile_and_recompute_from_raw_matches_both_populations` (Σteam=BU=Group=Portfolio=AllHig=11 in the 2-BU tree fixture; independent recompute of BU01's dim-0 mean from raw manager scores) + the two teamless-attribution regression tests (`A_cross_bu_managed_report_is_teamless…`, `A_manager_less_scored_bu_head_is_teamless…`). QA independently re-derived this invariant a second, dev-independent way below.
- [x] QA (adversarial, fresh agent — mandatory L3 attempts): see the full QA record below.
- [x] `./scripts/verify.sh` green (migration idempotent). — **PASS**, confirmed by this QA pass (see verify.sh result below), migration idempotent (applies once, second run "already up to date").

## Attempts / notes

**SPEC AUDIT 2026-07-21 (pre-start edit, story was todo):** completion denominator rule for mid-cycle leavers added to the snapshot criterion.
**L2 PANEL B1 (same day):** domain specialist required the §3.5 retention guard — submitted leavers stay in scored aggregates; scored-n vs completion-n disambiguated and both locked by tests.

**DEV 2026-07-22 (dev-hap10) — design decisions taken (flag for panel):**
- **Close hooked into `CycleService.CloseAsync` (Q-017), one transaction.** The score-reading work must live in the seam (research D1 boundary guard fails the build on any `Assessment` token outside `Hap.Api/Authorization`), and `CycleService` is in `Hap.Infrastructure` — so a neutrally-named port `ICycleCloseProcessor` is declared in Infrastructure and its sole implementation `CycleCloseProcessor` lives in the seam, injected via DI. `CloseAsync` opens a tx, does the state transition, runs the processor (which stages auto-adopt mutations + snapshot inserts on the same context), then one `SaveChanges` + commit. No parallel close path.
- **Team node = member's-BU × manager (BU-partitioned).** The reconciliation AC ("Σ team n = BU n") only holds if teams partition the BU, so teams are grouped by the member's home BU and their manager id (node_ref = manager id); a manager-less person joins no team but still counts in BU+ up the tree. Completion denominators do NOT reconcile team→BU (manager-less active people sit in the BU base, no team) — they reconcile BU→group→portfolio→all-HIG, where BUs partition cleanly. Both populations locked by tests.
- **Suppression frozen by REUSING the seam `SuppressionEvaluator`** (never reimplemented): evaluated top-down per parent level (AllHig→Portfolios→Groups→BUs→Teams); AllHig gets rule-1 (n<4) only. Verdict + reason (`N<4`/`Complement`) stored on the immutable snapshot.
- **Migration #5 append-only triggers mirror HAP-3 `audit_log` EXACTLY (plain, not ENABLE ALWAYS).** Row-level UPDATE/DELETE + statement-level BEFORE TRUNCATE. The "session_replication_role reset coupling" HAP-7 mandated means the trigger must be *bypassable by the test reset's* `session_replication_role='replica'` (which the app role, a non-superuser, can never set) — verified: app-role UPDATE/DELETE/TRUNCATE all rejected, superuser replica-role CASCADE-truncate (the `ResetAsync` path, since rollup_snapshots FKs cycles) succeeds. `ENABLE ALWAYS` was rejected: it would break every integration test's reset.
- **EF scaffolder bug fixed:** the HEAD model snapshot never recorded HAP-9's `xmin` rowversion, so the migration diff invented an `AddColumn "xmin"` on `assessments` — but `xmin` is a Postgres *system column* (`ALTER TABLE ... ADD COLUMN xmin` is rejected). That op was removed from the migration; the regenerated snapshot now carries the token so no future migration re-proposes it.
- **Q-022 (PROVISIONAL, recorded in QUESTIONS.md) — FR-068 × Q-017a:** a late override granted post-close must still enable moderation, but close has already auto-adopted by then. Resolution: `Moderate` now accepts `Submitted` OR `AutoAdopted` (re-moderation supersedes the placeholder, clears `unmoderated`), gated by the unchanged submission lock. Widens the HAP-9 moderation contract slightly — flagged for panel/owner. Kept the two HAP-9 post-close-override tests green.
- **Q-020 confirmed:** senior-leader-with-no-eligible-moderator is handled by the generic auto-adopt path with NO special-casing.

**PANEL ROUND 1 (2026-07-22) — verdicts:** `hap-domain-specialist` = **SIGN-OFF** (with the Q-023 ruling below); `hap-red-team` = **VIOLATION FOUND** (B1 cross-BU/manager-less → Σchild>parent → suppression throw / wrong verdict); `hap-code-reviewer` = **CHANGES REQUIRED** (3 blocking). Fixes applied this round (TDD, re-verified):
- **B1 (domain-ruled Q-023):** teams now require manager AND report share a home BU; manager-less + cross-BU-managed people are teamless (BU-direct). `BuildNodes` guards on `manager.BusinessUnitId == report.BusinessUnitId`. New Category=PrivacyReporting tests: `A_cross_bu_managed_report_is_teamless…` and `A_manager_less_scored_bu_head_is_teamless…` (both assert close 200 not 500, correct teamless attribution, generalized reconciliation). Existing `Snapshot_totals_reconcile…` unchanged and still green. AC bullet spec-corrected above.
- **B2:** snapshot index is now UNIQUE on (CycleId, OrgNodeType, OrgNodeRef) with **NULLS NOT DISTINCT** (dedups the AllHig null-ref row too) — a concurrent double-close's second insert fails and rolls back. Migration #5 regenerated (`20260722125917_AddRollupSnapshots`), still idempotent.
- **B3:** three live-DB app-role rejection tests added (raw UPDATE/DELETE/TRUNCATE each raise "append-only"), mirroring `OrgOverrideAuditTests`, Category=PrivacyReporting.
- **F3:** `A_post_close_override_re_moderation_never_alters_the_frozen_snapshot` — closes, re-moderates an auto-adopted report under a post-close override, asserts the snapshot row is byte-for-byte identical. Reconciliation "as of close" documented in the AC + Q-022 addendum.
- **SHOULD-FIX applied:** unresolvable person id in close now logs a warning (ILogger) instead of silent skip; migration comment corrected to state the trigger honestly (app role `hap` is owner/superuser → same accepted HAP-3/HAP-7 residual, real mitigation = non-owner app role, owed before real data); data-model.md late-override line reconciled with Q-022.

**CARRY-FORWARD F2 (recorded, NOT fixed in HAP-10 — precondition on HAP-11):** `RollupSnapshot` persists real N/mean/floor-distribution for **Suppressed** rows in queryable columns behind a PUBLIC DbSet. This is NOT live-reachable today (no snapshot-read endpoint exists) and the figures are required for internal reconciliation, so it is deliberately not fixed here. **HARD PRECONDITION on HAP-11 (BU-dashboard read story):** snapshot reads MUST apply suppression — no external projection may expose N/mean/distribution for a Suppressed row (the seam must gate snapshot reads exactly as it gates live rollups). **G1-READINESS FLAG:** this is a G1 witness item — the owner must confirm at G1 that no read path projects a suppressed snapshot's numbers.

**Blocked-by / precondition for HAP-11:** F2 above — HAP-11 must not merge a snapshot-read path that bypasses N<4/complement suppression on the stored `Suppressed` verdict.

**PANEL ROUND 2 (2026-07-22) — CLEAN at a8a231e (verbatim sign-offs):**
- `hap-domain-specialist` r2: **✓ SIGN-OFF** — Q-023 correctly implemented (team-homed carve-out; home-BU attribution preserved; reviewer-of-record path untouched).
- `hap-red-team` r2: **✓ NO PATH FOUND** — the cross-BU Σchild>parent break is dead; teamless-complement differencing is closed by suppression rule 2 (a publishable team whose teamless complement is <4 is complement-suppressed). Proposed one passing pin test for QA (below).
- `hap-code-reviewer` r2: **✓ APPROVED** — gate green (438 / 208), zero blocking; three non-behavioral fold-ins requested (stale BuildNodes summary, Q-023 §-citation → CLAUDE.md §8.3, UPDATE-trigger test re-reads N not count) — all applied in the clock-out pass, code approval stands.
- Round-1 verdicts (for the record): domain SIGN-OFF+Q-023 ruling · red-team VIOLATION FOUND (B1) · code CHANGES REQUIRED (3 blocking).

**HANDED TO hap-qa (§9 — QA owns, dev did NOT write these):**
1. Red-team's proposed pin `A_publishable_team_is_suppressed_when_the_teamless_complement_is_below_four` — proves suppression rule 2 protects a teamless <4 complement (a BU with one publishable team ≥4 and 1–3 teamless people must complement-suppress the team).
2. Standard §9.3 adversarial attempts: read a score outside the chain via any close output; obtain an aggregate covering <4; make a rollup/Harris figure disagree with underlying rows (independent recompute). Document outcomes in this file.

---

## QA (2026-07-22, fresh instance, qa-hap10) — VERDICT: PASS, zero blocking defects

Fresh agent, no shared context with Dev. Verified every AC clause literally against the running code
(marked PASS inline above). New file `backend/tests/Hap.Api.Tests/CycleCloseQaAdversarialTests.cs` —
8 test methods (1 six-case `[Theory]` + 7 `[Fact]`, 13 executions total), all `Category=PrivacyReporting`,
attributed as QA work (none existed during Dev/panel). One self-authored assertion bug found and fixed
during this pass (below) — not a product defect.

### Mandatory L3 attempt (a) — read a score outside the chain via any close output/snapshot path
**Examined, no path exists.** By source inspection (`CycleEndpoints.cs`) HAP-10 wires exactly one new
handler, `POST /close`, returning `CycleResponse.From(cycle)` — cycle metadata only (id/frameworkVersionId/
name/state/contractorExclusionEnabled/opensAt/closesAt). No `RollupSnapshots` DbSet is exposed through any
endpoint file (`AdminEndpoints`/`AssessmentEndpoints`/`CycleEndpoints`/`FrameworkEndpoints`/`TeamEndpoints`
— grepped, none reference it). Proved at the wire level, not just by inspection:
- `Close_response_body_carries_no_score_aggregate_or_snapshot_field_at_all` — parses the raw `/close`
  response JSON and asserts the property set is EXACTLY the seven cycle-metadata fields, so a future
  accidental over-serialization would fail this test immediately.
- `No_snapshot_or_rollup_read_route_exists_at_all_guessed_paths_all_404` — probes 7 plausible guessed
  routes (`/snapshot`, `/snapshots`, `/rollup`, `/rollups`, `/api/rollups`, `/api/snapshots`,
  `/api/rollup-snapshots/{id}`); all 404 (undefined route), never 403 (would prove a route exists) or 200.
No individual score is reachable via any HAP-10 close output for any seeded role — the question is moot
for this story because the read surface is zero, not because a gate correctly denies it. **Residual flag
independently re-confirmed (already tracked by Dev as F2):** `RollupSnapshot` DOES carry real N/mean/
floor-distribution for **Suppressed** rows in queryable columns behind a public DbSet — inert today (no
read endpoint), but a HARD PRECONDITION on HAP-11: any future snapshot-read path MUST re-apply suppression
before projecting a Suppressed row's numbers. Confirmed still correctly flagged in Dev's notes above; not
re-litigated as a new finding.

### Mandatory L3 attempt (b) — obtain an aggregate covering <4
**Required red-team pin, added and defeated (test passes — suppression holds):**
`A_publishable_team_is_suppressed_when_the_teamless_complement_is_below_four` — team of 4 (E1..E4) under
MGR1, MGR1 reports to a teamless, submitting BU head (HEAD, manager-less). BU n = 5 (4 team-homed + 1
teamless). Team's own n=4 clears rule 1; complement = 5−4 = 1 (in 1..3) → rule 2 correctly suppresses the
team with reason `"Complement"`. Attempted to defeat it two ways: (1) HTTP differencing — moot, no read
endpoint exists at all (attempt (a) above) so there is no wire-level surface to difference in the first
place; (2) direct DB inspection (test-only privilege) — confirmed HEAD never materialises a sibling
`Team(HEAD)` row (teamless people are B1/Q-023 BU-direct, never a Team node), so the only route to HEAD's
score is exactly the BU-minus-Team(MGR1) differencing that rule 2 closes. **No defeat found.**

Additional <4 attempts:
- `Exactly_three_scored_is_suppressed_exactly_four_is_published_isolated_from_complement` — the threshold
  itself, isolated from any complement interaction (n=3 → `Suppressed=true, reason="N<4"`; the existing
  dev suite's n=4 case is `Snapshots_carry_hand_computed_mean_floor_completion_and_unmoderated_pct`).
- Reused/re-read dev's `Suppression_verdicts_are_frozen_per_node_over_the_tree` (multi-child differencing,
  parent 7 / children 5+2) and confirmed by independent re-derivation it still holds after this session's
  changes (unmodified, re-ran green).

### Mandatory L3 attempt (c) — make a snapshot disagree with underlying rows
**Independent recompute across 3 node types, a fixture dev never used** (deliberately not reusing Dev's
tree/single-team fixtures, so a bug either fixture happens to mask is not silently re-inherited):
`Independent_recompute_matches_the_snapshot_for_team_bu_and_allhig_nodes` — 3-BU fixture (BU01/02 one
group+portfolio, BU03 a separate group+portfolio), 12 submitters with a deliberately rich mix: one
moderated-and-divergent row (A1, self=3→manager=1, so recompute must use the manager score of record, not
self), several plain auto-adopts, one mid-cycle leaver (C4, deactivated post-submit). Recomputed
independently from raw `AssessmentScore`/`Assessment` rows and compared to the stored snapshot:
- Team(M1) per-dimension mean, recomputed from raw manager scores — **matches**.
- BU01 floor distribution, recomputed by taking `MaturityScoring.FloorLevel` over each person's raw
  manager scores independently — **matches**, including the total count.
- AllHig total scored N, recomputed via a raw `COUNT` over `Moderated ∪ AutoAdopted` assessments spanning
  all 3 BUs — **matches** (12).
- BU03's two populations (the leaver split) — scored N=4 (leaver included) vs completion denominator=4
  (M3 the never-submitting-but-active head + C1..C3; C4 excluded) — **matches**, both independently.

**One self-authored bug found and fixed during this pass, not a product defect:** my first draft asserted
BU03's completion denominator = 3, forgetting M3 (the BU head, active, invited, never submits) also counts
toward the denominator — only submission status affects the numerator, not the denominator. Corrected to 4
and re-verified green; documented here per the "attempted to make it disagree" honesty requirement — this
was MY arithmetic mistake, not evidence of a snapshot/raw desync.

No disagreement found anywhere. The Q-022 "as of close" byte-for-byte freeze (dev's
`A_post_close_override_re_moderation_never_alters_the_frozen_snapshot`) was independently re-read and
re-run green, confirming a post-close re-moderation legitimately diverges the frozen snapshot from live
rows by design (immutable history), not by defect.

### Red-team brief (mandatory L3 panel member — QA reprising the role for its own new tests)
**Violation path attempted and NOT found.** The two candidate attacks examined:
1. Differencing a frozen Suppressed snapshot against a published sibling/parent to recover a <4 cell —
   closed by suppression rule 2 (pin test above), and moot in any case because no HTTP read path exists
   for any snapshot row today.
2. Racing two concurrent `/close` calls to double-write a snapshot set, or to slip an unauthorized close
   through timing — tested directly:
   `Two_concurrent_close_requests_produce_exactly_one_committed_close_never_duplicate_snapshots` —
   `Task.WhenAll` on two independent authenticated clients hitting `/close` simultaneously. Exactly one
   succeeds (200), exactly one fails cleanly (409 forward-only or 500 unique-index violation, both
   accepted outcomes — never a silent second 200), and exactly one `Team(MGR1)` snapshot row exists after
   the race. Passed — B2's unique index (`CycleId, OrgNodeType, OrgNodeRef`, NULLS NOT DISTINCT) plus the
   forward-only `Cycle.Close()` transition together close the race.

### Non-mandatory but in-scope finding: RBAC coverage gap on `/api/cycles/*` (closed by new tests)
`AdminGateAllRolesTests` (a prior story's role-sweep) exercises `/api/admin/*` for all six non-admin seeded
roles but never touches `/api/cycles/*`, despite both being gated by the identical `PlatformAdmin` policy
and `/api/cycles/{id}/close` being the exact write path that drives auto-adoption over `AssessmentScores`
— CLAUDE.md §7's own example of a small-diff, big-blast-radius L3 surface. Traced the gate itself
(`LocalDevProvider.SignInAsync` / `IdentityServiceCollectionExtensions`): `RequireRole("PlatformAdmin")`
checks ONLY explicit `RoleGrant`-backed claims; the five hierarchy-tier labels ("Individual" through
"Portfolio Leader") get no RoleGrant and thus no Role claim at all, so a small hand-built fixture is
behaviorally equivalent to the real canonical hierarchy for this specific gate (confirmed by code reading,
not assumed) — used a lightweight fixture rather than AdminGateAllRolesTests' ~15k-person canonical org
sync, avoiding a 7x cost multiplication under a `[Theory]`.
- `Non_admin_seeded_roles_cannot_create_open_or_close_a_cycle` (`[Theory]`, 6 roles) — create/open/close
  all 403 for every non-admin role; cycle state and snapshot table both verified unchanged after the
  denied close attempt.
- `Platform_admin_role_alone_is_admitted_to_close_negative_control` — proves the gate is discriminating,
  not failing closed universally.
No defect (the gate DOES hold for `/cycles/*` too — this closes a coverage gap, not a live vulnerability),
but flagging the pattern: **any future PlatformAdmin-gated endpoint group should get its own explicit
non-admin sweep rather than relying on `AdminGateAllRolesTests`' `/api/admin/*`-only coverage.**

### verify.sh
Two full runs (first caught my own assertion bug above; second green). Final run: **exit 0.**
Backend build 0 errors/warnings-as-errors clean; migration applies once, idempotent second run; unit
suite `Hap.Domain.Tests` 79/79, `Hap.Architecture.Tests` 11/11, `Hap.Synth.Tests` 41/41,
`Hap.Api.Tests` **320/320** (was 319/319 at Dev clock-out — +1 net file, 13 new executions, all pass);
`Category=PrivacyReporting` regression suite 201 (API) + 13 (Domain) + 7 (Architecture), all pass;
frontend install/lint/typecheck/test/build green; no external font request in the built output.

### QA verdict
**PASS.** All acceptance-criterion clauses verified literally (marked above). All three mandatory L3
attempts executed and documented; no violation path found in any of them. The required red-team pin test
added and green. One coverage gap closed (RBAC sweep on `/api/cycles/*`) as in-scope negative-path work,
zero product defects found. Not setting `status: done` or merging — closure is the lead's.
