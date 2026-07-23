# DR-0009 — G1 privacy gate: owner sign-off (M1 = zero leaks)

- **Date:** 2026-07-23
- **Status:** Accepted
- **Deciders:** Craig Millard (owner/witness)
- **Gate:** G1 Privacy (constitution Art. VII; CLAUDE.md §11) — human-witnessed, not self-certifiable.

## Context

G1 is the privacy gate at the end of the RBAC/assessment wave: the owner witnesses, on the local stack with synthetic data, that no read path exposes individual assessment data outside the management chain, that N<4 suppression holds, and that every individual-level view is audited. **M1 = zero leaks.** G1 is a precondition for any future deployment with real data. HAP-12 completed the G1 readiness evidence set; HAP-5/10/11/12 shipped the seam, suppression, rollups, audit, and GDPR surfaces it covers.

## Decision

**The owner witnessed G1 on 2026-07-23 on the local synthetic stack (current `main`) and passed it. M1 = zero leaks confirmed.**

Evidence witnessed:
- **Live zero-leak demonstration** (full synthetic org: 23 BUs, 11,084 people, George's real Submitted assessment). George's individual assessment was readable **only by his direct line manager (Chloe, 200)**; his BU Lead, Group Leader, Portfolio Leader, the HIG Executive, and the Platform Admin were **all denied (404)**. Because George's data genuinely exists, each 404 is a real authorization denial, not a "no data" artifact — proving transitive closure (DR-0005: 2+ hops up denied) and that Platform Admin has no individual-data access.
- **Audit completeness:** the authorized views logged `IndividualView` rows (actor=Chloe, subject=George); the denied attempts logged nothing (they never viewed). Every individual read that occurred is on the audit trail.
- **Automated deterministic evidence** (controls hierarchy + data, asserts allow/deny decisions): `PrivacySpotChecksV3Tests` (all 7 roles' out-of-chain denials, DR-0005 one-hop-allow/2+-hop-deny, DR-0006 contractor-deny, above-BU aggregates-only, N<4 suppression), `HierarchySuppressionTests` + `RollupDashboardTests` (complement-differencing), `AuditCompletenessSweepTests` — green in the standing `Category=PrivacyReporting` suite.

## Ratifications (all accepted as-shipped)

The owner ratified every accumulated provisional G1 decision **as-shipped**:

| Item | Ratified position |
|---|---|
| **Q-027** — GDPR erasure of the non-nullable `AssessmentScore.SelfScore` | **As-shipped:** zero the value; the `RetentionErasure` audit-ledger row is the authoritative "was erased" record. No tombstone/nullable score column added. Q-027 → RESOLVED. |
| **HAP-11 residuals** — k=4 suppression floor; cross-cycle trend differencing assumption; fixed/laminar-hierarchy partition assumption | **As-shipped.** |
| **Erasure cross-request TOCTOU** — retention-vs-write concurrency window (never a leak; unreachable in the single-admin witnessed model) | **Parked as-shipped** — durable interlock (an `AssessmentScore` xmin token / in-tx ledger re-check / retention bumping `Assessment.xmin`) deferred to a follow-up, not required for G1. |

DR-0005 (one-hop above-BU direct read) and DR-0006 (contractor-manager restrictive) were already owner-ratified; G1 confirmed the seam implements them.

## Consequences

- **G1 is PASSED.** The privacy precondition for a future real-data deployment is met (within local scope; the real-data unlock itself remains a separate deployment step).
- **HAP-20 (G2 reporting-gate readiness) is unblocked** from the §11 "preceding gate unsigned" wall. G2 itself (reporting reconciliation, witnessed via quickstart V5) remains to be witnessed after HAP-20 completes.
- The Q-027 provisional is now resolved; the TOCTOU durable-fix remains a recorded follow-up, not a blocker.
