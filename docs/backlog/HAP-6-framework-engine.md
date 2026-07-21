---
id: HAP-6
title: Framework engine — versioned framework data seeded from docs/frameworks
epic: E1-foundations
wave: 0
fr: [FR-001, FR-054]
risk: L2                # trigger: EF migrations / schema
status: done
estimate: {dev: M, qa: S}
worklog:
  - {phase: dev, start: 2026-07-21T17:27:46Z, end: 2026-07-21T18:06:05Z, mins: 38}
  - {phase: qa, start: 2026-07-21T18:07:50Z, end: 2026-07-21T18:20:32Z, mins: 12}
closure:
  sha: 76a08ff
  files: [backend/src/Hap.Domain/Frameworks/**, backend/src/Hap.Infrastructure/Frameworks/**, backend/src/Hap.Infrastructure/Persistence/Migrations/20260721173430_AddFrameworkEngine*, backend/src/Hap.Api/FrameworkEndpoints.cs, backend/tests/**, docs/wiki/frameworks.md, specs/001-maturity-initiative-register/data-model.md]
  tests: "backend Domain 21 / Architecture 6 / Synth 41 / Api 52; migration #2 idempotent + chains behind #1; FR-054 structural guard (AddDimension/AddLevelDescriptor→EnsureMutable) + locked-version rejection tests; Art. II.4 no-hardcoded-content guard (case-insensitive, scans backend/src + backend/tests + app/src); verify.sh ALL GREEN"
  risk: L2
  panel: [hap-code-reviewer, hap-domain-specialist]
  date: 2026-07-21
  carry_forward:
    - "HAP-7 (first Lock() caller): make Dimension.Create / LevelDescriptor.Create internal (or private) so 'sole guarded path' is compiler-enforced, and add a DB/SaveChanges-layer FR-054 guard. QA PROVED the public raw-factory path (Create + DbContext.Add + SaveChanges) writes content under a locked FrameworkVersion and PERSISTS through Postgres — unreachable in HAP-6 (nothing calls Lock() yet), becomes reachable the moment HAP-7 adds the first Lock() caller. Both L2 panel members + QA flagged this; it is a hard precondition of the Lock() introduction."
    - "Content-guard coverage gap: single-word banned tokens < 10 chars are unguarded (QA proved hardcoding the dimension names 'Timing'/'Impact' passes the Art. II.4 scan). AC met (a verify-time grep test exists and runs); close with the A2 word-boundary-matching solution in a future hardening pass."
    - "Seeder does not self-heal a manually-deleted content row (deleting one LevelDescriptor then re-seeding leaves 27, not 28 — content creation is gated behind versionIsNew). Non-blocking (no admin content-integrity tooling in scope); flag for whichever story adds it."
    - "Q-012 (two simultaneously-Active versions per framework): Activate() does not demote the incumbent and Retire() is blocked once locked — logged, not built; resolve in the framework activation-workflow story."
    - "Root-spec FR-054 wording ('immutability once cycle closes') vs data-model.md/api.md ('immutable once cycle-referenced'): pre-existing spec-internal drift; dev followed the more specific data-model.md wording. Needs a reconciling decision record."
  note: "Q-011 (auto-activate a framework's first version when none is Active) logged in QUESTIONS.md, provisional in effect. NOTE: numbering collides with HAP-4's independent Q-011/Q-012 (parallel branch); reconciled at HAP-4 merge (HAP-6 keeps Q-011/Q-012, HAP-4 renumbered to Q-013/Q-014)."
---
## Story
As a Platform Admin, the AI-DLC framework (dimensions, levels, descriptors) exists as versioned data seeded from `docs/frameworks/ai-maturity-sdlc.v1.json`, immutable once a cycle uses it, so assessment content is never hard-coded and historic scores stay tied to the version they were scored against.

## Context
- Spec: "Module 1: Assessment Framework & Cycles" FR-001 (frameworks/versions/dimensions/descriptors as data), FR-054 (version immutability); "Key Entities" Framework/FrameworkVersion/Dimension/LevelDescriptor.
- Plan: data-model.md "Framework (data, not code)"; contracts/api.md `GET /api/frameworks/current`, `[PA] GET/POST /api/admin/frameworks`; constitution Art. II.4 (hard-coding a dimension name is a violation).
- Files: `backend/src/Hap.Domain/**` (framework entities), `backend/src/Hap.Infrastructure/Persistence/**` (**EF migration #2** + seeder reading the JSON), API endpoints in `Hap.Api`.
- **Serialise with: HAP-3 (migration chain — this migration lands after HAP-3's).**
- Blocked by: HAP-1
- Parallelisable: yes, with HAP-4 (disjoint files) — but only start after HAP-3 merges (migration chain)

## Acceptance criteria
- [ ] Seeder loads `docs/frameworks/ai-maturity-sdlc.v1.json` into Framework/FrameworkVersion(v1)/7 Dimensions/28 LevelDescriptors; idempotent (re-seed = no-op); content equality test against the JSON file (names, order, descriptor text).
- [ ] `GET /api/frameworks/current` returns the active version with dimensions + descriptors in display order — the payload that drives the assessment UI entirely from data.
- [ ] Immutability (FR-054): once a Cycle row references a FrameworkVersion, any write to that version or its dimensions/descriptors is rejected (domain guard test; enforced in the write path, not by trust).
- [ ] No dimension name, level name, or descriptor string appears in C#/TS source: a verify-time grep test asserts strings like "AI Delegated" and "How AI is leveraged" occur only under `docs/frameworks/` and seed output (constitution Art. II.4 guard).
- [ ] Versioning: creating v2 (draft) leaves v1 assessments untouched; `current` returns the active version only (test).
- [ ] `./scripts/verify.sh` green (migration idempotent, run twice).

## Attempts / notes

### Phase 1 — risk classification (CLAUDE.md §6.4)

Classified **L2**. Trigger: EF migrations / schema — the story adds migration #2
(`20260721173430_AddFrameworkEngine`, four new tables: frameworks, framework_versions,
dimensions, level_descriptors), chaining behind HAP-3's migration #1
(`20260721160505_InitialOrgAndAudit`). No path under `backend/src/Hap.Api/Authorization/**` or
any other L3 trigger is touched. No new NuGet/npm dependency introduced. First match against the
§7 table (schema/migrations row) wins; not rounded up further since nothing else in the diff
matches a higher row.

### Genuine ambiguity logged during Phase 1/2

- QUESTIONS.md Q-011 (dated 2026-07-21): the seeded JSON's own `"status": "draft"` field vs. the
  acceptance criterion that `GET /api/frameworks/current` must return an active version.
  Provisional answer in effect: the seeder bootstrap-activates a framework's first version only.
- QUESTIONS.md Q-012 (added during L2 panel round 1, below): no invariant enforces "exactly one
  Active FrameworkVersion per framework" — harmless today (no admin activation endpoint exists
  yet to trigger it), flagged for whichever story adds one.

### Dev — verify.sh green (first run, pre-panel)

- Date: 2026-07-21. Tip SHA at that point: `8bdf12b` (test(HAP-6): domain guard, Art. II.4
  content-hardcoding guard, endpoint + idempotency/versioning coverage).
- Result: `verify.sh: ALL GREEN` — backend build (warnings-as-errors), migration #1+#2 applied
  and re-applied idempotently against a disposable Postgres, all backend tests green (Hap.Domain.Tests
  15, Hap.Architecture.Tests 4, Hap.Synth.Tests 41, Hap.Api.Tests 46 — 106 total), the
  `Category=PrivacyReporting` suite green (unaffected — HAP-6 touches no L3 path), frontend
  lint/typecheck/vitest (66 tests)/production build all green, no external font request in the
  built output.
- Reported to the session lead for the L2 panel at this SHA.

### L2 panel round 1 (CLAUDE.md §8.6) — against tip `8bdf12b`

- **hap-domain-specialist:** BLOCKED. 1 blocking (B2 — FR-054 guard not actually wired into the
  seeder's write path), 4 advisory.
- **hap-code-reviewer:** CHANGES REQUIRED. 2 blocking (B1 — this notes section was empty; B2, same
  finding as the domain specialist), 6 advisory. Independently re-derived risk as L2 (matches).
  Re-ran verify.sh green first-hand. Confirmed migration #2 chains behind #1 and is additive-only.
- Both round-1 verdicts are recorded here per §8.6 — including the code-reviewer's refusal
  (CHANGES REQUIRED, not an approval) — before any fix commit.

### Fixes applied in response to round-1

- **B1 (this section):** recorded.
- **B2 (`6986021`):** `FrameworkVersion` gained `AddDimension`/`AddLevelDescriptor` — the domain
  content-creation path itself now calls `EnsureMutable()`, so a locked version rejects new
  content structurally rather than by the seeder remembering to check `versionIsNew`.
  `FrameworkSeeder` now calls these instead of `Dimension.Create`/`LevelDescriptor.Create`
  directly. New domain tests prove both are rejected once locked, plus a cross-version guard test.
- **Advisory A1 (case-sensitivity) and A3 (`5e6767f`):** `FrameworkContentNotHardcodedTests` now
  matches `OrdinalIgnoreCase` and scans `backend/tests` as well as `backend/src`/`app/src`.
  Extending the scan to `backend/tests` immediately surfaced a **real, not hypothetical** false
  positive: the descriptor value "Tasks" (a single common word) matched, case-insensitively,
  both a local variable named `tasks` and the substring "Tasks" inside the
  `System.Threading.Tasks` namespace — which appears in nearly every async C# file, including the
  guard's own test file. Fixed with a differentiated length floor (multi-word phrases stay at 5,
  single-word entries need 10) rather than dropping single-word content strings from the guard
  outright — this keeps both of the acceptance criteria's own named example phrases in scope
  while excluding the collision-prone single words ("Tasks", "Weeks", "Impact", "Timing",
  "Features"). This is a targeted, evidence-based fix, not the general word-boundary/regex
  solution advisory A2 describes (deliberately not built now, per the panel).
  (`RepoSource` gained `TestCsFiles()` to enable the A3 scan.)
- **Advisory (dead `FrameworkDefinition.Status` field, `f6134b3`):** removed — it was deserialised
  but never consulted (the seeder deliberately ignores the JSON's `"status"` field per Q-011);
  nothing else referenced it.
- **Advisory (LevelName not in data-model.md; two-Active-versions lifecycle gap, `059b3a8`):**
  `data-model.md`'s LevelDescriptor section now documents the `level_name` column HAP-6 shipped.
  The lifecycle gap is logged as QUESTIONS.md Q-012, not fixed here per the panel's explicit
  instruction not to build out the activation lifecycle in this story.
- **Advisory A2 (word-boundary matching) and A6 (DB-layer FR-054 guard):** noted as carry-forward
  in code comments (`FrameworkContentNotHardcodedTests.cs` class doc; `FrameworkVersion.Lock()`
  doc), not built now — A6 belongs with HAP-7 when `Lock()` gets its first real caller.
- **Root-spec vs data-model.md FR-054 wording conflict** ("once cycle closes" vs "once
  cycle-referenced"): pre-existing spec-internal drift, not a HAP-6 defect — this implementation
  follows the more specific data-model.md/contracts/api.md wording (locked the moment a Cycle
  references the version, not at cycle close). Flagging here per the panel's note; owner/lead to
  decide whether a reconciling decision record is needed.

### L2 panel round 2 (CLAUDE.md §8.6) — against tip `c4656b3`

- **hap-code-reviewer:** SIGN-OFF. B1 and B2 both resolved; verify.sh re-run green first-hand;
  migration idempotence confirmed; content-guard hardening (A1/A3 fix, differentiated length
  floor) validated.
- **hap-domain-specialist:** SIGN-OFF. Confirmed the FR-054 write-path guard
  (`FrameworkVersion.AddDimension`/`AddLevelDescriptor`) genuinely satisfies "enforced in the
  write path, not by trust"; all four round-1 advisories closed.
- Zero blocking. Both sign-offs recorded at `c4656b3`.
- Shared non-blocking carry-forward from both reviewers (not built, logged for later): `Dimension.Create`/
  `LevelDescriptor.Create` remain public static, so the "sole path" doc claim on
  `FrameworkVersion.AddDimension`/`AddLevelDescriptor` is convention, not type-enforced. Fold into
  a future hardening pass — the lead suggested alongside HAP-7's A6 (DB-layer FR-054 guard).

### Dev clock-out

- Start: `2026-07-21T17:27:46Z` (from `.wallclock-HAP-6-dev`). End: `2026-07-21T18:06:05Z`. 38
  minutes measured AI wall-clock (well under any 4× estimate threshold on the dev: M human-equivalent
  estimate — no shaving, no back-fill, timestamps taken directly).
- `.wallclock-HAP-6-dev` deleted. `status: qa` set in frontmatter. Final tip for QA: `c4656b3`.

### QA (CLAUDE.md §9) — fresh instance, no shared context with Dev

Started against branch tip `77ec35c` (worktree `C:\git\hap-worktrees\HAP-6`). Read this story file,
the constitution, FR-001/FR-054 in `specs/001-maturity-initiative-register/spec.md`,
`data-model.md`'s Framework section, `contracts/api.md`, and the seed source
`docs/frameworks/ai-maturity-sdlc.v1.json` before touching any code, per the fresh-instance brief.

**Literal acceptance-criterion verification** (re-derived independently, not trusted from Dev's
notes — verified by running the real seeder/endpoints against a disposable migrated Postgres via
`./scripts/verify.sh`, plus reading the existing `FrameworkEndpointsTests`/`FrameworkEntityTests`
line by line to confirm they actually assert what the criteria require):

- [x] **Seeder loads the JSON into Framework/FrameworkVersion(v1)/7 Dimensions/28 LevelDescriptors;
  idempotent; content-equality tested against the JSON.** PASS.
  `FrameworkEndpointsTests.Seed_loads_the_json_into_seven_dimensions_and_twenty_eight_descriptors`
  and `Seed_is_idempotent_reseeding_produces_no_duplicates` both run against real Postgres via
  `verify.sh` and pass; `GetCurrent_returns_active_version_content_matching_the_json_in_order`
  is a genuine content-equality test (positional dimension order, per-level descriptor text,
  level names — all sourced from `FrameworkSeeder.LoadDefinitionAsync`, never a hand-typed
  expectation). Confirmed the JSON itself has exactly 7 dimension objects and 4 levels ×
  7 dimensions = 28 descriptor entries by direct inspection.
- [x] **`GET /api/frameworks/current` returns the active version, dimensions + descriptors in
  display order.** PASS. `FrameworkEndpoints.cs` orders dimensions by `DisplayOrder` and
  descriptors by `Level` server-side (not relying on insertion order); the endpoint test asserts
  positionally against the JSON's own array order.
- [x] **FR-054 immutability: once a Cycle references a version, any write to it or its
  dimensions/descriptors is rejected (domain guard, enforced in the write path).** PASS **for the
  guarded path** — `FrameworkVersion.EnsureMutable()`/`AddDimension`/`AddLevelDescriptor` correctly
  reject writes once `Lock()` has been called (`FrameworkEntityTests`, all green). **However, this
  criterion's "the write path" is narrower than the actual write surface** — see Adversarial
  target (a) below: the raw `Dimension.Create`/`LevelDescriptor.Create` factories are a second,
  unguarded write path that the criterion's wording does not exclude and that a real caller (or a
  future story) can reach. Recording this as a **verified, real gap**, not a clause failure — the
  criterion as literally worded ("a domain guard test") is satisfied by what Dev built; the gap is
  that the guard is not the *only* path, which the story text's stronger framing ("immutable once
  a cycle uses it") implies it should be.
- [x] **No dimension/level/descriptor string appears in C#/TS source (verify-time grep test).**
  PASS **for phrases at or above the guard's length floor** — confirmed the guard runs over
  `backend/src`, `backend/tests`, and `app/src` and is case-insensitive. **Found a real coverage
  gap**: the differentiated length floor (`MinSingleWordBannedLength = 10`) silently drops two
  genuine single-word dimension names, "Timing" and "Impact" (6 chars each), out of the banned
  list entirely — see Adversarial target (b) below.
- [x] **Versioning: creating v2 draft leaves v1 untouched; `current` returns only the active
  version.** PASS. `Creating_a_draft_v2_leaves_v1_untouched_and_current_still_serves_v1` exercises
  exactly this and passes against real Postgres.
- [x] **`./scripts/verify.sh` green, migration idempotent, run twice.** PASS. Ran twice in full
  during this QA pass (once before fixes, once after) — both times `dotnet ef database update` ran
  twice per the script and no-opped the second time; final run is `verify.sh: ALL GREEN` end to
  end (backend build, migrations ×2, all backend test projects, PrivacyReporting suite, frontend
  lint/typecheck/vitest/build, no external font check).

**§9.3 applicability:** N/A, stated explicitly rather than skipped. HAP-6 introduces no
`Assessments`/`AssessmentScores`/rollup tables and no role-scoped read path — the framework tables
(`frameworks`, `framework_versions`, `dimensions`, `level_descriptors`) are pre-cycle reference
data, world-readable by design (`GET /api/frameworks/current` carries no auth filter, matching the
documented HAP-4/5 deferral). There is no individual score to read outside a chain, no aggregate to
under-suppress, and no Harris/rollup figure to desynchronise in this story's scope. The mandatory
§9.3(a)/(b)/(c) attempts are therefore not applicable as literally scoped; the two adversarial
targets the session lead specified in their place (raw-factory lock bypass; content-guard length
floor) are the story's actual privacy/integrity-equivalent surface (FR-054 immutability and Art.
II.4 no-hardcoding) and are covered in full below.

**Adversarial target (a) — FR-054 raw-factory bypass under a locked version.** Attempted via every
reachable path:

1. **The guarded path** (`FrameworkVersion.AddDimension`/`AddLevelDescriptor`) — attempted a write
   against a version after calling `Lock()`. **Result: correctly rejected**
   (`FrameworkVersionLockedException`). New test:
   `Hap.Api.Tests.FrameworkLockBypassQaTests.Guarded_AddDimension_path_correctly_rejects_the_same_write_the_raw_factory_allows`
   (contrast case, DB-backed).
2. **The still-public static factories** (`Dimension.Create(frameworkVersionId, ...)`,
   `LevelDescriptor.Create(dimensionId, ...)`) called directly, bypassing `AddDimension`/
   `AddLevelDescriptor` entirely — no DB involved, pure entity construction against a locked
   in-memory `FrameworkVersion`. **Result: SUCCEEDS — no exception, no guard fires.** Both
   factories take only a `Guid` (the parent id), never a reference to the owning
   `FrameworkVersion` object, so they have no way to consult `IsLocked` even in principle. New
   tests: `Hap.Domain.Tests.FrameworkRawFactoryLockBypassTests`
   (`Dimension_Create_the_raw_factory_ignores_a_locked_version_and_succeeds`,
   `LevelDescriptor_Create_the_raw_factory_ignores_a_locked_version_and_succeeds`).
3. **The raw factory + direct `DbContext.Add` + `SaveChangesAsync`, over a real migrated
   Postgres, against an actually-locked and persisted `FrameworkVersion`** — the strongest form of
   this probe. **Result: the write PERSISTS.** No domain guard, no FK constraint, no unique-index
   collision, no DB trigger stops it — the row lands under the locked version exactly as if the
   version were still Draft. New tests:
   `Hap.Api.Tests.FrameworkLockBypassQaTests.Raw_Dimension_Create_plus_direct_DbContext_Add_persists_content_under_a_locked_version`
   and `Raw_LevelDescriptor_Create_plus_direct_DbContext_Add_persists_content_under_a_locked_version`
   — both green, both prove the persisted bypass (dimension/descriptor counts under the locked
   version increase by exactly the injected row; the injected rows are found by query afterward).
4. **Any admin HTTP endpoint** — the only endpoint that writes framework content is
   `POST /api/admin/frameworks` (the seeder). Examined directly: does re-POSTing after a version
   is locked let content through over HTTP? **No** — `FrameworkSeeder`'s content-creation loop is
   gated entirely behind `versionIsNew`, so re-seeding an already-seeded `VersionNumber` is a
   no-op *by construction*, before the loop (and therefore before any `AddDimension` call) ever
   runs — lock state is never even consulted on this path because it never gets that far. This is
   a closed path, not a defeated guard. New test:
   `Hap.Api.Tests.FrameworkLockBypassQaTests.Admin_reseed_endpoint_offers_no_equivalent_bypass_because_it_no_ops_for_an_existing_version`.
   No route exists that could add a *new* dimension/descriptor to an *existing* version at all
   (the admin surface only creates whole new draft versions via `FrameworkAdminService.
   CreateDraftVersionAsync`, unwired to HTTP in this story) — so there is no fifth path to try.

**Verdict on (a): the raw-factory path is a real, exploitable bypass of FR-054, reachable by any
in-process caller (a future story's service code, a bugged migration/backfill script, or a
careless direct DB seed) — not merely a documentation gap.** The panel's round-1/round-2 "sole
path" carry-forward note was accurate in describing intent but the guard is not type-enforced: the
factories should be made `internal` (visible only to `FrameworkVersion` via the same-assembly
friend-access pattern already used elsewhere, or accessed only through `AddDimension`/
`AddLevelDescriptor`) so the compiler — not caller discipline — makes `AddDimension`/
`AddLevelDescriptor` the actual sole path. Flagging as the primary finding of this QA pass; not
blocking merge per the panel's explicit prior decision to defer this to hardening (round-1/round-2
carry-forward, "fold into a future hardening pass"), but the owner/lead should weigh whether that
deferral still holds now that the bypass has been proven to persist through the DB, not just
constructed in memory.

**Adversarial target (b) — Art. II.4 content-hardcoding guard length-floor gap.** Attempted to
defeat `FrameworkContentNotHardcodedTests` (case-sensitivity already closed per round-1 A1; scan
now covers `backend/tests` per A3):

1. Confirmed by direct inspection of `docs/frameworks/ai-maturity-sdlc.v1.json` that two dimension
   names — "Timing" and "Impact" — are single words of 6 characters each, below
   `MinSingleWordBannedLength = 10`.
2. New test `Hap.Architecture.Tests.FrameworkContentGuardCoverageGapTests.
   Timing_and_Impact_are_real_dimension_names_excluded_from_the_banned_list_by_the_length_floor`
   reflects the guard's own private length-floor constants (never re-guessing the numbers, so this
   test cannot silently drift from what the shipped guard enforces) and proves both strings are
   extracted from the JSON as real dimension names but then filtered out of `bannedFiltered` before
   any scan runs.
3. New test `Hardcoding_Timing_and_Impact_in_a_source_file_passes_the_guards_real_scan_undetected`
   goes further: writes a scratch `.cs` file (outside `backend/src`/`backend/tests`/`app/src`, so
   this never introduces an actual Art. II.4 violation into the real source tree) that literally
   hardcodes `public const string DimensionOne = "Timing";` and `DimensionTwo = "Impact";`, then
   runs the guard's *real* scan algorithm against it with the *real* banned list pulled from the
   production class by reflection. **Result: zero offenders reported** — the guard's real,
   end-to-end scan genuinely misses both strings when actually hardcoded.
4. **Confirmed the honest answer to the session lead's direct question: YES, hardcoding "Timing"
   or "Impact" in source passes the guard undetected.** This is a real, evidence-based coverage
   gap in the shipped guard, not a hypothetical one — demonstrated by running the production
   algorithm against contrived-but-representative hardcoded content, not merely by inspecting the
   length constant in isolation. Checked for accidental cross-coverage first: no retained
   multi-word banned phrase in the current JSON contains "Timing" or "Impact" as a substring, so
   there is no incidental protection either.
5. **A near-miss during this very QA pass is itself supporting evidence the guard's non-length-
   floor-affected behaviour works correctly**: this file's own first draft (this test class's XML
   doc comment) quoted the phrases "AI Delegated" and "How AI is leveraged" verbatim while
   describing the gap — both are multi-word, well above the 5-char floor, and the guard correctly
   flagged them as offenders in `backend/tests` on the first `verify.sh` run of this QA pass. Fixed
   by rewording the comment to describe rather than quote the content. Recorded here as evidence
   the guard's *general* mechanism (case-insensitive substring scan across `backend/src` +
   `backend/tests` + `app/src`) is working exactly as designed — the gap is specifically and only
   the single-word length floor, confirmed by contrast.

**Verdict on (b): confirmed defect, not blocking per the panel's prior explicit choice not to build
the general word-boundary/regex fix (advisory A2) in this story** — but the length-floor mitigation
that replaced it has a real, now-proven hole for any future single-word dimension/level name under
10 characters. Recommend the owner/lead decide between: (i) lowering
`MinSingleWordBannedLength` for the two known short words specifically (targeted, same style as
the existing fix, reintroduces some collision risk the floor was raised to avoid), or (ii) building
the deferred word-boundary/regex match (A2) so length stops being the discriminator at all. Not a
HAP-6 blocking defect (the story's own acceptance criterion is satisfied by the guard as shipped —
it is *a* verify-time grep test, which exists and runs), but a real, demonstrated hole in a
constitution Art. II.4 guard that the next story touching framework content should not assume is
airtight.

**Additional negative-path / idempotence coverage added (QA work, not Dev's):**

- `Hap.Api.Tests.FrameworkLockBypassQaTests.
  Reseeding_after_a_manually_deleted_descriptor_row_does_not_restore_it_a_real_no_self_heal_finding`
  — adversarial idempotence probe beyond Dev's clean happy-path re-seed test: manually deletes one
  `LevelDescriptor` row (simulating partial/corrupted state, not a clean "never seeded" state),
  then re-seeds. **Finding: the seeder does not self-heal** — content creation is gated entirely
  behind `versionIsNew`, so a corrupted version's missing content is silently NOT restored by
  re-seeding (descriptor count stays at 27, not 28). Not an acceptance-criterion breach (the AC
  only requires "re-seed = no-op", which this literally is), but a real, evidence-based gap for
  whichever story adds admin tooling around framework content integrity — the seeder is not a
  repair mechanism, and nothing currently detects this kind of drift.
- `Concurrent_first_time_seed_calls_never_produce_duplicate_frameworks_or_partial_content` — fires
  5 concurrent first-time `FrameworkSeeder.SeedAsync()` calls in-process against the same empty
  database. **Result: exactly one framework/version/7 dimensions/28 descriptors survive**; racing
  callers either succeed cleanly or fail with a DB-level unique-constraint conflict (both outcomes
  accepted by the test), but the end state is never duplicated or partially written. No defect
  found on this vector.

**Tests added this QA pass** (all `test(HAP-6): qa` attribution — Dev window did not write these):

- `backend/tests/Hap.Domain.Tests/FrameworkRawFactoryLockBypassTests.cs` — 3 tests (raw-factory
  bypass ×2, guarded-path contrast ×1).
- `backend/tests/Hap.Architecture.Tests/FrameworkContentGuardCoverageGapTests.cs` — 2 tests
  (banned-list exclusion proof; end-to-end undetected-hardcoding proof).
- `backend/tests/Hap.Api.Tests/FrameworkQaAdversarialTests.cs` — 6 tests (persisted lock-bypass
  ×2, guarded-path DB contrast ×1, admin-reseed-no-bypass ×1, partial-state re-seed ×1, concurrent
  first-seed ×1). 2 tests tagged `[Trait("Category", "PrivacyReporting")]` (the persisted
  lock-bypass writes) so they run on every `verify.sh` going forward, consistent with this
  project's practice of tagging FR-054/write-guard-adjacent tests even outside the strict
  Assessments/rollup scope the category otherwise targets.
- 11 new tests total. All green.

**verify.sh:** run twice this QA pass. First run (before the two fixes below) surfaced two real
issues in the QA-authored tests themselves, both fixed and re-verified:
1. `FrameworkContentGuardCoverageGapTests`'s own doc comment quoted two real banned framework
   phrases verbatim ("AI Delegated", "How AI is leveraged") — correctly caught by the guard it was
   testing (see point 5 under target (b) above). Fixed by rewording, not by weakening the guard.
2. `Reseeding_after_a_manually_deleted_dimension_row...` originally deleted a `Dimension` row that
   still had child `LevelDescriptor` rows, tripping the real `FK_level_descriptors_dimensions_...`
   FK-restrict constraint — a genuine schema behaviour, not a test bug in the production code.
   Fixed by deleting a leaf-table `LevelDescriptor` row instead (renamed test accordingly), which
   correctly exercises the intended "manual partial edit, then re-seed" scenario without an
   unrelated FK failure masking it.
- **Final run: `verify.sh: ALL GREEN`** — backend build, both migrations applied idempotently
  (twice, second no-op), all backend test projects green (Hap.Domain.Tests 21, Hap.Architecture.Tests
  6, Hap.Synth.Tests 41, Hap.Api.Tests 52 — includes all 11 new QA tests), PrivacyReporting suite
  green (2 tests match the new `[Trait]` tags plus the pre-existing HAP-3 ones), frontend
  lint/typecheck/vitest (66 tests)/production build all green, no external font request in built
  output.

**QA outcome: no blocking defects against HAP-6's acceptance criteria as literally worded — every
clause verified PASS.** Two real, evidence-based gaps were found and documented (raw-factory FR-054
bypass; Art. II.4 single-word length-floor coverage hole), both already flagged by the panel as
carry-forward/deferred rather than newly discovered blind spots, and neither breaches the letter of
this story's own acceptance criteria. Recommending the session lead/owner weigh whether the
raw-factory finding (now proven to persist through a real database, not just in-memory) changes the
"defer to future hardening" call, since HAP-7 is the next story to give `Lock()` its first real
caller. Not setting `status: done` and not merging — per the QA brief, closure is the session
lead's.

### QA clock-out

- Start: `2026-07-21T18:07:50Z` (from `.wallclock-HAP-6-qa`). End: `2026-07-21T18:20:32Z`. 12
  minutes measured AI wall-clock (well under any 4× threshold on the qa: S estimate — no shaving,
  no back-fill, timestamps taken directly).
- `.wallclock-HAP-6-qa` deleted. Final tip after this commit: see closure block (filled by the
  session lead at Phase 4).
