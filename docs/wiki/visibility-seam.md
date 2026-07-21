# The visibility seam — as built

_Subsystem shipped by HAP-5 (FR-014, FR-025, FR-071) — the constitution's Wave-0 spike (Art. VII) and the sacred safeguarding seam (Art. VI). Describes shipped behaviour only; WHAT/WHY live in `docs/spec/` + `specs/`, decisions in `docs/decisions/`._

## What exists

The single authorization layer through which every read of assessment data must pass. It lives entirely under `backend/src/Hap.Api/Authorization/**`. No assessment scores are stored yet (HAP-8 registers the DbSet + migration), so the seam is exercised today against the org graph with an in-memory store — but the authorization logic that will gate every future score read is complete and tested.

An architecture test (`SeamBoundaryTests`) fails the build if any code outside `Hap.Api.Authorization` names the `Assessment`/`AssessmentScore` types — proven non-vacuous by a compiled negative-case fixture.

## Components

### ChainResolver — the individual-read grant

Individual-score access follows the **management chain only** (FR-025 clause 1): a reader may reach a subject only if they are on the subject's manager chain. Structural (`Person.ManagerPersonId`), never the depth-derived tier labels from HAP-4 (Q-014-independent). Handles: manager gap (null → chain ends), on-leave (stays in chain), departed/inactive manager (loses their own access, chain still reaches past them via escalation for FR-070), cross-BU chains (the chain governs regardless of BU). **Cycle-safe**: visited-set (termination) + `MaxChainDepth` cap — both are live independent backstops, so a hostile multi-node cycle (importable via a future non-synthetic directory) can't hang or over-grant.

**Q-006 (contractor-manager) — RESTRICTIVE default:** a contractor ancestor is excluded from the read grant (the walk passes *through* them so employee managers above keep access; reviews escalate to the first non-contractor ancestor). Behind a DI-default config flag; the contractor-eligibility check is independent of any grant, so no grant can launder past it.

### RoleScope — structural, capability-gated

Built from BU/Group/Portfolio FK membership + explicit `RoleGrant` rows — **never** the depth-derived labels (Q-014). `IndividualReadCapability(role)` is the single source both the gateway and the anchored scope consume. Group Leader / Portfolio Leader / HIG Executive / Platform Admin have **no individual-read capability by construction** (FR-025 clause 2). BuDelegate ⇒ within-BU. BU-scoped decisions re-read `RoleGrant` from the DB, never the cookie claim (HAP-4 A3).

### AssessmentReads gateway — fail-closed

The sole type permitted to touch the Assessment DbSets. An individual read requires **both** the chain grant **and** the reader's role capability — it authorises *before* touching the store, and a denied read never queries it (proven by a counting fake store). Reader role is derived structurally and fails closed: where within-BU can't be proven without the Q-014 anchor, the read is denied.

### Suppression (FR-014 / research D2)

Suppress a cell if `n < 4` **or** the unpublished complement within its parent is `0 < parent.n − node.n < 4`. The publication path is **set-based** (`EvaluateLevel`), which defeats *multi-child* differencing (e.g. parent 11, children 4/4/3: the 3 is below-threshold, then one 4 is complement-suppressed so 11−4=7 can't reduce to the hidden figure) — stronger than a pairwise check, which was removed to eliminate the footgun. Throws on corrupt input (children sum > parent). Suppressed results serialise to `{suppressed: true, reason}` with **no** numeric field (FR-071).

## The Q-015 residual — a recorded G1 owner decision

FR-025's two clauses overlap in HIG's single-tree org: an above-BU leader is also a chain ancestor. The gross transitive over-grant (an above-BU leader reading individuals deep in their subtree) is **closed** by the role-capability gate. But an **ungranted hierarchy** Portfolio/Group Leader has no explicit `OrgRole` grant, so they classify as a plain Manager and **can read their immediate direct report's individual score** (`HAP-GRP-01 → HAP-BUL-01`, `HAP-PF-01 → HAP-GRP-01` are ALLOWED; anything 2+ hops is denied). Distinguishing a hierarchy Group Leader from an ordinary manager needs the Q-014 "leads this unit" anchor; denying all ungranted direct reads would break the core clause-1 manager grant. So this one-hop residual is **genuinely Q-014-bound** and left as an explicit **G1 owner decision**: does an above-BU leader read their direct report's individual score, or aggregates only? Pinned by a test that flips to deny when Q-014 lands and the owner rules restrictive. **HAP-8 must not wire a live cross-person individual-read endpoint until this cap lands.**
