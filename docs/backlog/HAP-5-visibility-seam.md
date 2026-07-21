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
- Files: `backend/src/Hap.Api/Authorization/**` (ChainResolver, RoleScope, Suppression, AssessmentReads gateway — the ONLY type touching Assessments/AssessmentScores DbSets), `backend/tests/Hap.Architecture.Tests/**`, integration tests in `Hap.Api.Tests`. **No migration and no DbSet registration** (audit 2026-07-21): define the Assessment/AssessmentScore domain *types* only — registering DbSets without their migration would fail verify's pending-model-change/idempotence check. The gateway is fully unit-tested on the org side now; HAP-8 registers the DbSets + migration and extends the architecture test to the DbSet form.
- Blocked by: HAP-3, HAP-4
- Parallelisable: no (everything reading assessment data depends on it)

## Acceptance criteria
- [ ] ChainResolver: for the synthetic hierarchy, resolves management chains correctly incl. edge cases — manager gap (null manager → chain ends), on_leave persons still in chain, escalation lookup (manager's manager) available for FR-070, **cross-BU chains** (the chain rule governs access regardless of BU membership), and the **contractor-manager branch resolving to the RESTRICTIVE default** — a contractor manager gets no individual-score access and their pending reviews escalate to the manager's manager, behind a config flag defaulting restrictive, pending Q-006 ratification (uncertainty rounds up in the safeguarding seam; G1 must not certify contractor-manager access until Q-006 is answered — panel B2) (unit tests per case, `Category=PrivacyReporting`).
- [ ] RoleScope: for each of the seven roles, the scope function returns exactly the org nodes the spec's "Sees" column grants — parameterised tests; Group/Portfolio/Executive scopes contain **no individual-read capability** by construction (FR-025).
- [ ] Suppression (research D2 algorithm): exhaustive tests against the engineered synth cases — n=3 team suppressed; sub-4 BU suppressed; single-team BU child suppressed (complement); 4-in-7 complement case suppressed; a 4+4+4 BU unsuppressed. All `Category=PrivacyReporting`.
- [ ] Suppressed results carry `{suppressed: true, reason}` and never numeric fields (FR-071) — serialisation test.
- [ ] Architecture test fails the build if any namespace other than `Hap.Api.Authorization` references the Assessment/AssessmentScore **types or table names** (the DbSet form of the test is added by HAP-8 with the migration; research D1); proven by a temporary violation in a test fixture compiled negative-case.
- [ ] **`[PA]` gating of `/api/admin/*`** (closing the HAP-3 wave-0 deferral, QUESTIONS.md Q-004): the Platform-Admin routes — including the HAP-3 `POST /api/admin/sync` and `GET/POST /api/admin/overrides` — are restricted to the Platform Admin role. An authenticated non-admin role is denied (403, or 404 per the existence-leak rule for person-addressed resources); parameterised test across all seven seeded roles. This MUST be in place before Gate G1 (HAP-3 shipped these endpoints functional but un-gated, with the guard as a marked extension point in `AdminEndpoints.cs`).
- [ ] **Cycle-safe chain walk** (HAP-3 red-team carry-forward — load-bearing): the management-chain resolver MUST NOT assume the chain is acyclic. It walks with a visited-set **and** a depth cap and terminates safely (no infinite loop, no stack blow-up) on a cyclic chain. HAP-3 guards single-node and 2-cycle *overrides* at the write seam and rejects self/unresolvable manager refs on import, but a multi-node cycle (A.mgr=B, B.mgr=A) is importable via a future non-synthetic directory (e.g. the Entra adapter) and is the read-side backstop's responsibility. Proven against a synthetic hostile 2-cycle fixture (`Category=PrivacyReporting`): resolving either member's chain terminates and grants no access it should not.
- [ ] `./scripts/verify.sh` green.
- [ ] QA (adversarial, fresh agent): construct a violation path or state exactly what was examined and why none exists (red-team brief, CLAUDE.md §9.4) — "looks fine" is not a deliverable; document in this file.

## Attempts / notes

**SPEC AUDIT 2026-07-21 (pre-start edit, story was todo):** clarified no-DbSet-before-migration sequencing (verify idempotence hazard); added cross-BU chain and contractor-manager (Q-006) test cases.
**L2 PANEL B2 (same day):** contractor-manager default reversed permissive → RESTRICTIVE (excluded + escalation, config-flagged) pending Q-006 ratification — a safeguarding seam rounds up; the L3 panel and red-team for this story must still treat Q-006 as open.
