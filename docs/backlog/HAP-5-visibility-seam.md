---
id: HAP-5
title: The visibility seam — chain resolver, role scopes, N<4 + complement suppression
epic: E1-foundations
wave: 0
fr: [FR-014, FR-025, FR-071]
risk: L3                # trigger: role-scope/visibility predicates + N<4 suppression logic (the seam itself)
status: todo
estimate: {dev: L, qa: M}
worklog: []
closure: null
---
## Story
As the platform, every read of assessment data must pass through one authorisation layer — management-chain resolution, role scoping, and small-group suppression with complement defense — so no query path can ever reach a score the caller isn't entitled to. This is the constitution's Wave-0 spike, hardened into production code.

## Context
- Spec: "Users and roles" (visibility table + chain rule), FR-014 (N<4 + complement suppression), FR-025 (chain-only individual reads; aggregates above BU), FR-071 (suppressed display semantics); SC-005/SC-006; "Clarifications" bullets 2 (differencing defense).
- Plan: research **D1** (seam enforcement: internal EF configs + architecture test + per-role integration tests) and **D2** (complement algorithm: suppress if n<4 OR 0 < parent.n − node.n < 4, evaluated over the fixed hierarchy with suppressed siblings excluded); constitution Art. VI.1, Art. VII Wave-0 spike.
- Files: `backend/src/Hap.Api/Authorization/**` (ChainResolver, RoleScope, Suppression, AssessmentReads gateway — the ONLY type touching Assessments/AssessmentScores DbSets), `backend/tests/Hap.Architecture.Tests/**`, integration tests in `Hap.Api.Tests`. **No migration** (Assessment tables arrive in HAP-8; the gateway compiles against the entity types and is fully unit-tested on the org side now; score-path tests extend in HAP-8).
- Blocked by: HAP-3, HAP-4
- Parallelisable: no (everything reading assessment data depends on it)

## Acceptance criteria
- [ ] ChainResolver: for the synthetic hierarchy, resolves management chains correctly incl. edge cases — manager gap (null manager → chain ends), on_leave persons still in chain, escalation lookup (manager's manager) available for FR-070 (unit tests per case, `Category=PrivacyReporting`).
- [ ] RoleScope: for each of the seven roles, the scope function returns exactly the org nodes the spec's "Sees" column grants — parameterised tests; Group/Portfolio/Executive scopes contain **no individual-read capability** by construction (FR-025).
- [ ] Suppression (research D2 algorithm): exhaustive tests against the engineered synth cases — n=3 team suppressed; sub-4 BU suppressed; single-team BU child suppressed (complement); 4-in-7 complement case suppressed; a 4+4+4 BU unsuppressed. All `Category=PrivacyReporting`.
- [ ] Suppressed results carry `{suppressed: true, reason}` and never numeric fields (FR-071) — serialisation test.
- [ ] Architecture test fails the build if any namespace other than `Hap.Api.Authorization` references the Assessments/AssessmentScores DbSets or table names (research D1); proven by a temporary violation in a test fixture compiled negative-case.
- [ ] `./scripts/verify.sh` green.
- [ ] QA (adversarial, fresh agent): construct a violation path or state exactly what was examined and why none exists (red-team brief, CLAUDE.md §9.4) — "looks fine" is not a deliverable; document in this file.

## Attempts / notes
