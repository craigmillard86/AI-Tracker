---
id: HAP-13
title: Initiative register core + list UI (register-list.html)
epic: E3-register
wave: 2
fr: [FR-026, FR-027, FR-034, FR-035]
risk: L2                # trigger: EF migrations/schema (no assessment reads; Harris taxonomy as seeded data)
status: done
estimate: {dev: L, qa: M}
worklog:
  - {phase: dev, start: 2026-07-22T21:25:05Z, end: 2026-07-23T00:41:09Z, mins: 196}
  - {phase: qa, start: 2026-07-23T00:44:38Z, end: 2026-07-23T01:15:23Z, mins: 30}
# post-QA riskTier fix (un-deferred on QA's finding) was an un-timestamped fold-in dev
# round — no wallclock captured, so per the honesty rule nothing is logged for it (the
# fix + its green gate run are recorded in "Post-QA fix" below).
closure:
  sha: 3b5f843b0b2d2f05bd4659b44d28ffe4a27305d2
  date: 2026-07-23
  risk: L2
  files: 37
  tests: "backend Api 378/378, Domain 79/79, Architecture 14/14, Synth 41/41; PrivacyReporting unchanged (Api 232, Domain 13, Arch 9); frontend 153/153; migration #6 idempotent — verify.sh green on the merge candidate AND re-run green on integrated main (HAP-12+HAP-13 together)"
  panel: [hap-design-reviewer, hap-domain-specialist, hap-code-reviewer]
  qa: "PASS (fresh hap-qa) — FR-034 create-authority role-matrix all 7 roles incl. Platform Admin, PUT authority incl. cross-BU BU-Lead + raw-JSON BU/stage smuggling, phantom categoryId, AI-DLC boundary; QA found riskTier \"99\"->201 reachable AC violation, un-deferred + fixed (Enum.IsDefined -> 422). No assessment/audit read path (L2, not L3)."
---
## Story
As a Manager or BU Lead, I can register AI initiatives classified against the Harris taxonomy and find them through a filterable, searchable list — so the group finally has a live register instead of tribal knowledge.

## Context
- Spec: "Module 2: Initiative Register" FR-026 (identity fields), FR-027 (Harris taxonomy categories incl. "Other — not group-reported"; AI-DLC level 1–3; dimensions advanced), FR-034 (Manager+ create, BU Lead curates own BU), FR-035 (search + facets); User Story 3 scenario 2.
- Plan: data-model.md "Initiative register" — Initiative + **HarrisCategory seeded table** (`group_reported=false` for Other; categories are DATA, not enums); contracts/api.md "Register" endpoints. Stage history/NR/updates are **HAP-14** — this story sets `current_stage=Idea` on create only.
- Mockup: `docs/design/mockups/register-list.html` — binding incl. **stale rows flagged** (StaleRowFlag renders from `last_update_at`; nag jobs are HAP-18) and **red-RAG rows**. Components (A8): **StaleRowFlag**; A4 DataTable (sticky header, pagination >25, right-aligned numerics), badges/chips, one primary button ("New initiative").
- Files: `backend/src/Hap.Domain/**` (Initiative), `backend/src/Hap.Infrastructure/Persistence/**` (**EF migration #6**: Initiative, HarrisCategory + seed), register endpoints, `app/src/screens/register-list/**`, `app/src/components/StaleRowFlag/**`.
- **Serialise with: HAP-10 (migration chain — this migration lands after HAP-10's).**
- Blocked by: HAP-4
- Parallelisable: yes, with HAP-12 (disjoint files)

## Acceptance criteria
- [x] HarrisCategory seeded from data (5 categories; "Other" has `group_reported=false`, customer-deployed flags correct); a grep-guard test asserts no category name string in C#/TS source (Art. II.4).
- [x] `POST /api/initiatives`: Managers and BU Leads only, **within their own BU** (roles above BU level are read-only — FR-034 as amended 2026-07-21); requires name, BU, category, AI-DLC level (1–3 validated), owner; BU Lead can create/edit any entry in own BU, not other BUs; role-matrix test includes denied create attempts by Group Leader, Portfolio Leader, and HIG Executive.
- [x] `GET /api/initiatives` supports full-text search on name/description and facets BU, category, stage, risk tier, AI-DLC level (FR-035; each facet has a test); dimension facet joins dimensions-advanced tags.
- [x] `PUT /api/initiatives/{id}` permission: owner, creator, or BU Lead of that BU (test each + a denied case).
- [x] UI implements the mockup: DataTable with columns BU, category, stage (+ Harris-mapped label), AI-DLC level badge, RAG chip, customers, last update; StaleRowFlag on rows >7d (amber) / >14d (red) with day count in text; filters panel; sticky header; pagination at >25 rows.
- [x] Level badges always print the level number; RAG chips always carry a text label (A2 colour-independence; component tests).
- [x] vitest-axe passes; strings externalised; tokens only.
- [x] Wiki/guide (DR-0003, at closure): create `docs/wiki/register.md` (extended by HAP-14) and `docs/user-guide/initiative-register.md` (list/create portions). **Created at closure (Phase 4).**
- [x] `./scripts/verify.sh` green (migration idempotent).

## Attempts / notes

**SPEC AUDIT 2026-07-21 (pre-start edit, story was todo):** creation authority bounded per FR-034 as amended — "Manager+" ambiguity removed; above-BU roles are read-only.

**Gate-of-record evidence (2026-07-22/23, dev):** `./scripts/verify.sh` ran ALL GREEN end-to-end (backend Release build, migration `20260722214357_AddInitiativeRegister` applied + verified idempotent on re-apply, frontend build) with counts: backend Domain 79/79, Architecture 14/14, Synth 41/41, Api 371/371; PrivacyReporting suite Domain 13/13, Architecture 9/9, Api 232/232; frontend 153/153 (19 test files) incl. 10 register-list-screen + 5 stale-row-flag; frontend lint/typecheck clean; no external font request. This green run is the gate of record — no re-run required per team-lead confirmation (its disposable Postgres container was uniquely named, no PID-collision with concurrent runs).

**L2 panel verdicts:** hap-design-reviewer — CONFORMS. hap-domain-specialist — SIGN-OFF (incl. concurring on the FR-034 role-precedence role-matrix and the Q-014 write-authority carve-out below). hap-code-reviewer — CHANGES-REQUIRED (3 blocking, all resolved in a follow-up edit): (1) this gate-evidence note itself, (2) added `Put_denies_bu_lead_of_a_different_bu` — the existing PUT-deny test only covered a no-anchor-at-all person, not `CanEditAsync`'s real cross-BU BU-Lead branch, (3) removed the dead, never-thrown `InitiativeNotFoundException`. Should-fix items folded in: `ParseRiskTier`→`TryParseRiskTier` now 422s an unrecognised non-null risk tier instead of silently coercing to Low (+test); renamed `Business_units_endpoint_lists_onboarded_bus`→`..._lists_all_bus` (the endpoint correctly does not filter on onboarding — the filter dropdown should show every BU); debounced the register search input ~250ms; removed the `NormaliseCustomers` no-op.

**Assumption (data-model.md gap-fill, not a spec ambiguity):** `Initiative.CreatedByPersonId` was added beyond data-model.md's literal Initiative field list — the AC's PUT-permission clause ("owner, creator, or BU Lead") requires distinguishing creator from owner (FR-026's owner is the delivery lead; the creator is whoever called POST), which data-model.md's list doesn't separately capture. Domain specialist concurred at sign-off.

**Q-014 write-authority clearance:** FR-034 create/curate authority uses `HierarchyRoleResolver`'s depth-derived labels (BU Lead / Group Leader / Portfolio Leader / plain Manager) to gate WRITE authority over non-sensitive register data — NOT the visibility-seam individual-score scope Q-014's known limitation (docs/decisions/QUESTIONS.md) actually guards. This is within the Q-014 clearance carve-out for non-assessment uses; domain specialist signed off on the role-matrix on this basis. Practical effect if Q-014 is never ratified: a mislabelled interim leadership layer (per Q-014's uniform-depth-tree assumption) could shift *register create/curate rights*, not individual assessment-score visibility.

**Deferred (not fixed, noted per L2 panel):** (1) `OwnerPersonId`/`SponsorPersonId` have no People-existence FK/check at write time (no-FK is deliberate, matching the AuditLog/OrgOverride convention elsewhere) — a data-quality follow-up, not a correctness bug. (2) The register's `search`/`dimension` facet values pass through as raw ILIKE/array-contains parameters — fully parameterised via EF (`EF.Functions.ILike`, `.Contains`), no injection surface; a `%`/`_` LIKE-wildcard character in a search term is user-controllable but not a security issue, just a minor UX quirk (a literal `%` in a name search broadens the match). Noted, not fixed. (3) ~~`TryParseRiskTier` uses `Enum.TryParse`, which also accepts numeric strings... Deferred rather than fixed now~~ — **UN-DEFERRED AND FIXED, see below.** QA proved this was a reachable, persisted AC violation (not frontend-only), so the original defer-as-advisory call was superseded.

**Gate-of-record evidence — SUPERSEDES the 371/371 entry above (2026-07-23, post-panel-fix re-run):** `./scripts/verify.sh` re-run ALL GREEN on the fixed tree — backend Domain 79/79, Architecture 14/14, Synth 41/41, **Api 373/373** (the +2 over the prior run are the two panel-added tests: `Put_denies_bu_lead_of_a_different_bu`, `Create_rejects_unrecognised_risk_tier_with_422`); PrivacyReporting suite Domain 13/13, Architecture 9/9, Api 232/232 (unchanged — neither new test is seam-gated); frontend 153/153 across 19/19 test files (register-list-screen 10/10 incl. the debounce-aware search assertion, stale-row-flag 5/5); migration `20260722214357_AddInitiativeRegister` re-verified idempotent (second `dotnet ef database update` reported "No migrations were applied. The database is already up to date."); frontend lint/typecheck/build clean; no external font request. This 373-count run is the gate of record for the merge candidate's actual tree.

---

## QA (2026-07-23, fresh instance, no shared context with dev)

**Verdict: PASS.** Every AC clause verified literally against the running code (not against dev's notes); 6 QA-added tests (all passing, all in `backend/tests/Hap.Api.Tests/RegisterEndpointsTests.cs`, tagged honestly as QA additions in their doc-comments — none backdated to dev); `./scripts/verify.sh` green end-to-end after the additions (Api 378/378, up from 373/373; PrivacyReporting suite unaffected — 232/232 Api, 13/13 Domain, 9/9 Architecture, all pre-existing, none of this story's endpoints are seam-gated by design). This is an L2 story with no read path over `Assessments`/`AssessmentScores` — confirmed by inspection (`RegisterEndpoints.cs` queries only `db.Initiatives`/`db.HarrisCategories`/`db.BusinessUnits`/`db.FrameworkVersions`/`db.Dimensions`; no assessment table referenced anywhere in the file) — so the CLAUDE.md §9.3 "read a score outside the chain" / "<4-person aggregate" / "rollup-desync" mandatory attempts are **N/A for this story**, as the assigning message anticipated. Adversarial energy went into the register's own authority + integrity model instead, per the assignment brief.

**AC-clause verification (literal, one line per clause):**
1. HarrisCategory seed (5 categories, Other `group_reported=false`, customer-deployed flags) — **PASS**, `Harris_taxonomy_seeds_five_categories_with_correct_flags`; grep-guard genuine (`HarrisTaxonomyContentNotHardcodedTests`, dynamically loads banned strings from the JSON, not hardcoded itself) — confirmed by reading the taxonomy JSON directly (5 multi-word names, all above the 5-char floor, no single-word edge case to sneak past the 10-char floor).
2. POST authority (Manager/BU Lead, own BU, required fields, role-matrix incl. denied Group/Portfolio/HIG Exec) — **PASS**, plus QA closed the 7th role: Platform Admin was untested by dev (`QA_platform_admin_cannot_create` — added, passes: falls through to the "Individual" branch since ADMIN holds `OrgRole.PlatformAdmin`, not `HigExecutive`, and has no reports/anchor in the fixture).
3. GET facets + search (BU/category/stage/riskTier/aiDlcLevel/dimension, each with a test) — **PASS**, all 6 present with dedicated tests; full-text search case-insensitive on name (ILIKE, parameterised).
4. PUT authority (owner/creator/BU-Lead-of-BU allow, denied case) — **PASS**, plus the cross-BU BU-Lead branch specifically (`Put_denies_bu_lead_of_a_different_bu`).
5. UI mockup implementation (DataTable columns, StaleRowFlag thresholds, filters, sticky header, pagination >25) — **PASS**, verified against `RegisterListScreen.tsx`/`.css` and the passing `register-list-screen.test.tsx` (10 tests) + `stale-row-flag.test.tsx` (5 tests, pre-existing — dev's suite already had a standalone component test file, contrary to my initial `find` miss).
6. Level badge always prints number / RAG chip always carries label — **PASS**, `strings.assessment.levelAbbrev` prints the raw unclamped level (`L${level}`), colour ramp is a separate class; RAG chip label from `strings.register.ragLabels`, colour is reinforcement only. Verified in source and in the passing component-behaviour test.
7. vitest-axe / strings externalised / tokens only — **PASS**, `has no detectable accessibility violations` test green; `RegisterListScreen.css`/`StaleRowFlag.css` use only `var(--...)` tokens, no hardcoded hex/px colour/radius/shadow/type-size values found on inspection.
8. Wiki/guide — **out of scope for QA**, correctly deferred to Phase 4 closure per the AC's own "(at closure)" qualifier; confirmed neither `docs/wiki/register.md` nor `docs/user-guide/initiative-register.md` exists yet (correct — premature creation would itself be drift).
9. `./scripts/verify.sh` green, migration idempotent — **PASS**, re-run twice (once pre-QA-additions inherited from dev at 373/373, once post-QA-additions at 378/378); both runs show the second `dotnet ef database update` as a no-op ("No migrations were applied").

**Adversarial attempts (FR-034 create authority, PUT authority, categories-as-data, "Other" exclusion, riskTier gap) — all documented with outcome:**
- **Forged BU in POST body** (dev's `Bu_lead_and_manager_cannot_create_in_another_bu`, re-verified by inspection): `ResolveWritableBusinessUnitAsync` returns the caller's OWN writable BU; the handler then compares it to `request.BusinessUnitId` and 403s on mismatch — the request body's BU can never smuggle a different BU past the caller's actual scope. **No path found.**
- **Above-BU leader exploiting the "structurally a manager" fallback**: read `ResolveWritableBusinessUnitAsync` line-by-line — Portfolio Leader and Group Leader anchors are checked and return `null` BEFORE the bare `IsManager` fallback is ever reached, so a Group Leader (who IS `IsManager=true` structurally, per the class doc's own warning) cannot fall through to the plain-manager branch. Confirmed by `Above_bu_roles_and_plain_individual_cannot_create` (GRPLEAD_A, PFLEAD both denied). **No path found**, and completed the matrix with Platform Admin (see above) — also denied.
- **PUT: forged `businessUnitId`/`currentStage` via raw JSON** (beyond the typed client): `UpdateInitiativeRequest` doesn't declare either property, and the API sets no `[JsonUnmappedMemberHandling]` (default STJ behaviour is to silently skip unknown members) — QA test `QA_put_ignores_forged_business_unit_and_stage_fields_in_raw_json` posts an anonymous object with both fields forged (cross-BU + skip-ahead-to-Production) and confirms both are silently ignored; the initiative's actual BU and stage are unchanged. **No path found.**
- **PUT: BU Lead of a different BU** — `Put_denies_bu_lead_of_a_different_bu` (dev, panel-required fix): 404. **No path found.**
- **Categories/stage-map as DATA (Art. II.4)**: grep-guard confirmed genuine, not a hardcoded banned-list (see clause 1 above); manually re-read `harris-taxonomy.v1.json` for a name short/single-word enough to evade the 10-char single-word floor — none of the 5 names are single-word, all comfortably clear the 5-char multi-word floor. **No evasion found.**
- **"Other" category `group_reported=false`**: confirmed by dev's `Harris_taxonomy_seeds_five_categories_with_correct_flags`, cross-checked directly against the seed JSON (`other-internal`: `group_reported: false`). **Correct.**
- **RiskTier `Enum.IsDefined` gap (per the assignment brief — "confirm whether exploitable/persisted, don't fix")**: **CONFIRMED exploitable and persisted.** `TryParseRiskTier` uses a bare `Enum.TryParse`, which — unlike a named-but-unrecognised value ("Medium", which correctly 422s) — accepts ANY numeric string and produces an undefined `RiskTier`. QA test `QA_finding_numeric_risk_tier_bypasses_422_and_persists_undefined_value`: POST with `riskTier: "99"` returns **201 Created** (not the 422 the AC's "unrecognised → 422" language implies), the response DTO's `riskTier` is the literal string `"99"`, and a direct SQL read of the `initiatives.RiskTier` column confirms the same `"99"` is what's actually stored (via `HasConversion<string>()`, which calls the undefined enum's `.ToString()`). This is reachable by ANY authenticated Manager/BU Lead calling the API directly (curl/Postman), independent of the typed frontend client the dev notes' deferral reasoning relies on ("the typed frontend client never sends a numeric string") — the frontend being well-behaved doesn't close the API-level gap. **Recommendation: worth un-deferring** — it's not a G1/H3 privacy leak and doesn't touch the authority model, but it is a genuine, currently-reachable data-integrity defect that contradicts the AC's own literal text, and a future consumer of `RiskTier` (e.g. any subsequent Harris-adjacent rollup or filter keyed on risk tier) would silently see an undefined value rather than a clean validation failure. Not blocking this story's QA pass (dev/panel already made an informed, documented deferral decision before I arrived) — flagging for a follow-up story/task rather than reopening this one.
- **Unknown categoryId (phantom FK)**: untested by dev — added `QA_create_rejects_unknown_category_id_with_422`, confirms a well-formed-but-nonexistent category GUID 422s before ever reaching `Initiative.Create`. **Correct, no integrity break.**
- **AI-DLC level lower boundary**: dev only tested the upper out-of-range case (4); added `QA_create_rejects_ai_dlc_level_zero_with_422` for the lower boundary (0). **Correct, 422s.**

**New tests added (QA work, this session, all in `RegisterEndpointsTests.cs`):** `QA_platform_admin_cannot_create`, `QA_create_rejects_ai_dlc_level_zero_with_422`, `QA_finding_numeric_risk_tier_bypasses_422_and_persists_undefined_value`, `QA_put_ignores_forged_business_unit_and_stage_fields_in_raw_json`, `QA_create_rejects_unknown_category_id_with_422` — 5 new methods (the riskTier one required a one-line self-fix to its own SQL scalar query, `SqlQueryRaw<string>` needing the result column aliased `AS "Value"`; verified standalone against a disposable manual Postgres before the final gate run, not counted as a production defect). None are `Category=PrivacyReporting` — this story's endpoints are not seam-gated (register data, not individual assessment data), consistent with the dev's own class-doc rationale.

**QA gate-of-record:** `./scripts/verify.sh` ALL GREEN with the QA additions in place — backend Domain 79/79, Architecture 14/14, Synth 41/41, **Api 378/378** (373 dev + 5 new QA tests); PrivacyReporting suite unchanged (Domain 13/13, Architecture 9/9, Api 232/232); frontend 153/153 across 19 test files; lint/typecheck/build clean; migration idempotent on re-apply.

**Status left at `qa`** — closure (Phase 4: squash-merge, story `closure:` block, change-log row, wiki/user-guide pages, board regen, worktree teardown) is out of QA's scope per the assignment brief; not merged.

---

## Post-QA fix (2026-07-23, dev, un-deferred per team-lead directive on QA's finding)

**FIXED:** `TryParseRiskTier` (RegisterEndpoints.cs) now requires `Enum.IsDefined(value)` alongside `Enum.TryParse` — a numeric string like `"99"` (previously an undefined-but-accepted `RiskTier`) now fails the same as a named-but-unrecognised value like `"Medium"`, and both POST/PUT 422 consistently. Null/blank still defaults to `Low` (the valid omitted-field case) — unchanged.

**Test:** QA's `QA_finding_numeric_risk_tier_bypasses_422_and_persists_undefined_value` (which documented the bug passing) renamed to `Create_rejects_numeric_risk_tier_with_422_and_persists_nothing` and rewritten to assert the fixed behaviour: `riskTier: "99"` → 422, and a DB query confirms nothing with that request's name was ever persisted. The existing `Create_rejects_unrecognised_risk_tier_with_422` ("Medium" case) is unchanged.

**Fresh gate-of-record evidence — SUPERSEDES the 378/378 QA entry above for the RiskTier fix delta:** `./scripts/verify.sh` re-run ALL GREEN — backend Domain 79/79, Architecture 14/14, Synth 41/41, **Api 378/378** (unchanged count from QA's run: the fix renamed/rewrote an existing test rather than adding one); PrivacyReporting suite unchanged (Domain 13/13, Architecture 9/9, Api 232/232); frontend 153/153 across 19/19 test files, unchanged (no frontend delta this round); migration `20260722214357_AddInitiativeRegister` re-verified idempotent ("No migrations were applied. The database is already up to date."); frontend lint/typecheck/build clean; no external font request. This run is the gate of record for the merge candidate's actual tree, superseding all prior counts.
