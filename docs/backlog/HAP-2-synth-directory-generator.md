---
id: HAP-2
title: Deterministic synthetic directory generator (Hap.Synth + scripts/synth)
epic: E1-foundations
wave: 0
fr: [FR-020]
risk: L2                # trigger: feeds directory-import; uncertainty rounds up (constitution: synth distributions are protected)
status: done
estimate: {dev: M, qa: S}
worklog:
  - {phase: dev, start: 2026-07-21T15:06:02Z, end: 2026-07-21T15:34:59Z, mins: 28}
  - {phase: qa, start: 2026-07-21T15:36:32Z, end: 2026-07-21T15:42:50Z, mins: 6}
closure: {sha: 2ef0cdf, files: [backend/src/Hap.Synth/**, backend/tests/Hap.Synth.Tests/**, scripts/synth/generate.sh, backend/Hap.sln, .gitignore, docs/decisions/QUESTIONS.md], tests: "Hap.Synth.Tests 41/41 (26 dev + 15 QA negative-path); verify.sh ALL GREEN", risk: L2, panel: [hap-code-reviewer, hap-domain-specialist], date: 2026-07-21}
---
## Story
As the platform team, we need a deterministic, seeded synthetic directory covering 23 BUs with every edge case engineered in, so all later stories build and test against realistic org data without any real employee data ever existing in this repo.

## Context
- Spec: "Functional Requirements — Module 1: Organization Structure" (FR-020 source shape); "Assumptions — Synthetic Data Only"; SC-008 scale (23 BUs, ≥10,000 people, ≥2,000 teams).
- Plan: research **D8** (deterministic hand-rolled generator; seed recorded in output; distributions live in one reviewed file). Contract: `contracts/api.md` — `IDirectorySource.FetchSnapshotAsync` DirectorySnapshot shape (this story emits exactly that JSON).
- Files: `backend/src/Hap.Synth/**` (console app; project exists from HAP-1), `scripts/synth/generate.sh` (wrapper with the canonical seed).
- Constitution X: never silently change generator distributions — the distribution table is one file with a header comment saying changes are reviewed.
- Blocked by: HAP-1
- Parallelisable: no (nothing else is unblocked yet)

## Acceptance criteria
- [ ] `./scripts/synth/generate.sh` writes `backend/src/Hap.Synth/output/directory.json` conforming to the DirectorySnapshot shape in contracts/api.md; output metadata records the seed and generator version.
- [ ] Running the script twice produces byte-identical output (determinism test in Hap.Domain.Tests or a dedicated Hap.Synth test).
- [ ] Population: 23 BUs across 6 groups / 3 portfolios; 300–800 people per BU; total ≥10,000 people; ≥2,000 distinct managers with ≥1 active report (derived teams — SC-008). All asserted by test.
- [ ] Engineered edge cases exist and are asserted by name in tests: (a) ≥1 team with exactly 3 members; (b) ≥1 BU with <4 people total; (c) ≥1 BU containing a single team; (d) ≥1 BU where one sub-team of 4 sits inside an org of 7 (complement case); (e) ≥1 person with a null manager (manager gap); (f) ≥5% contractors spread across BUs; (g) ≥1 inactive person (leaver); (h) ≥1 person flagged on_leave; (i) ≥1 manager whose direct reports include a person in a different BU (cross-BU chain); (j) ≥1 contractor who is a manager of employees (Q-006 case).
- [ ] One seeded user per role exists for the dev sign-in: Individual, Manager, BU Lead, Group Leader, Portfolio Leader, HIG Executive, Platform Admin — exported in a `seed-users.json` alongside the snapshot with stable external_refs.
- [ ] No dependency on Bogus/faker or any randomness source other than the seeded PRNG (research D8); `./scripts/verify.sh` green.

## Attempts / notes

**SPEC AUDIT 2026-07-21 (pre-start edit, story was todo):** added team-count assertion (SC-008 coverage gap) and two edge cases — cross-BU manager, contractor manager (Q-006).

**DEV 2026-07-21 (Phase 1):** Logged **Q-009** (QUESTIONS.md) — acceptance criterion 3 "300–800 people per BU" literally contradicts edge cases (b)/(c)/(d), which demand sub-4 / single-team / org-of-7 BUs. Provisional resolution in effect (non-blocking, synthetic data): the 300–800 band is asserted over the ordinary (non-engineered) BUs; whole-population invariants (exactly 23 BUs, 6 groups, 3 portfolios, ≥10,000 people, ≥2,000 managers) are asserted over all BUs. Population = 23 BUs total (3 engineered-small + 20 ordinary). Proceeding on the provisional; a reversal is a generator retune + test update.

**Risk L2 trigger (confirmed at Phase 1):** output feeds the L3 directory import (HAP-3); protected synthetic distributions live in one reviewed file (`Distributions.cs`) with a header stating changes are reviewed. No new NuGet/npm dependency (hand-rolled seeded PRNG — research D8); a new test project `Hap.Synth.Tests` uses the solution's existing xunit versions only.

**DEV 2026-07-21 (Phase 2) — gate of record green (initial):** `scripts/verify.sh` run at branch tip `cb51969`, result **ALL GREEN** (exit 0): backend build 0 warnings (warnings-as-errors), dockerised Postgres up, EF migrations applied idempotently (second pass no-op), backend suite `Hap.Synth.Tests` **26/26** passed (Domain/Api/Architecture 1/1 each), PrivacyReporting suite ran (0 matches — expected pre-seam), frontend lint + typecheck + Vitest + production build green, no external font request in built output.

**L2 panel — round 1 (2026-07-21):**
- **hap-domain-specialist: SIGN-OFF**, no blocking findings. Advisories: (1) when HAP-3 lands, document the additive `metadata` envelope in contracts/api.md (deferred to HAP-3); (2) make the "manager with ≥1 active report" operationalisation of SC-008's team count explicit — **resolved** (comment added to `Population_AtLeastTwoThousandManagersWithActiveReport`).
- **hap-code-reviewer: CHANGES REQUIRED** — derived risk L2 (matches declared); re-ran verify.sh itself (exit 0, ALL GREEN).
  - **BLOCKING:** story notes did not record the gate-of-record green run — **resolved** by this note.
  - Advisory 2 (generate.sh seed literal duplicated `CanonicalSeed`) — **resolved**: no-arg omits `--seed`, Program.cs defaults to `Distributions.CanonicalSeed`.
  - Advisory 3 (EdgeCase_C hard-coded `BuLeadRef(12)`) — **resolved**: index derived from `SingleTeamBuCode`.
  - Advisory 4 (indented STJ uses OS newline on .NET 8 → per-platform byte-identity) — **resolved**: caveat noted in `SnapshotSerializer`.

**DEV 2026-07-21 (Phase 2) — gate of record green (after advisory fixes):** advisory fixes committed at `6c06d60`; `scripts/verify.sh` re-run against that tree result **ALL GREEN** (exit 0) — backend 0 warnings, migrations idempotent, `Hap.Synth.Tests` **26/26**, PrivacyReporting ran, frontend green. Awaiting code-reviewer re-check.

**L2 panel — round 2 (2026-07-21):**
- **hap-code-reviewer round 2 (2026-07-21): APPROVED — zero blocking notes, at 194c4ec.**
- **hap-domain-specialist (2026-07-21): SIGN-OFF re-confirmed at 194c4ec** (delta reviewed, no domain concerns).

**DEV clock-out 2026-07-21:** L2 panel complete (both sign-offs). Dev wall-clock logged to frontmatter: {phase: dev, start 2026-07-21T15:06:02Z, end 2026-07-21T15:34:59Z, mins 28} — measured from `.wallclock-HAP-2-dev`, well within the `dev: M` estimate (no over-run note needed). `.wallclock-HAP-2-dev` deleted after logging. Status set `qa`; QA is a separate fresh agent (`hap-qa`).

---

## QA (2026-07-21) — fresh instance, no dev context, worktree `C:\git\hap-worktrees\HAP-2`, branch tip at start `06ba404`

**§9.3 applicability:** this story does not read assessment data or produce rollups/submissions (it emits a synthetic directory only). The mandatory chain-read / sub-4-aggregate / rollup-desync adversarial attempts and the red-team brief are **not applicable** and are explicitly skipped rather than silently omitted, per the task brief. No `Category=PrivacyReporting` tests were added (none apply to this story's surface).

### Acceptance-criterion clauses — verified literally, one by one

1. **`./scripts/synth/generate.sh` writes `backend/src/Hap.Synth/output/directory.json` conforming to the DirectorySnapshot shape; metadata records seed + generator version.** PASS. Ran `bash scripts/synth/generate.sh` for real (not just the in-process xunit tests) from a clean tree; it wrote both `directory.json` and `seed-users.json`. Independently parsed the raw JSON with a standalone Python script (not importing any C# from the repo) and asserted `persons[0]` keys == exactly `{external_ref, name, email, job_title, manager_external_ref, bu_code, employee_type, is_active, on_leave}` and `bus[0]` keys == exactly `{code, name, group, portfolio}` — the contract's `DirectorySnapshot` shape (`specs/001-maturity-initiative-register/contracts/api.md`). `metadata.seed == 20260721` (`Distributions.CanonicalSeed`) and `metadata.generator_version == "1.0.0"` both present. Note (non-blocking, already flagged as an advisory by `hap-domain-specialist` round 1 and deferred to HAP-3): the top-level `metadata` envelope is additive beyond the contract's bare `{persons[], bus[]}` — confirmed still true, no new concern.
2. **Running the script twice produces byte-identical output.** PASS — verified at the actual CLI/script level, not by trusting the xunit determinism test. Ran `scripts/synth/generate.sh` twice in succession; `sha256sum` of both `directory.json` and `seed-users.json` matched exactly across runs (`5139d58e...` / `ae78d617...` both times), and `diff -q` reported no differences.
3. **Population: 23 BUs / 6 groups / 3 portfolios; 300–800 people per ordinary BU; ≥10,000 total; ≥2,000 distinct managers with ≥1 active report.** PASS, checked against the **raw output JSON** independently (not the C# test assertions): 23 BUs, 6 distinct groups, 3 distinct portfolios, 11,084 total persons, 2,271 distinct managers with ≥1 active report, zero ordinary BUs outside 300–800 (engineered BUs BU04/BU12/BU20 correctly excluded per the Q-009 provisional), every BU populated. Q-009's recorded provisional interpretation (300–800 applies to the 20 ordinary BUs; whole-population invariants apply over all 23) holds exactly as documented — no discrepancy found between what the story records and what the generator actually does.
4. **Engineered edge cases (a)–(j), asserted by name.** PASS, all ten independently re-derived from the raw JSON (external_refs read directly from `Distributions.cs`'s public constants, not from the xunit test file, then checked against `directory.json`):
   - (a) team-3 manager `HAP-EDGE-TEAM3-MGR` → exactly 2 active reports. ✓
   - (b) `BU20` → 3 people (< 4). ✓
   - (c) `BU12` → exactly one internal manager (`HAP-BUL-12`), everyone else reports to it. ✓
   - (d) `BU04` → 7 people total; team-4 manager `HAP-EDGE-TEAM4-MGR` → exactly 3 reports; complement 3. ✓
   - (e) `HAP-EDGE-NULLMGR` → null manager, active, distinct from the legitimate root (`HAP-EXEC`). ✓
   - (f) contractors 6.92% of population (≥5%), spread across 21 of 23 BUs. ✓
   - (g) `HAP-EDGE-LEAVER` inactive; at least one inactive person exists generally. ✓
   - (h) `HAP-EDGE-ONLEAVE` on_leave=true and still is_active=true (FR-069). ✓
   - (i) `HAP-EDGE-XBU-REPORT` homed in `BU02`, managed by `HAP-BUL-01` (BU01) — cross-BU chain confirmed. ✓
   - (j) `HAP-EDGE-CTR-MGR` is `Contractor` employee_type, manages ≥1 `Employee`-type report. ✓
5. **One seeded user per role in `seed-users.json` with stable external_refs.** PASS. Read the file directly: all seven roles present (Individual, Manager, BU Lead, Group Leader, Portfolio Leader, HIG Executive, Platform Admin), each `external_ref` resolves to a real person in `directory.json`, refs match the stable constants in `Distributions.cs` (`HAP-SEED-IND`, `HAP-SEED-MGR`, `HAP-BUL-01`, `HAP-GRP-01`, `HAP-PF-01`, `HAP-EXEC`, `HAP-ADMIN`).
6. **No Bogus/faker dependency; `verify.sh` green.** PASS. `grep -ril -i "bogus\|faker" backend/` (excluding build artefacts) returns only comments stating the *absence* of such a dependency (`NamePools.cs`, and this QA pass's own test file) — no package reference anywhere in `Hap.Synth.csproj`/`Hap.Synth.Tests.csproj`. Full `./scripts/verify.sh` run: **ALL GREEN**, exit 0 — backend build 0 warnings, disposable Postgres up, EF migrations idempotent (second pass no-op), backend test suite including all 41 `Hap.Synth.Tests` (26 dev + 15 QA-added) green, `Category=PrivacyReporting` filter matched 0 tests across all four backend test assemblies (expected — no seam yet), frontend lint/typecheck/vitest/production build green, no external font request in built output.

**Verdict: PASS on every clause. No defects found.**

### Adversarial / negative-path attempts

- **Determinism at the real CLI, not just in-process:** ran `scripts/synth/generate.sh` twice from a clean tree and byte-compared via `sha256sum` + `diff -q` — identical. (Covers criterion 2 more strongly than the existing in-process xunit test alone.)
- **Seed variation is a real distribution change, not accidental constancy:** added `SeedVariation_ChangesActualPersonCountNotJustJsonText` — proves total headcount *and* at least one BU's per-BU headcount differ between `CanonicalSeed` and `CanonicalSeed+1` (the existing dev test only proved the serialised JSON *strings* differ, which would pass even under a bug that varied only a cosmetic field). Added `SeedVariation_EngineeredEdgeCasesSurviveRegardlessOfSeed` — proves the pinned fixtures (null-manager gap, leaver, sub-4 BU count) are present identically under a non-canonical seed, so "determinism" isn't an artefact of the one seed everyone tests against.
- **Malformed/hostile CLI arguments:** `--seed not-a-number` (non-zero exit, correct error text), `--totally-bogus-flag` (non-zero exit, "unrecognised argument"), `--seed`/`--out`/`--seed-users` each with no trailing value (must not crash/hang — all fail cleanly with non-zero exit and non-empty stderr), `--seed 999999999999999999999999999999` (long overflow — non-zero exit, correct error text). All passed as expected: the CLI fails closed and cleanly on every hostile input tried; no exception stack trace leaked to stdout/stderr, no hang.
- **Negative seed:** `--seed -12345` — generates successfully (exit 0, both files written). No crash from the unchecked `long`→`ulong` cast in `DeterministicRandom`.
- **PRNG boundary values:** `DeterministicRandom.Next(max < min)` throws `ArgumentException` as documented; `Next(7,7)` always returns 7 across repeated calls (degenerate range); `NextDouble()` stays within `[0,1)` across 10,000 draws.
- **Referential-integrity / uniqueness (raw JSON, independent of the dev tests):** zero dangling `manager_external_ref`s, zero duplicate `external_ref`s, zero duplicate emails, every `bu_code` resolves to a `bus[]` entry — all confirmed via the standalone Python script against the actual generated file, not by re-running the C# assertions.
- **Management-chain cycle freedom (new coverage — not previously tested):** added `Integrity_ManagementChainHasNoCycles`, walking every person's manager chain to termination with a visited-set guard across the full 11,084-person canonical population. No cycles found; every chain terminates (either at a null manager or within population-size hops).
- **Wall-clock independence:** added `Cli_OutputIsByteIdenticalAcrossDifferentTimeZones`, running the actual CLI as a subprocess once under `TZ=UTC` and once under `TZ=Pacific/Kiritimati` (UTC+14, the most extreme civil timezone) into separate temp directories, byte-comparing both output files. Identical. Also confirmed by source inspection: no `DateTime.*`/`Environment.*` reference anywhere in `Hap.Synth/**`.
- **Locale independence:** added `Generation_IsIndependentOfThreadCulture`, generating under `CultureInfo.CurrentCulture = ar-SA` (non-Gregorian default calendar, different digit/number conventions) and diffing the serialised JSON against the canonical-culture run byte-for-byte. Identical.
- **No stray `Category=PrivacyReporting` tags added** — confirmed by grep; correct, since this story's surface (a directory generator) does not touch assessment/rollup data, consistent with the §9.3 not-applicable note above.

No defects found in any attempt above.

### Tests added (QA work, honestly attributed — new file, dev's `DirectoryGeneratorTests.cs` untouched)

`backend/tests/Hap.Synth.Tests/QaNegativePathTests.cs` — 15 tests, all tagged as QA-window additions in the file header, none `Category=PrivacyReporting` (not applicable to this story):
`Cli_NonIntegerSeed_FailsWithErrorAndNonZeroExit`, `Cli_UnrecognisedFlag_FailsWithErrorAndNonZeroExit`, `Cli_SeedFlagWithNoTrailingValue_FailsRatherThanCrashingOrHanging`, `Cli_OutFlagWithNoTrailingValue_FailsRatherThanCrashingOrHanging`, `Cli_SeedUsersFlagWithNoTrailingValue_FailsRatherThanCrashingOrHanging`, `Cli_SeedOverflowingLong_FailsWithErrorAndNonZeroExit`, `Cli_NegativeSeed_StillGeneratesSuccessfully`, `DeterministicRandom_MaxLessThanMin_Throws`, `DeterministicRandom_EqualBounds_ReturnsThatValue`, `DeterministicRandom_NextDouble_AlwaysInZeroToOneExclusive`, `SeedVariation_ChangesActualPersonCountNotJustJsonText`, `SeedVariation_EngineeredEdgeCasesSurviveRegardlessOfSeed`, `Integrity_ManagementChainHasNoCycles`, `Generation_IsIndependentOfThreadCulture`, `Cli_OutputIsByteIdenticalAcrossDifferentTimeZones`.

### Gate of record

`./scripts/verify.sh` re-run after adding the QA tests: **ALL GREEN**, exit 0. Backend `Hap.Synth.Tests` now 41/41 (26 dev + 15 QA). Full log retained at session scratchpad for this run; summary: backend build 0 warnings, Postgres migrations idempotent, all four backend test assemblies green, `Category=PrivacyReporting` filter 0 matches (expected), frontend lint/typecheck/vitest/build green, no external font leak.

### QA outcome: **PASS** — zero defects, zero unverified clauses, zero successful violation paths (§9.3 not applicable to this story's surface, stated explicitly above rather than skipped silently).

**QA clock-out 2026-07-21:** wall-clock measured from `.wallclock-HAP-2-qa` (start `2026-07-21T15:36:32Z`, end `2026-07-21T15:42:50Z`, **6 mins**) — well inside the `qa: S` estimate, no over-run note needed. `.wallclock-HAP-2-qa` deleted after logging. Status left at `qa`; closure (squash-merge, change-log row, four-box) is the session lead's job, not QA's.
