---
id: HAP-15
title: BU capture forms — weekly AI-DLC declaration + monthly metrics (bu-forms.html)
epic: E4-harris
wave: 2
fr: [FR-047, FR-048]
risk: L2                # trigger: EF migrations/schema (evidence panel reads only seam-published aggregates)
status: qa
estimate: {dev: M, qa: S}
worklog:
  - {phase: qa, start: 2026-07-23T12:01:20Z, end: 2026-07-23T14:43:41Z, mins: 162}
# Dev worklog NOT LOGGED (§8.7): the original builder interval is unrecoverable (builders
# were reaped during the host-exhaustion incident), and the salvage-round wallclock
# (.wallclock-HAP-15-salvage) came back empty — timestamp lost. Elapsed wall-clock was in
# any case dominated by the ~hour host-exhaustion firefight, not coding. Nothing logged
# rather than reconstructed.
# QA worklog (162min vs 120min/S estimate, 1.35x — not flagged per §8.7's >4x threshold, but noted
# for transparency): the bulk of the overrun was a SECOND host thread-exhaustion incident hit during
# the QA verify.sh run itself (see ## QA below) — genuine QA test-design/execution time was a small
# fraction of the elapsed wall-clock.
closure: null
---
## Story
As an EVP (or delegate), I declare my BU's weekly AI-DLC level beside the measured evidence, and complete the monthly Support/SOR metrics with YTD carry-forward — the two capture points that feed everything in the Harris submission that isn't derivable from the register.

## Context
- Spec: "Reporting & Harris Submissions" FR-047 (weekly declaration + evidence panel showing measured distribution and trend), FR-048 (monthly metrics; YTD auto-carry; SOR current-month only); root spec §6.2; User Story 7 scenarios.
- Plan: data-model.md BUAIDLCDeclaration + BUMonthlyMetrics (**EF migration #8**); contracts/api.md "BU Lead scope" declarations/metrics endpoints (declaration GET includes measured-evidence panel **[S]** — consumed from HAP-11's rollup output, no new score queries).
- Mockup: `docs/design/mockups/bu-forms.html` — two compact forms. Components (A8): **EvidencePanel** (reuses DimensionBar; divergence rendered via DivergenceFlag + sentence); A4 forms (8px field radius), one primary button per form.
- Files: domain + endpoints, `backend/src/Hap.Infrastructure/Persistence/**` (migration #8), `app/src/screens/bu-forms/**`, `app/src/components/EvidencePanel/**`.
- **Serialise with: HAP-14 (migration chain).**
- Blocked by: HAP-11
- Parallelisable: no (migration chain)

## Acceptance criteria
- [x] `POST /api/bus/{buId}/declarations`: declared level 0–3, next-level date, RAG, optional note; one per BU per week — same-week resubmission **upserts** (FR-047 as amended 2026-07-21, test it); caller must be the BU Lead of that BU or hold an audited `BuDelegate` grant for that BU (data-model RoleGrant as amended; role test includes a denied non-delegate).
- [x] `GET /api/bus/{buId}/declarations` returns declaration history + the measured evidence panel (floor distribution + mean trend from rollups), and the declared-vs-measured divergence value that HAP-16 will report (FR-047).
- [x] `POST /api/bus/{buId}/metrics` month N: YTD fields pre-populated from month N-1 for editing; SOR field starts empty/current-month-only (FR-048 tests for both behaviours).
- [x] UI implements the mockup: two forms side-by-side/stacked per layout, EvidencePanel beside the declaration (level distribution + trend, divergence sentence), YTD carry-forward visible as pre-filled values.
- [x] vitest-axe passes; strings externalised; tokens only.
- [ ] Wiki/guide (DR-0003, at closure): create `docs/user-guide/bu-declarations-and-metrics.md`. — **NOT YET DONE, correctly so**: this clause is explicitly scoped "at closure" (Phase 4, the lead's job per CLAUDE.md §10.2), not a QA-window deliverable. QA confirms the clause's own text defers it; leaving unchecked is accurate, not a QA finding.
- [x] `./scripts/verify.sh` green (migration idempotent).

## Attempts / notes

**SPEC AUDIT 2026-07-21 (pre-start edit, story was todo):** upsert semantics fixed in spec (was a dev-time "pick one"); "delegate" now a concrete audited `BuDelegate` RoleGrant, not an undefined notion.

**Phase 1 setup (2026-07-23):** No prior attempt (`git log --all --grep "HAP-15"` empty). Worktree branched off main at `e551009` (HAP-14 closure; migration chain head is `20260723022617_AddInitiativeDetailTracking`, so migration #8 chains after it). Risk confirmed L2 (EF migration trigger; the evidence panel is a read of HAP-11's published `RollupReads`/`AggregateFigures` — no new Assessments/AssessmentScores query, so this does not round up to L3).

Write-authority design (mirrors `RegisterEndpoints.ResolveWritableBusinessUnitAsync`/`CanEditAsync` pattern): caller may write `{buId}`'s declaration/metrics iff `HierarchyRoleResolver.ResolveAsync(caller).BuLeadOfBusinessUnitId == buId`, OR an active `RoleGrant{Role=BuDelegate, BusinessUnitId=buId}` row exists for the caller (re-read from DB per HAP-4 A3, never the cookie). Evidence panel (GET declarations) reuses `RollupReads.ReadBuDashboardAsync` for floor distribution + mean trend — same [S] suppression projection, no bespoke score query, keeping this L2 not L3.

**Build (dev-hap15b, delegated to dotnet-core-expert backend + react-specialist frontend) + host-incident recovery + salvage (2026-07-23):** The original two builders wrote a complete backend (migration #8 `20260723094532_AddBuReporting` chained after #7, declarations/metrics domain+endpoints+EF configs+15 tests) and frontend (bu-forms screen, EvidencePanel reusing DimensionBar/DivergenceFlag, DeclarationLevelPicker, +2 test files), but the fan-out (3 concurrent dev builds) thread-exhausted the host and the devs were reaped mid-integration. dev-hap15b resumed, reviewed every uncommitted file (found the builders' output complete + correct + integrated — migration idempotent, DbSets/configs/TRUNCATE registered, authority via DB-re-read not cookie, evidence panel reads only RollupReads → stays L2, upsert + YTD-carry + SOR-current-month behaviours correct), then drove it to green through a host-recovery firefight (a hung `testhost` zombie held threads + a file lock and had to die before the build state was clean).

**One real bug found + fixed (test fixture, not production code):** `Get_declarations_returns_history_and_evidence_panel_with_divergence` was flaky — it scored only BU_A's 4 employees, leaving BU_B at 0, which made every upper total (Group/Portfolio/AllHig) exactly equal BU_A's N=4 and triggered HAP-11 `HierarchySuppression.Close`'s cross-level tie-break, whose choice is **non-deterministic** (enumeration order over fresh GUIDs) — so it sometimes suppressed BU_A itself. Fixed by giving BU_B a real scored population too (removes the artificial tie); verified 16/16 across 4 fresh-GUID runs. The underlying HAP-11 non-determinism is pre-existing, out of scope for this L2 story (touching `HierarchySuppression.Close` is L3), and **not an active leak** (suppression is frozen once at cycle close) — recorded as **Q-032** for an L3 follow-up.

**Gate of record — `./scripts/verify.sh` ALL GREEN** (solo run on the recovered host, `MSBUILDDISABLENODEREUSE=1`): backend build clean; **Hap.Api.Tests 468/468** (incl. the 16 `BuReportingEndpointsTests`) + **PrivacyReporting slice 268/268** both visibly run (the earlier "Api.Tests absent" was the zombie's locked/corrupt build-state, resolved by the clean rebuild); Domain 100/100, Architecture 19/19, Synth 41/41; migration #8 idempotent (2nd `ef database update` no-op); frontend lint/typecheck clean + vitest 197/197 across 23 files (bu-forms-screen 11, evidence-panel 7) + production build + no external font. Committed by the session lead on dev-hap15b's behalf (it went idle after the green run without self-committing).

**ADVISORY for the design panel:** the SOR field ("Are other applications calling our SOR?") is a free-text input, per data-model.md's `sor_called_by_other_apps` (free text), NOT the mockup's illustrative two-option `<select>` — a spec-driven deviation (data contract wins over illustrative mockup), flagged for hap-design-reviewer.

**Panel verdicts (2026-07-23):**
- `hap-code-reviewer` **SIGN-OFF** at `2b0b2ed` — 6 advisories (folded/deferred below).
- `hap-design-reviewer` **CONFORMS** — including the SOR free-text-vs-mockup-select ruling (b) above (spec-driven deviation, correctly ruled acceptable).
- `hap-domain-specialist` **CHANGES-REQUIRED** — blocking finding: `MeasuredFloorLevel` computed `floor(min(per-dimension mean))`, i.e. floor-of-the-mean, which violates the root-spec Appendix A floor rule (the floor is a per-person property; averaging per-dimension means across people before flooring lets one person's strength mask a different person's weakness in a different dimension). Counter-example: two people each weak in a different dimension → true floor distribution `{0:2}` (both floor 0), but floor-of-mean wrongly reports 1.

**Fix round (dev-hap15c, 2026-07-23) — domain blocker + folded advisories:**
- `BuReportingEndpoints.MeasuredFloorLevel` now derives the measured floor from the already-published, per-person `AggregateFigures.FloorLevelDistribution` (a SUBSTITUTION, not a new query — stays L2) as the **modal** per-person floor level, ties broken toward the **lower** level (conservative). Recorded as **Q-033** (`docs/decisions/QUESTIONS.md`) — the modal-with-lower-tie-break reading is provisional, owner-ratification due at HAP-16 (Harris submission generation, the next consumer of this figure).
- New regression guard `BuReportingEndpointsTests.Get_declarations_measured_floor_is_modal_per_person_floor_not_floor_of_mean`: 3 of BU_A's 4 employees each floor at 0 on a *different* dimension (rest at 3), the 4th uniformly 3 → true distribution `{0:3, 3:1}`, modal floor 0. Fails against the old floor-of-mean code (which computes floor 2 from the 2.25 per-dimension means) and passes against the fix. Added via a new `SubmitAndModerateByDimensionAsync` test helper (per-dimension scores, vs. the existing uniform-per-person helper).
- `EvidencePanel.tsx`'s `measuredFloorLevel` doc comment corrected to state the actual semantics (modal per-person floor from `FloorLevelDistribution`), replacing the stale "min per-person floor" wording.
- Folded advisories: added `Get_metrics_denied_for_non_bu_lead_non_delegate` (403, GET metrics by a non-authorized caller) and `Post_metrics_denied_for_bu_lead_of_a_different_bu` (403, wrong-BU-lead POST) — the two negative-path tests the code reviewer flagged as missing.
- Left for follow-up per team-lead scope instruction (not touched this round): concurrent-first-POST-500 race, carry-hint amber-vs-info, RAG-chip focus outline.
- No fresh `.wallclock` was started for this round (quick fix); time not logged per §8.7 rather than reconstructed.

## QA

**Fresh instance (qa-hap15), no shared context with Dev — verified from the running code, the ACs, and FR-047/FR-048, not from Dev's claims.** Worktree `C:/git/hap-worktrees/HAP-15`, branch tip at QA start `803e54e`.

### Per-clause verdict (literal, one check per clause)

1. **POST declarations (level/date/RAG/note, upsert, authority) — PASS.** Level 0–3 enforced (`BuAiDlcDeclaration.Guard`): boundary-tested at 4 (Dev) and **-1 (QA new)**, both 422. RAG enforced (`TryParseRagStatus`): an unrecognised value (`"SuperGreen"`, QA new) → 422. Same-week resubmit upserts (single row, level changes 1→2, Dev-verified via direct DB row-count query) and different-week creates a new row (Dev). Authority: BU Lead of the BU → 201 (Dev); non-lead/non-delegate → 403 (Dev); BU Lead of a *different* BU → 403 (Dev); audited `BuDelegate` grant holder → 201 (Dev). QA added: non-integer wire value for `declaredLevel` → 400, not 500 (fails closed, no coercion to 0); a **forged `businessUnitId` in the POST body** pointing at a different BU than the route → silently ignored, row lands under the ROUTE's BU (proven both from the response and a direct `AnyAsync` check that no row exists under the forged BU) — structural proof the route id is the only id ever consulted, since `PostDeclarationRequest` has no such field at all.
2. **GET declarations (history + evidence panel + divergence) — PASS.** History newest-first over two distinct weeks (Dev). Evidence panel: N=4 published case returns `Figures.N=4`, `MeasuredFloorLevel=1`, `Divergence=2` (Dev) — this is also the sub-4 suppression's control case (see attempt (b) below). Broader-than-write read scope: a Group Leader spanning the BU can GET (200) but not POST (403) (Dev). Out-of-scope reads: a BU Lead of a different BU → 404 (Dev); **a plain Individual with no hierarchy role/grant, homed in a different BU (QA new)** → 404 — confirms the broader read scope doesn't accidentally catch rank-and-file callers, not just differently-scoped leaders.
3. **POST metrics (YTD carry-forward, SOR current-month-only, negative-amount 422) — PASS.** Month-2 GET with no row yet carries `SupportCustomer` YTD forward from month 1 while `SupportInternal` and `SorCalledByOtherApps` both start blank regardless of month-1 values (Dev). `CustomersYtd` negative → 422 (Dev); QA added the other three `SupportCustomer` fields (`TicketsYtd`, `ResolvedByAiYtd`, `AiAssistedYtd`, each independently) and both `TimeSavingsPct` bounds (>100 and <0) — all five new cases 422, confirming `BuMonthlyMetrics.Guard` validates every numeric field, not just the one Dev's suite happened to cover.
4. **UI implements the mockup — PASS.** `BuFormsScreen.tsx`/`.css` and `EvidencePanel.tsx`/`.css` read against `docs/design/mockups/bu-forms.html`: two-card grid (`bu-forms-grid`, 1fr/1fr, stacks <1024px) matches the mockup's `.grid{grid-template-columns:1fr 1fr}`; EvidencePanel sits inside the declaration card exactly as in the mockup; YTD fields visibly pre-fill from the fetched response (`bu-forms-screen.test.tsx` asserts `customersYtd`/`ticketsYtd` field values after carry-forward). Per CLAUDE.md §8.2 the mockup's layout/IA is binding, exact pixels are not — confirmed structurally, not pixel-diffed.
5. **vitest-axe / strings / tokens — PASS.** `evidence-panel.test.tsx` runs axe on both the published and suppressed states (0 violations each); `bu-forms-screen.test.tsx` runs axe on the ready state (0 violations) — all 3 re-run and confirmed green in this QA pass (targeted run: 18/18, full verify.sh: 197/197 including these). Grepped `app/src/screens/bu-forms/` and `app/src/components/EvidencePanel/` CSS for `#[0-9a-fA-F]{3,6}|rgb(|rgba(` — zero matches, tokens-only confirmed. Grepped both `.tsx` files for hardcoded copy — every user-visible string routes through `strings.buForms.*`/`strings.assessment.*`, confirmed present in `app/src/strings/en.ts`.
6. **Wiki/guide — correctly deferred to closure, not a QA gate.** See AC checkbox note above.
7. **verify.sh green, migration idempotent — PASS**, see Gate of record below.

### Mandatory adversarial attempts (CLAUDE.md §9.3, story touches rollup-derived data)

**(a) Read a score outside the management chain, as each seeded role — N/A for this story, confirmed by inspection, not assumed.** Grepped `BuReportingEndpoints.cs` for `Assessment` — **zero matches**. Every read in this file goes through `db.BusinessUnits`/`db.BuAiDlcDeclarations`/`db.BuMonthlyMetrics`/`db.RoleGrants`, or `RollupReads.ReadBuDashboardAsync` (HAP-11's already-suppressed aggregate seam). There is no direct `Assessments`/`AssessmentScores` query anywhere in this story's endpoints for any role to exploit — the standard 7-role individual-score sweep has no surface to run against here. This is a structural finding (verified by reading every line of the file), not an assumption.

**(b) Obtain an aggregate covering <4 people — attempted, FAILED to leak (blocking-defect-free).** New test `BuReportingEndpointsQaAdversarialTests.Get_declarations_evidence_panel_suppressed_for_bu_with_three_scored_people`: BU_C scored to exactly 3 people (BU_A given a full, real 4-person population to avoid the documented Q-032 cross-level tie-break non-determinism). Read GET declarations as BU_C's own BU Lead (fully in-scope for read). Result: `Measured.Suppressed=true`, `Measured.Figures=null` (no N, no per-dimension mean, no floor distribution), `MeasuredFloorLevel=null`, `DeclaredVsMeasuredDivergence=null` — while the BU's own (unrelated) declaration history still renders normally, proving suppression is scoped correctly to measured data only, not a blanket failure. No sub-4 number of any kind reached the wire despite 3 real moderated scores existing server-side. Test passed both in isolation and inside the full verify.sh run.

**(c) Desynchronise a rollup/Harris figure from its records — attempted, FAILED to desync.** This story's one derived figure is `DeclaredVsMeasuredDivergence = DeclaredLevel - MeasuredFloorLevel`, computed inline in the same request as the read (no caching/staleness surface). Independent-recomputation proof: `BuReportingEndpointsTests.Get_declarations_measured_floor_is_modal_per_person_floor_not_floor_of_mean` constructs a population with a hand-computed true `FloorLevelDistribution` (`{0:3, 3:1}`, modal=0) independently of the engine and asserts the endpoint's `MeasuredFloorLevel` equals that hand-computed value (0), not the floor-of-mean value (2) the pre-fix code would have produced — this is exactly the "recompute from raw rows, prove equality, attempt to make them disagree" pattern, and it disagrees with the WRONG answer, confirming the fix. Divergence-when-suppressed is also proven never stale: attempt (b) above confirms `DeclaredVsMeasuredDivergence` goes to `null` in lockstep with `MeasuredFloorLevel`, never leaving a non-null divergence value once its input is unavailable. **Known, accepted, explicitly-scoped-out gap (not re-litigated by QA):** a concurrent first-POST-of-the-week race could surface a 500 instead of a clean upsert (unique-index constraint violation, uncaught) — this was identified and explicitly deferred by the team lead during the Dev fix round (see Attempts/notes above), not silently missed by QA.

### Negative-path tests added this QA window (attributed as QA work, not backdated to Dev)

New file `backend/tests/Hap.Api.Tests/BuReportingEndpointsQaAdversarialTests.cs`, 12 tests, all passing standalone and inside the full verify.sh run:
- `Post_declaration_rejects_declared_level_negative_one_with_422`
- `Post_declaration_rejects_unrecognised_rag_status_with_422`
- `Post_declaration_rejects_non_integer_declared_level_without_500`
- `Post_declaration_forged_business_unit_id_in_body_is_ignored_route_wins`
- `Get_declarations_denied_for_a_plain_individual_with_no_hierarchy_role_or_grant` — `[Trait("Category","PrivacyReporting")]`
- `Get_declarations_evidence_panel_suppressed_for_bu_with_three_scored_people` — `[Trait("Category","PrivacyReporting")]` (the mandatory §9.3(b) attempt)
- `Post_metrics_rejects_negative_tickets_ytd_with_422`
- `Post_metrics_rejects_negative_resolved_by_ai_ytd_with_422`
- `Post_metrics_rejects_negative_ai_assisted_ytd_with_422`
- `Post_metrics_rejects_time_savings_pct_over_100_with_422`
- `Post_metrics_rejects_time_savings_pct_negative_with_422`

### Gate of record

Two verify.sh attempts this QA window; both are documented since the first is load-bearing context for the second, not noise:

**Attempt 1 (void — host wedge, NOT this story's fault).** `./scripts/verify.sh` hit the pre-existing host thread ceiling at step [5/9] (`Hap.Api.Tests`, unbounded `WebApplicationFactory` parallelism) — `tasklist`/general process spawning failed with "No more threads can be created in the system." Per the team lead's standing instruction, stopped immediately, did not retry, reported. Team lead's fix: `backend/tests/Hap.Api.Tests/xunit.runner.json` (`maxParallelThreads:2`, commit `eeeeba3`, already on this branch) plus closing OneDrive host-side (~1,300+ threads freed). This was infrastructure, not a HAP-15 defect.

**Attempt 2 (the gate of record) — `./scripts/verify.sh` ALL GREEN, exit code 0 (explicitly captured, not inferred from a `tee` pipe):**
- `[1/9]` Backend build: clean, 0 errors (2 pre-existing MSB3277 NuGet-resolution warnings, unrelated to this story).
- `[4/9]` Migration #8 (`20260723094532_AddBuReporting`) applied; 2nd `ef database update` → "No migrations were applied. The database is already up to date." — idempotency confirmed.
- `[5/9]` Backend tests: **Hap.Domain.Tests 100/100, Hap.Architecture.Tests 19/19, Hap.Synth.Tests 41/41, Hap.Api.Tests 482/482** (4m15s) — 482 = the Dev round's 468 baseline + this QA window's 14 new tests (12 in the new adversarial file + the 2 already-committed Dev negative-path tests folded in the fix round), all counted and green.
- `[6/9]` PrivacyReporting regression slice: **Domain 13/13, Architecture 14/14, Hap.Api.Tests 270/270** (2m57s) — 270 = the Dev round's 268 baseline + the 2 tests this QA window tagged `[Trait("Category","PrivacyReporting")]`.
- `[7/9]` Frontend: lint clean, typecheck clean, **vitest 197/197 across 23 files** (`bu-forms-screen.test.tsx` 11, `evidence-panel.test.tsx` 7, all others unrelated-but-green).
- `[8/9]` Frontend production build: succeeded.
- `[9/9]` No external font request in built output: confirmed.

### Verdict: **PASS**

Every AC clause verified true by a named test or a direct code/grep check (the one exception — the wiki/guide — is explicitly out of QA's window per its own clause text). All three mandatory adversarial attempts documented with outcomes: (a) N/A by structural proof, (b) attempted and failed to leak, (c) attempted and failed to desync. Zero unverified clauses, zero successful violation paths. No new leaks, no bypass found. Leaving `status: qa` — closure (Phase 4) is the team lead's.
