---
id: HAP-8
title: Self-assessment — API + UI (assessment-self.html)
epic: E2-assessment
wave: 1
fr: [FR-007, FR-062, FR-066]
risk: L3                # trigger: read/write path over Assessments/AssessmentScores tables
status: qa
estimate: {dev: L, qa: M}
worklog:
  - {phase: dev, start: 2026-07-21T23:04:23Z, end: 2026-07-22T00:45:41Z, mins: 102}
  - {phase: qa, start: 2026-07-22T00:47:31Z, end: 2026-07-22T01:05:54Z, mins: 18}
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
- **HAP-7 handoff — the post-close submission lock (Q-017a, binding):** HAP-7 shipped only the lock PRIMITIVE `Cycle.AllowsSubmission(hasLateOverride)` (Open→allowed, Closed→only with a late override, Draft→never) — it could not wire it to a real submission because the Assessment tables are THIS story's migration #4. **The self-assessment submit path MUST consult `Cycle.AllowsSubmission` (+ `CycleService.HasLateOverrideAsync`) and reject a post-close submit (423/409) unless a late override exists** — if you don't, post-close rejection silently will not exist anywhere in the system. Test it (submit after close → rejected; after a late-override → accepted).
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

### Attempt 1 — dev (2026-07-22), branch `HAP-8-fr-007-self-assessment`
No prior HAP-8 attempts (`git log --all --grep HAP-8` shows only HAP-7 closure + DR-0005 carry-forward context commits). First real assessment data; first migration touching `Assessments`/`AssessmentScores`.

**Risk L3 — trigger:** read/write path over the `Assessments`/`AssessmentScores` tables (§7 L3 row, first match). Also touches the visibility seam (`Hap.Api/Authorization`) and the seam-boundary architecture guard — both independently L3.

**Design decisions (for the panel):**
- **Type relocation (HAP-5 handoff):** `Assessment`/`AssessmentScore` move from `Hap.Api/Authorization/AssessmentData.cs` to `Hap.Domain/Assessments/` (Infrastructure references Domain, so `HapDbContext` registers them with no layer inversion). `IAssessmentStore` port stays in the seam.
- **Seam stays the only DB-query path.** `HapDbContext` exposes NO public `DbSet<Assessment>`; entities are registered via `IEntityTypeConfiguration` (`AssessmentEntityConfiguration`, Infrastructure) so `HapDbContext.cs` never names the bare types. The ONLY `db.Set<Assessment>()` call lives in the seam's `SeamAssessmentStore`. The seam-boundary guard is extended with (a) the DbSet/`Set<>` query-surface patterns, and (b) allowlisting the new domain-definition folder + the generated Migrations dir + the single EF-config file (schema mapping, not a query path).
- **Submission lock (HAP-7 / Q-017a):** the self submit path AND every score write consult `Cycle.AllowsSubmission(hasLateOverride)` + `CycleService.HasLateOverrideAsync`; post-close write rejected unless a late override exists.
- **Self endpoints take NO personId input** — subject is always the authenticated caller, so cross-person access via the self endpoints is structurally impossible (QA's mandatory attack surface returns own data only).
- **Q-015 pinned test reframed** to "ratified per DR-0005" (assertion unchanged: ALLOW one-hop direct read, DENY transitive) — rides this L3 panel.

**Provisional decisions recorded (mockup-guided, no behaviour-changing ambiguity → not raised to QUESTIONS.md):**
- **FR-062 progress-counting model:** the prior-cycle score is *shown* pre-selected with the "last month" dashed pill (pre-population), but a dimension counts toward "x of 7" and toward the projected floor ONLY once it has a current self score this cycle. This is exactly the mockup's simultaneous state — prior-populated dims (e.g. "Value measured" L0) carry a "last month" pill yet read "to do" in the progress panel. So a genuinely no-change month is a quick per-dimension confirm rather than instant 7/7. The mockup's shown state is binding and resolves this; documented here rather than invented silently.
- **Invitation gating — INITIALLY deferred, then FIXED in round 2** (panel overruled the deferral; see round-1 record below). The self endpoints now require a non-excluded `CycleInvitation` for the resolved cycle before GET/PUT/submit; an excluded contractor (FR-005) or not-onboarded-BU person (FR-002) gets 404 and cannot land a row.
- **Per-dimension hint omitted:** the mockup shows a descriptive sentence under each dimension name; the framework JSON (`docs/frameworks/ai-maturity-sdlc.v1.json`) has no such field and Art. II.4 forbids hard-coding dimension content, so the hint is omitted. All binding content (dimension name, four level names + descriptors) comes from data.
- **Status codes:** cycle-locked (closed w/o override) → **423**; not a cycle participant → **404**; already-submitted / no-cycle-on-write → **409**; incomplete submit / out-of-range score / unknown-or-duplicate dimension → **422**; no-cycle-on-GET → **404**. (Draft is never resolved as "current" — a Draft-only state yields no-cycle → 409 on write / 404 on GET; `Cycle.AllowsSubmission`'s Draft→false branch is unreachable via this service, so there is no Draft→423 path.)

### Dev clock-out record (round 1)
`./scripts/verify.sh` ran **ALL GREEN** at commit `fc1f9e9` (all four migrations idempotent — second `ef database update` no-ops; PrivacyReporting suite green; frontend lint/typecheck/test incl. vitest-axe + the 5-of-7 component test; no external font). Backend counts: Domain 48, Architecture 9, Synth 41, Api 219, PrivacyReporting 124. Independently re-run green first-hand by hap-code-reviewer.

### L3 panel — round 1 verdicts
- **hap-design-reviewer — SIGN-OFF** (1 advisory: omitted page-head subtitle/chip, covered by the sidebar ProgressStepper).
- **hap-domain-specialist — BLOCKED:** invitation gating — self endpoints admit any authenticated person, so an FR-005-excluded contractor or FR-002 not-onboarded person can land an Assessment row, violating US1's "invited individual" precondition and the HAP-19 "rows ⊆ invited" assumption.
- **hap-red-team — SIGN-OFF (conditional):** gaps to record — (5a) a PUT with an unknown/duplicate DimensionId → 500 or future phantom score; defense-in-depth join for rollup/Harris queries (carry-forward).
- **hap-code-reviewer — CHANGES REQUIRED:** record the green verify run; + dimension-membership advisory elevated; minor doc/comment nits. Risk confirmed **L3** by both reviewers.

### Round 2 — fixes applied (session-lead rulings)
- **BLOCKING 1 (invitation gating, ruled fix-now):** `SelfAssessmentService.EnsureInvitedAsync` gates GET/PUT/submit on a non-excluded `CycleInvitation` for the resolved cycle → `NotInvitedToCycleException` → **404** (symmetric with no-cycle; person-addressed existence-leak convention). Tests (PrivacyReporting): excluded contractor and not-onboarded-BU person cannot read or land a row; an invited person still can.
- **BLOCKING 2 (dimension-membership, elevated to blocking):** `ValidateDimensionsAsync` rejects any DimensionId not in the resolved cycle's framework version, and any dimension duplicated in one payload → `SelfScoreDimensionException` → **422**, before any write. Robust membership test (not a `FirstOrDefault` sentinel) so `Guid.Empty` is also rejected. Tests: unknown dim (incl. `Guid.Empty`) → 422 not 500; duplicate dim → 422; no row persisted.
- **BLOCKING 3 (record):** the green verify run + these four verdicts recorded above.
- **Advisories applied:** (a) added `SeamStoreImplementationTests` — reflection proof that `SeamAssessmentStore` is the ONLY production implementer of `IAssessmentStore`/`ISelfAssessmentStore`; softened the overstated "compile-time fact" comment to describe the two guards accurately (regex scan + reflection, not a compiler proof). (b) `Editable` flag added to `GET /api/me/assessment`; the UI now disables the level cards / evidence / buttons and shows a read-only notice when the cycle is closed without an override (token-clean). (c) Doc/comment nits: `NoOpenCycleException`→`NoCurrentCycleException` cref fixed; stale `CycleLateOverride.cs` comment updated (assessment types now in `Hap.Domain.Assessments`); Draft-status claim corrected above.
- **Advisory noted, not built:** evidence typed for a dimension with no level selected is dropped on save — the schema requires a self score per row (evidence supports a score), so evidence-only cannot persist; acceptable, left as a known UX limitation.

### Dev record (round 2)
`./scripts/verify.sh` re-ran **ALL GREEN** after the round-2 fixes (all four migrations idempotent; PrivacyReporting green; frontend lint/typecheck/test/build; no external font). Backend counts: Domain 48, Architecture 9, Synth 41, Api 228 (+9), PrivacyReporting 131 (5 architecture + 126 api, +7). New/changed tests: invitation gating (excluded contractor / not-onboarded / invited-can), dimension-membership (unknown incl. `Guid.Empty` → 422; duplicate → 422), `Editable` flag, and `SeamStoreImplementationTests` (sole-implementer reflection proof).

### L3 panel — round 2 verdicts + round-3 fix
Round-2 panel: **hap-code-reviewer, hap-domain-specialist, hap-red-team all SIGN-OFF** (invitation gating + dimension-membership confirmed closed, no bypass). **hap-design-reviewer BLOCKED** on the read-only UX: `disabled` was passed to the radios/textarea but there was no `:disabled` CSS, so a locked card looked identical to an editable one (only the banner conveyed state) — inconsistent with the A4 `.assessment-btn:disabled` convention.

Round-3 fix (design-only, no backend/seam change): applied the A4 disabled treatment (opacity 0.4 + `not-allowed` cursor, no colour change, no hover) to the level card (`level-card-disabled`, added when its input is disabled) and to `.assessment-evidence-input:disabled` — token-clean. Added a screen test rendering `editable:false` asserting (a) the read-only notice shows, (b) every level card / evidence textarea / Save+Submit is disabled, (c) vitest-axe stays clean. `./scripts/verify.sh` re-ran ALL GREEN (frontend 93 tests). Only hap-design-reviewer re-reviews (code/domain/red-team sign-offs stand over a CSS+test delta).

### L3 panel — FINAL sign-offs (all four, zero blocking)
- **hap-code-reviewer — SIGN-OFF** @ `531b83f`: invitation gating on all three paths pre-lock; dimension-membership 422 pre-write; sole-implementer reflection guard verified; verify green first-hand (PrivacyReporting 131).
- **hap-domain-specialist — SIGN-OFF** @ `531b83f`: gating satisfies US1/FR-002/FR-005; 404 disposition correct; dimension-membership 422 spec-consistent.
- **hap-red-team — SIGN-OFF** @ `531b83f`: both gaps closed (not relocated); no bypass across all four vectors.
- **hap-design-reviewer — SIGN-OFF** @ `c7e071e`: read-only disabled state now A4-consistent + tested.

### Dev clock-out
Measured dev wall-clock: **102 min** (start `2026-07-21T23:04:23Z` → end `2026-07-22T00:45:41Z`), logged to frontmatter. Well under the `dev: L` estimate (agent time ≪ human-equivalent) — no overrun note needed. `.wallclock-HAP-8-dev` deleted. Status → **qa**. Final Dev tip: `c7e071e`. QA is a fresh `hap-qa` spawn (adversarial L3).

### Carry-forwards recorded (do NOT build here)
- **MSB3277** EFCore 8.0.4-vs-8.0.8 conflict in the test projects is **pre-existing** (not this diff) — for the scheduled L2 dependency-alignment story; versions NOT bumped here.
- **G2 defense-in-depth (red-team):** even with write-side invitation gating, rollup/Harris queries MUST inner-join `cycle_invitations WHERE Excluded=false` so the entitled population can't desync from the reported one — carry-forward for **HAP-10/HAP-11/HAP-19**.

## QA — fresh-instance adversarial pass (2026-07-22)

Fresh `hap-qa` spawn, no shared context with Dev. Re-derived correctness from the acceptance criteria,
`specs/001-maturity-initiative-register/spec.md` (FR-007/062/066, FR-002/003/004/005), data-model.md
Assessment/AssessmentScore, contracts/api.md "Self scope", research D1, and DR-0005/Q-014/Q-015/Q-016/
Q-017a — not from Dev's notes above. This is the FIRST story writing real Assessment/AssessmentScore
rows, so the §9.3 mandatory attempts below are exercised in full against the running system (real HTTP
calls into a `WebApplicationFactory` + disposable Postgres via `verify.sh`, not by trusting Dev's tests).

### Verdict: PASS — every AC clause verified, zero violation paths found (all attempts documented below)

### Per-clause verification (literal, against the running system)
1. **`GET /api/me/assessment` — 7 dimensions + descriptors from data, no hard-code, FR-062 pre-population, FR-066 key.** PASS. `Get_returns_seven_dimensions_with_descriptors_prior_null_and_the_purpose_key` proves 7 dims/4 levels each, sourced from `_db.Dimensions`/`_db.LevelDescriptors` (no literal dimension/level text anywhere in `Hap.Api`/`app/src` — spot-checked by grep, and `app/src/__tests__` carries an explicit "framework grep-guard" comment forbidding real content in fixtures). `Get_pre_populates_with_the_prior_cycle_scores_cycle_n_plus_1_shows_cycle_n_values` proves cycle N+1 shows cycle N's scores as `PriorScore`. `purposeLimitationKey` = `"assessment.purposeLimitation"` confirmed on the wire; `app/src/strings/en.ts` carries the actual FR-066 "development, not performance management" statement, rendered by `PurposeBanner` (`role="note"`, non-dismissible).
2. **`PUT …/scores` partial 0–3 upsert; reopening restores in-progress; `POST …/submit` InProgress→Submitted; writes after submit → 409.** PASS. Dev's `Put_upserts_partial_progress_and_a_later_get_restores_the_in_progress_values` (5-of-7 restore), `Submit_requires_all_dimensions_scored_then_transitions_to_submitted`, `A_score_write_after_submit_returns_409` all independently re-read and re-verified against the code (`SeamAssessmentStore.UpsertSelfScoresAsync`/`SubmitSelfAsync`), not merely trusted. Score range 0–3 confirmed both at the seam (`SelfScoreRangeException`) and the domain entity (`AssessmentScore.Validate`) — defense in depth.
3. **All data access via the seam gateway; architecture test green; `Category=PrivacyReporting`.** PASS. `SeamBoundaryTests` (source-scan) + `SeamStoreImplementationTests` (reflection: `SeamAssessmentStore` is the ONLY production implementer of `IAssessmentStore`/`ISelfAssessmentStore`) both green. Read the guard's allowlist and canary/vacuity tests myself — not vacuous, not evadable by a second port implementation or a raw `Set<Assessment>()` outside the seam folder.
4. **UI mockup states: LevelSelectorCards, ProgressStepper "x of 7" + floor, PurposeBanner above the fold, evidence textarea, save+submit per A4.** PASS by inspection of `AssessmentSelfScreen.tsx`/`LevelSelectorCard.tsx`/`ProgressStepper.tsx`/`PurposeBanner.tsx` plus `assessment-self-screen.test.tsx` (9 tests) — native radio-group semantics, `aria-live` progress announcement, PurposeBanner first in the main column.
5. **5-of-7 → floor L0 component test.** PASS — `progress-stepper.test.tsx`'s `renders the 5-of-7 incomplete state with a projected floor of L0` is a direct, literal match to this clause.
6. **vitest-axe + keyboard-only (SC-007).** PASS — axe assertions in both the ready state and the read-only state (0 violations, color-contrast excluded for jsdom as documented); a dedicated keyboard-only test (Tab-focus, Space, ArrowRight) drives selection with no mouse event.
7. **Strings externalised; tokens only.** PASS — grepped `app/src/components/{LevelSelectorCard,ProgressStepper,PurposeBanner}` and `AssessmentSelfScreen` CSS for hex/rgb literals outside `var(--…)`: zero matches. Grepped the TSX for bare capitalised JSX text nodes: zero matches — every string routes through `strings.assessment.*`.
8. **QA adversarial L3 attempts, documented.** See §9.3 below.
9. **`verify.sh` green.** PASS — see Verify run below.

### §9.3 mandatory adversarial attempts (assessment-data write path)

**(a) Cross-person read/write as each of the seven seeded roles.** Dev's own suite proved this only for
MGR1 against EMP1. I re-derived the claim structurally first: `AssessmentEndpoints.cs` derives `personId`
exclusively from `http.User.FindFirstValue("person_id")` — the session claim set server-side at
`/auth/signin` by `LocalDevProvider`, carried in an `HttpOnly` cookie (`IdentityServiceCollectionExtensions`).
No route parameter, JSON body field, or query string is ever consulted for the subject; the frontend
client (`app/src/api/client.ts`) never sends one either. **Attempted, not just inferred:** added
`No_seeded_role_can_read_another_persons_self_assessment_via_the_self_endpoints` (`[Theory]`, all 7
canonical seeded roles — Individual, Manager, BU Lead, Group Leader, Portfolio Leader, HIG Executive,
Platform Admin, via the real 23-BU canonical generator) — a victim Individual enters a fingerprinted
score (3, evidence `"QA-VICTIM-FINGERPRINT-HAP-8"`) and each other role signs in and calls
GET/PUT `/api/me/assessment*`; the fingerprint never appears in any other role's response, and the
victim's data is provably untouched afterward. Also added
`Injecting_a_personId_via_body_query_string_or_header_has_no_effect_on_the_subject`: MGR1 splices
`personId`/`subjectPersonId`/`targetPersonId` fields naming EMP1 into a raw JSON PUT body, a `?personId=`
query string on GET, and `X-Person-Id`/`X-Subject-Person-Id` headers — all three are silently ignored;
the subject is always the caller. **Outcome: no cross-person read/write path exists — structurally
impossible (no personId parameter anywhere), and now proven by direct attempt across every seeded role,
not just asserted from code reading.** I also confirmed `AssessmentReads` (the cross-person gateway
built in HAP-5) is **not wired to any HTTP endpoint** in this story (`grep` for its usage found only its
own definition + DI registration) — consistent with the story's own binding note that Q-015's residual
keeps a live individual-read endpoint off until the BU-tier cap lands. There is, today, no cross-person
assessment-read endpoint in the system at all to attack beyond the self-scope one just exercised.

**(b) Invitation gating — ordering/timing windows.** Dev's suite covered PUT-only for the not-onboarded
case and GET+PUT+submit for the excluded contractor but never checked GET/submit specifically for the
not-onboarded person, nor a submit-with-no-prior-GET-or-PUT ordering probe. Added
`A_not_onboarded_bu_person_gets_404_on_GET_and_submit_too_not_just_PUT` (closes the gap: both now 404,
no row) and `An_excluded_contractor_gets_404_on_submit_with_no_prior_GET_or_PUT_ordering_probe` (submit
cold, no session history — still 404, no row, no first-call-only gate). Read `CycleService.OpenAsync`
and `CycleInvitation` myself: the invitation snapshot is generated **once**, in the same transaction as
Open, over every currently-active person — there is no mutation path afterward (no "exclude mid-cycle"
endpoint exists), so there is no window where an invitation row's `Excluded` flag could be toggled after
the fact. `EnsureInvitedAsync` re-checks the invitation on every GET/PUT/submit call (not just the
first), closing any first-call-only gate. **Outcome: no ordering/timing bypass found; examined the
invitation-generation transaction and the per-call gate directly — there is no code path by which an
uninvited or excluded person can land an Assessment row.**

**(c) Dimension membership — a genuinely foreign dimension, not just an unknown Guid.** Dev's tests use
an unknown random Guid and `Guid.Empty`. I white-boxed a stronger case: created a real, persisted
`Framework`/`FrameworkVersion`/`Dimension` row belonging to a **different** FrameworkVersion than the
cycle under test resolves to, then PUT that dimension id against EMP1's real open cycle
(`Put_rejects_a_real_dimension_belonging_to_a_different_framework_version_422_nothing_persisted`).
Result: clean 422, no Assessment row persisted — proving `ValidateDimensionsAsync` checks **membership of
the resolved cycle's framework version**, not merely "does this id exist in the database anywhere."
**Outcome: no phantom-score path found.**

**(d) Submission lock — probing "current cycle" resolution.** Beyond Dev's close→423→override→accepted
sequence, I added two boundary probes Dev's suite didn't cover: (i)
`A_draft_never_opened_cycle_is_treated_as_no_current_cycle_not_423` — a cycle that exists but was never
opened must resolve as "no current cycle" (404 GET / 409 write), never accidentally treated as current
or 423-locked; confirmed by reading `CurrentCycleAsync` (only queries `Open` and `Closed` states,
excludes `Draft` entirely) and by direct HTTP attempt. (ii)
`A_late_override_on_a_closed_cycle_does_not_apply_to_a_later_newly_opened_cycle` — grants EMP1 a
late-override on cycle A (closed), opens a brand-new cycle B for the same framework, and proves cycle B's
own lock (once B is also closed, with no override) is NOT bypassed by A's override; `HasLateOverrideAsync`
is scoped by `(CycleId, PersonId)`, so this is correct by construction, and the attempt confirms it.
**Outcome: no wrong-cycle write or write-past-the-lock path found.**

**(e) Seam boundary — evadability.** Re-derived independently rather than trusting Dev's claim: read
`SeamBoundaryTests` (source-scan over `backend/src`, case-sensitive on the PascalCase type tokens plus
the `DbSet<>`/`Set<>()` query-surface regexes) and `SeamStoreImplementationTests` (reflection: exactly
one concrete type implements `IAssessmentStore`/`ISelfAssessmentStore` in the production assembly). Both
carry their own negative-case proofs (`Guard_flags_a_reference_outside_the_sanctioned_locations_but_not_
inside_them`, the vacuity canary) — I verified those proofs are themselves sound (a synthetic leak in
`Hap.Domain/Leak.cs` outside the allowlist IS caught; the identical content inside the seam or inside the
`Assessments` definition folder is NOT). Confirmed `HapDbContext` exposes no public `DbSet<Assessment>`
and the tables are mapped only via `AssessmentEntityConfiguration` (an allowlisted schema file, not a
query path). **Outcome: no second implementation and no raw query path exists outside the seam; both
guards are non-vacuous and not trivially evadable (a second port implementer or a bare `Set<>()` call
anywhere else in `backend/src` fails the build).**

### Negative-path tests added (QA work — `backend/tests/Hap.Api.Tests/AssessmentEndpointsQaAdversarialTests.cs`, 8 fact/theory blocks, 14 test cases, all `Category=PrivacyReporting`)
Beyond the five mandatory items above, also added:
`Concurrent_submit_calls_never_both_succeed_with_corrupted_state_and_never_500` — two simultaneous
`POST /submit` calls for the same person/cycle (`Task.WhenAll`) must never both silently "win" into a
corrupted state or surface a 500; the unique `(CycleId, PersonId)` DB index plus the forward-only
`Assessment.Submit()` state machine resolve the race to exactly one persisted row in `Submitted` state,
with every response either 204 or 409 — confirmed by direct attempt, not just by reading the index
definition (no explicit EF concurrency token is configured, so this was worth attempting rather than
assuming).

### Verify run
`./scripts/verify.sh` ran **ALL GREEN** at branch tip `2b16406` + QA test file (pre-commit): backend
build warnings-as-errors (2 pre-existing MSB3277 warnings, carried-forward per Dev's note, not
introduced here); all four migrations idempotent (second `ef database update` no-ops); full backend
suite `Hap.Api.Tests` **242/242 passed** (Dev's 228 + 14 new QA tests, 2m20s); `Category=PrivacyReporting`
filtered suite **145/145 passed** (5 Architecture + 140 Api — Dev's 131 + 14, all 14 new tests tagged);
`Hap.Domain.Tests` 48/48, `Hap.Architecture.Tests` 9/9, `Hap.Synth.Tests` 41/41; frontend lint/typecheck
clean, `vitest` **93/93 passed**, production build succeeded, no external font in `dist`. Two QA-authored
test bugs were found and fixed during this pass itself (a JSON-anonymous-type serializer conflict, and
an assertion that incorrectly expected an attacker's own legitimately-written row to be empty) — both
were bugs in the QA test code, not in the system under test; corrected before the final green run.

### QA clock-out
Measured QA wall-clock: **18 min** (start `2026-07-22T00:47:31Z` → end `2026-07-22T01:05:54Z`), logged
to frontmatter. Well under the `qa: M` estimate — no overrun note needed. `.wallclock-HAP-8-qa` deleted.
**Status left at `qa`** — closure (squash-merge, closure record, change-log row, board regen) is the
session lead's per CLAUDE.md §10; QA does not self-approve to `done`.
