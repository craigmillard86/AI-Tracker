# DR-0005 — An above-BU leader may read their immediate direct report's individual score (Q-015 ruling)

**Date:** 2026-07-21 · **Status:** Accepted · **Scope:** operative reading of FR-025 for spec 001; no constitution text change
**Story key:** HAP-5 (visibility seam) · **Origin:** HAP-5 L3 panel Q-015 (owner ruling)

## Context

FR-025 has two clauses that overlap in HIG's single-tree org: (1) individual-score access follows the management chain, and (2) leaders above BU level see aggregates only. Because the org is one tree, an above-BU leader (Group Leader, Portfolio Leader) is also a genuine chain ancestor of people below them — so the chain rule would grant them an individual read that clause 2 appears to forbid.

The visibility seam (HAP-5) closed the **gross transitive** over-grant: an above-BU leader cannot read individuals several hops down their subtree. But a **one-hop residual** remained — an ungranted hierarchy leader is classified `Manager` (they have direct reports) and can read their **immediate direct report's** individual score (Group Leader → direct-report BU Lead; Portfolio Leader → direct-report Group Leader). Telling a hierarchy leader apart from an ordinary manager needs the Q-014 "leads this unit" anchor, so it was not resolvable in code, and denying all ungranted direct reads would break the core clause-1 manager grant. The seam's L3 panel escalated it to the owner as a binding decision. The functional tension: a subject who is themselves a manager (a BU Lead, a Group Leader) still needs a **moderating manager**, and moderation requires the moderator to see the subject's scores.

## Decision

**A direct one-hop line-manager relationship grants an individual-score read regardless of the manager's tier.** The current seam behaviour is ratified:

1. A manager reads their **immediate direct report's** individual scores — including when that manager is above BU level (Group/Portfolio Leader moderating their direct report). This is the moderation grant; every subject has exactly one moderating line manager.
2. **Transitive (2+-hop) individual reads by above-BU leaders remain denied.** A leader does not read individuals deep in their subtree.
3. The **broad above-BU view stays aggregates-only**, under N<4 + complement suppression (FR-014/FR-071). The above-BU roles carry no chain-independent individual-read capability.

The Q-015 residual is therefore **ratified intended behaviour, not a leak**.

## Consequences

- HAP-5's Q-015 G1 precondition is **satisfied** for the synthetic build: the owner has ruled and the shipped seam already implements the ruling. (HAP-5 is closed; this DR carries the resolution — the closed story file is not edited.)
- The **HAP-8 hard block is lifted** for the local/synthetic build: a story may now wire a live cross-person individual-read endpoint through the seam, which enforces the ratified policy (one-hop direct allowed, transitive denied, above-BU aggregates-only, contractor-restrictive per DR-0006). Self-scope endpoints were never blocked.
- `OrgGraphRealDirectoryTests.PINNED_ungranted_above_BU_hierarchy_leader_CAN_read_immediate_direct_report_pending_Q014_G1` now asserts **ratified** behaviour. Its comment was written as a "pending residual that flips to deny when the owner rules restrictive"; the owner ruled **allow**, so it does not flip. **HAP-8 (the next story to touch the seam — it relocates the Assessment types) must reframe the test name/comment from "pending Q-014" to "ratified per DR-0005."**
- **Real-data caveat (Q-014, deferred):** tier identification is still depth-based (correct on the synthetic generator, provisional on real org shapes). Under this ruling the *one-hop direct read* is tier-independent, so it is unaffected; but correctly scoping a real **BU Lead's BU-wide** individual read still needs an explicit `BuDelegate` grant or the Q-014 anchor. A BU Lead with no such grant is treated as an ordinary Manager (direct reports only) — under-grant, fail-closed. This is a real-data-onboarding precondition, not a build blocker.
- The G1 witness (HAP-12 V3) verifies the seam implements the ratified policy, rather than surfacing the case for the owner to rule (the owner has now ruled).

## Relates

- **DR-0006** (Q-006 contractor-manager restrictive) — the companion access ruling.
- **Q-014** (QUESTIONS.md) — deferred; the structural-anchor decision that would let a real BU Lead's BU-wide read be granted without an explicit `BuDelegate` grant.

## Supersedes

Nothing. Resolves QUESTIONS.md Q-015 (raised HAP-5; ruling round 1, corrected round 3; owner-ratified 2026-07-21).
