---
id: HAP-2
title: Deterministic synthetic directory generator (Hap.Synth + scripts/synth)
epic: E1-foundations
wave: 0
fr: [FR-020]
risk: L2                # trigger: feeds directory-import; uncertainty rounds up (constitution: synth distributions are protected)
status: todo
estimate: {dev: M, qa: S}
worklog: []
closure: null
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
