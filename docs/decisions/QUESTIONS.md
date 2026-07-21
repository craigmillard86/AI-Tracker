# QUESTIONS.md — open questions routed to the owner

Append-only. Each entry is dated and keyed (story or spec). Answers that change behaviour become decision records (DR-NNNN); record the answer here with a pointer, never edit history.

---

## 2026-07-21 · spec 001 (maturity-initiative-register) · Q-001 — Directory attributes across 23 BUs

Which AD/Entra attributes reliably carry the group/portfolio mapping and employee type across all 23 BUs? Expect inconsistency between BU tenants/domains; a directory hygiene audit is needed before rollout.

**Blocks:** deployment/onboarding only — not the local build (directory source is a port; all local data is synthetic).
**Owner action:** commission the directory audit before Cycle 1 onboarding.
**Status:** OPEN

## 2026-07-21 · spec 001 (maturity-initiative-register) · Q-002 — Harris form semantics confirmation

Confirm with the Harris AI Dashboard owner: (a) the stage mapping (Idea+Evaluation → Ideation; Pilot → Development; Production+Scaled → Production; Retired → Ideas Tried but Stopped), and (b) that an initiative's "level" means the AI-DLC autonomy level the initiative embodies (1–3).

**Blocks:** Gate G2 trust in the first real submission — not local build work, which proceeds on the documented interpretation (FR-027, FR-064); the mapping is configuration data, so a corrected answer is a data change, not a code change.
**Owner action:** confirm with whoever owns the Harris AI Dashboard definitions.
**Status:** OPEN

## 2026-07-21 · spec 001 / tasks · Q-004 — No mockups for admin surfaces or sign-in

The mockup set covers the seven user-facing screens only. No mockup exists for: the dev sign-in role picker (HAP-4), or any Platform Admin surface (cycle management, org sync, overrides, role grants, audit search, notifications trigger — HAP-3/7/12/18). Provisional answer recorded per CLAUDE.md §6.3: **admin surfaces ship API-only in v1** (exercised via quickstart/curl and integration tests) and **sign-in ships as a minimal DESIGN.md-conformant screen** (cards + buttons, no new components). Stories are written to that assumption.

**Blocks:** nothing — stories proceed on the provisional answer.
**Owner action:** confirm the provisional answer, or supply mockups (which would add L1 UI stories for the admin surfaces).
**Status:** OPEN (provisional answer in effect)

## 2026-07-21 · spec 001 (maturity-initiative-register) · Q-003 — Harris ingestion API roadmap

Does the Harris dashboard team plan an ingestion API? If yes, direct submission replaces the review-and-transcribe step (Phase 3 candidate).

**Blocks:** nothing in Phase 1; informs Phase 3 scope only.
**Owner action:** ask when convenient; low urgency.
**Status:** OPEN

## 2026-07-21 · spec 001 / retroactive audit · Q-005 — Data-quality score weighting

FR-038's BU data-quality score combines update timeliness and field completeness, but the spec never fixed the weights. **Provisional answer in effect:** 50% timeliness / 50% completeness, annotated in FR-038 and HAP-19. The score rolls up to group leadership, so the final formula is an owner decision → becomes a DR when answered.

**Blocks:** nothing — HAP-19 builds against the provisional weighting; changing weights later is a config/data change.
**Status:** OPEN (provisional answer in effect)

## 2026-07-21 · spec 001 / retroactive audit · Q-006 — May contractor managers moderate (and view) employee scores?

Contractors are excluded from assessment *participation* (FR-005/006), but a contractor can be a line manager. The chain rule (§2) is structural, which would let a contractor manager moderate and therefore view direct reports' individual scores — UK-GDPR-relevant access by a non-employee. **Provisional answer in effect:** yes, the chain is structural; moderation duty follows line management regardless of employee type. Flagged for privacy review (relevant to G1 and the DPIA).

**Blocks:** nothing locally (synthetic data), but the provisional answer shapes HAP-5/HAP-9 chain logic.
**Status:** OPEN — the provisional above was **REVERSED to restrictive** by the L2 panel; see the dated Q-006 update below (do not read this block's "yes" as current).

## 2026-07-21 · governance · Q-007 — FR citation rule vs governance stories (HAP-22 precedent)

Constitution Art. II says "code that cannot cite an FR-ID does not merge." HAP-22 (agent roster — docs/config only) merged with `fr: []` / change-log `none`. **Provisional answer in effect:** the rule binds *product code*; docs/process/governance stories may cite none, recording `none` in the change-log. Confirming this (or requiring a synthetic GOV-FR scheme) is an owner decision → constitution clarification (PATCH) when answered.

**Blocks:** nothing.
**Status:** OPEN (provisional answer in effect)

### Q-006 update — 2026-07-21, L2 panel review (B2)

Provisional answer REVERSED to **restrictive**: contractor managers get no individual-score access; their pending reviews escalate to the manager's manager; implemented behind a config flag defaulting restrictive, pending owner/DPIA ratification. Rationale: constitution Art. V "uncertainty rounds up" applied to the safeguarding seam — G1 must not certify an unratified access path (M1 = zero leaks). A "yes" answer later is a config flip + seam test update (L3).

## 2026-07-21 · spec 001 / L2 panel review · Q-008 — Leaver completion-denominator rule

Excluding mid-cycle leavers from the completion denominator (while their submitted/moderated scores remain in aggregates per §3.5/FR-024) changes a reported metric that rolls up the hierarchy. The rule mirrors the contractor precedent (FR-005) and is encoded in HAP-10/HAP-19 with the retention guard (panel B1), but the denominator choice itself deserves owner confirmation. **Provisional answer in effect:** leavers exit the denominator at close; alternative (count them as non-responders) rejected as penalising teams for attrition.

**Blocks:** nothing — a reversal is a query change + test update.
**Status:** OPEN (provisional answer in effect)

## 2026-07-21 · spec 001 / HAP-2 · Q-009 — "300–800 people per BU" vs engineered sub-4 / single-team / org-of-7 BUs

HAP-2 acceptance criterion 3 requires "300–800 people per BU", but edge cases (b) "≥1 BU with <4 people total", (c) "≥1 BU containing a single team", and (d) "≥1 BU where one sub-team of 4 sits inside an org of 7" require BUs deliberately far below 300. Read literally the criteria contradict each other. SC-008 phrases the band as an *estimate* ("estimated 300–800 per BU"), which supports treating it as the nominal range for ordinary BUs, not a hard invariant for every BU.

**Provisional answer in effect (per CLAUDE.md §6.3):** the 300–800 band applies to the ordinary (non-engineered) BUs; the three engineered edge-case BUs (sub-4, single-team, org-of-7) are intentional exceptions demanded by criteria (b)/(c)/(d). Tests assert the 300–800 shape over the ordinary BUs only, and assert the whole-population invariants (exactly 23 BUs, 6 groups, 3 portfolios, total ≥10,000 people, ≥2,000 managers with ≥1 active report) over all BUs including the engineered ones. Population stays at exactly 23 BUs (3 engineered-small + 20 ordinary) to honour the literal "23 BUs" count.

**Blocks:** nothing — synthetic Wave-0 data; a reversal (e.g. put the engineered small orgs in extra BUs beyond 23, or as sub-teams of larger BUs) is a generator retune + test update, both reviewed.
**Status:** OPEN (provisional answer in effect)

## 2026-07-21 · spec 001 / HAP-3 · Q-010 — What does an `OrgOverride` of field `DottedLine` do?

The override layer (FR-023) models three fields: `BusinessUnit`, `Manager`, `DottedLine`. `BusinessUnit` and `Manager` are structural — re-applied on top of every sync so a correction out-lives re-sync. **`DottedLine` has no defined structural effect in the spec.** The management-chain rule that governs individual-score visibility (§2, FR-025) is the *solid-line* manager chain; a dotted line is an advisory/secondary relationship.

**Provisional answer in effect (per CLAUDE.md §6.3):** in v1 a `DottedLine` override is **recorded and audited but advisory only** — it does not alter the management chain and therefore grants no individual-score visibility. If dotted-line relationships should later confer any visibility or reporting effect, that is a safeguarding-seam change (L3) and a decision record, not a silent behaviour change. Uncertainty rounds up: the restrictive interpretation (no visibility) is the safe default for the GDPR seam.

**Blocks:** nothing — HAP-3 records/audits DottedLine overrides; giving them an effect later is an additive L3 change.
**Owner action:** confirm whether dotted lines carry any visibility/reporting semantics before that feature is built.
**Status:** OPEN (provisional answer in effect)

## 2026-07-21 · spec 001 / HAP-6 · Q-011 — Does the seeded v1 framework version start Active or Draft?

`docs/frameworks/ai-maturity-sdlc.v1.json` carries `"status": "draft"`, and `data-model.md`'s `FrameworkVersion.status` enum is `Draft | Active | Retired`. Taken literally, seeding v1 would leave it Draft — but HAP-6's acceptance criterion requires `GET /api/frameworks/current` to return "the active version" so the assessment UI can be driven from data, and there is no other admin flow in scope yet to promote a version.

**Provisional answer in effect (per CLAUDE.md §6.3):** the seeder auto-activates a framework's very first version when the framework currently has no Active version (general bootstrap rule, not JSON-specific — a JSON `"status"` of `draft` is informational/authoring metadata, not literally imported as the initial `FrameworkVersion.Status`). Seeding a second version (v2) does **not** auto-activate — that requires an explicit admin activation step (not built in HAP-6; `FrameworkVersion.Activate()` exists as a domain method, unexercised by any endpoint yet). Re-seeding v1 is idempotent and does not re-trigger auto-activation once any version of the framework is Active.

**Blocks:** nothing — local synthetic build; a reversal (e.g. require explicit admin activation even for the first version, leaving `/current` briefly empty after seed) is a seeder behaviour change + test update, not a schema change.
**Status:** OPEN (provisional answer in effect)

## 2026-07-21 · spec 001 / HAP-6 · L2 panel round 1 · Q-012 — Nothing enforces "exactly one Active FrameworkVersion per framework"

`FrameworkVersion.Activate()` (FR-054) promotes the target version to Active but does not demote whichever version was previously Active for the same framework — nothing in the domain or the seeder enforces "at most one Active version per framework" as an invariant. Today this is harmless: HAP-6 only ever activates a framework's first version (bootstrap, Q-011) and never wires an admin activation endpoint, so two Active versions cannot occur in practice yet. But the gap is real and will matter the moment an activation workflow ships (HAP-7 or later) — e.g. two independent `Activate()` calls (v1, then v2) would leave both Active, and `GET /api/frameworks/current`'s `OrderByDescending(VersionNumber).FirstOrDefault()` would silently paper over it by picking the higher version number rather than surfacing the inconsistency.

**Blocks:** nothing in HAP-6 — no code path can trigger the two-Active state today. Whichever story adds an admin activation flow must decide the invariant explicitly: `Activate()` auto-demotes the incumbent (a version-level side effect), or the caller is required to `Retire()`/deactivate the old one first (an explicit two-step), or a DB constraint (partial unique index on `(framework_id) WHERE status = 'Active'`) enforces it as a hard invariant regardless of caller discipline.
**Status:** OPEN

## 2026-07-21 · spec 001 / HAP-4 · Q-013 — Who grants the explicit `RoleGrant` rows the dev fixtures need?

FR-056 splits roles into hierarchy-derived (never stored) and explicit grants (`RoleGrant` table: PlatformAdmin,
HigExecutive, BuDelegate, GroupViewer). Two of the seven seed users (`Platform Admin`, `HIG Executive`) need an
explicit grant to show the labelled role at `GET /api/me`, but the role-grant admin endpoint is a later story
(HAP-3/7/12/18 admin-surfaces list, Q-004) — nothing seeds these rows today.

**Provisional answer in effect:** `LocalDevProvider` (HAP-4) idempotently ensures the grant at sign-in time,
driven by the seed-users.json `role` field only (Hap.Api never references Hap.Synth/Distributions — no
hardcoded external_ref), `GrantedBy: "dev-seed"`, audited via the existing `RoleGrant` AuditAction. This is
scoped to the local dev provider only; the Entra adapter would never run this path, and the role-grant admin
endpoint superseding it later is additive, not a seam change.

**Blocks:** nothing — synthetic dev-only bootstrapping; a reversal (seed via a fixture/migration instead) is a
story-ordering change.
**Status:** OPEN (provisional answer in effect)

### Q-013 addendum — 2026-07-21, HAP-4 QA finding: the dev-seed bootstrap is self-healing, not just idempotent

QA (fresh instance, adversarial re-run) proved empirically — not merely inferred — that
`EnsureExplicitGrantAsync`'s idempotency cuts both ways: deleting HAP-ADMIN's (or HAP-EXEC's) `RoleGrant` row
directly in the DB and then having them sign OUT and back IN does **not** clear the revocation. Sign-in
re-derives the grant fresh from `seed-users.json`'s `role` label every time, so the grant is restored on the
very next sign-in regardless of any intervening DB state. See
`Hap.Api.Tests/Identity/QaFreshAdversarialTests.cs`'s
`Revoking_a_PlatformAdmin_grant_mid_session_does_not_immediately_revoke_the_live_cookie` for the concrete proof
(both the stale-cookie half, matching HAP-4's own advisory A3, and this sharper self-healing-on-resignin half).

This is **not** a cross-identity escalation (HAP-ADMIN/HAP-EXEC only ever re-acquire their OWN designated role;
nothing leaks to any other fixture) and is consistent with this entry's "synthetic dev-only bootstrapping"
framing — there is no revoke endpoint in scope anywhere yet. It is a forward-looking design constraint:
**whichever later story builds the real role-grant admin/revoke endpoint (the HAP-3/7/12/18 list above) must
either special-case HAP-ADMIN/HAP-EXEC (the two seed-labelled bootstrap fixtures) or accept that revoking their
PlatformAdmin/HigExecutive grant via that endpoint alone will be silently undone the next time either fixture
signs in, because this dev-seed bootstrap keeps running unchanged alongside it.**
**Blocks:** the future role-grant admin/revoke endpoint story's design (must read this before assuming revoke
semantics are uniform across all fixtures). Not a HAP-4 blocker.
**Status:** OPEN (informational; no action required in HAP-4's own scope)

## 2026-07-21 · spec 001 / HAP-4 · Q-014 — How is hierarchy-tier leadership (BU Lead / Group Leader / Portfolio Leader) structurally identified?

RoleGrant's own doc comment says hierarchy roles are "computed from org structure," but no entity
(`BusinessUnit`/`GroupOrg`/`Portfolio`/`Person`) carries an explicit "this person leads this unit" pointer,
and HAP-4 cannot add one (HAP-6 owns the only open migration slot). The obvious heuristics fail against the
generator's own engineered edge cases: "manager homed in a different BU" misclassifies BU01 (which hosts the
entire Exec→Portfolio→Group→BU leadership chain simultaneously, since BU01 is the first BU of the first group
of the first portfolio); "manager-subtree spans exactly one BU" misclassifies BU01's BU Lead (the engineered
cross-BU report, edge (i), leaks one BU02 person into their subtree) and undercounts due to the null-manager
directory-gap fixture (edge (e), which is deliberately disconnected from any manager chain).

**Provisional answer in effect:** classify by **manager-chain depth from a validated root**, where root =
active, no manager, **and ≥1 active direct report** (this last clause is what excludes the null-manager
directory-gap fixture, which has zero reports, from being mistaken for a root). Depth 1 = Portfolio Leader,
depth 2 = Group Leader, depth 3 = BU Lead — each of whichever BU/Group/Portfolio the person is themselves
homed in via the existing (non-inferred) `BusinessUnit.GroupId`/`GroupOrg.PortfolioId` FKs. Generic "Manager"
= has ≥1 active direct report, independent of depth (so a BU Lead is also a Manager, matching contracts/api.md's
"Manager scope"). HIG Executive (depth 0) is deliberately **not** granted hierarchically — per RoleGrant's own
doc comment it is an explicit-grant type, so it falls under Q-013 instead. Verified against all seven seed
users plus the BU01-collapse and null-manager fixtures in `Hap.Api.Tests/Identity/HierarchyRoleResolverTests.cs`.

**Blocks:** nothing locally — correct for the current generator's engineered shape. Flagged for the future
Authorization (L3) seam story: if a future population shape defeats this heuristic, or if BU-level visibility
scoping needs a stronger guarantee than depth-inference, add an explicit leader pointer via a reviewed migration.
**Status:** OPEN (provisional answer in effect)

### Q-014 update — 2026-07-21, HAP-4 L3 panel re-derivation (session-lead ruling)

HAP-4 was reclassified L2 → L3 mid-panel: `HierarchyRoleResolver` walks the management chain (CLAUDE.md §7 L3
trigger), which an L2 self-classification missed. That re-derivation elevates this entry's status: the
depth-from-root tier derivation is **not just a dev-provisional any more — it is an owner-ratification item**,
mirroring the Q-006 contractor-manager precedent. The reason: the algorithm assumes a **uniform-depth org
tree** (every Portfolio Leader/Group Leader/BU Lead sits at exactly depth 1/2/3 from a single validated root).
That is true of the synthetic generator's engineered shape (verified in `HierarchyRoleResolverTests`) but is
**not guaranteed of a real HIG org chart** — a real directory could have uneven depth (e.g. a BU Lead who is
also, structurally, two hops from the exec instead of three; a portfolio with an extra reporting layer) that
the depth rule would misclassify. `hap-red-team` is reviewing the resolver now, including this
uniform-depth-tree misassignment hypothesis and a deactivated-root subtree case the domain specialist raised
(what happens to computed tiers when the validated root itself is later deactivated mid-tree).

**Blocks:** any non-synthetic BU onboarding — this provisional MUST be ratified by the owner (or superseded by
a decision record) before real org data reaches this resolver. **HAP-5 (or whichever story builds the
visibility/Authorization seam) MUST NOT build individual-score visibility scope on these hierarchy-tier labels
until either (a) this entry is ratified by the owner, or (b) HAP-5's own L3 red-team independently clears the
derivation for its use case.** Uncertainty rounds up (constitution Art. V) — this is a safeguarding-seam-adjacent
input even though HierarchyRoleResolver itself does not gate any read today.
**Status:** OPEN — owner ratification required before real-data onboarding; provisional remains in effect for
the synthetic local build only.

### Q-014 update — 2026-07-21, hap-red-team confirmed the misassignment concretely (L3 panel round 1)

`hap-red-team`'s finding sharpens the uniform-depth-tree hypothesis above from theoretical to concrete: an
interim/dual-hat layer inserted anywhere in the chain — e.g.
`Exec -> PortfolioLeader -> GroupLeader -> InterimCover -> RealBuLead` — makes the depth rule label the
INTERIM cover person "BU Lead" (they sit at depth 3) and demote the actual BU Lead to "Manager" only (depth
4, past the recognised range). Two things came out of this round:
- **Partial fix landed:** a depth-1/2/3 person with **zero active reports** (e.g. a vacated leadership slot)
  no longer gets a leadership label — `HierarchyRoleResolver` now gates the tier fields on `IsManager` too,
  not depth alone. See `HierarchyRoleResolverTests.Depth_three_with_zero_active_reports_gets_no_leadership_label`.
- **NOT fixed (session-lead ruling — correctly out of scope here):** the interim-layer misassignment above,
  because the interim cover DOES have active reports (the real BU Lead's own reports, transitively) — the
  `IsManager` gate does not catch it. This is the "deeper misassignment" this entry already flagged; it
  needs the structural anchor (owner decision + migration), not a code change in HAP-4.

Reaffirming the blocking language above in the strongest terms this round revealed: **HAP-5 (or whichever
story consumes `HierarchyRoleResolver`'s output for visibility scope) MUST NOT do so until Q-014 is
owner-ratified with a structural anchor, or HAP-5's own L3 red-team independently clears the derivation.**
This is a G1 precondition, not a nice-to-have — G1's own bar is "zero leaks," and an interim-cover
misassignment is exactly the shape of leak G1 exists to catch.
**Status:** OPEN — owner ratification required before real-data onboarding AND before any story builds
visibility scope on these labels; provisional remains in effect for the synthetic local build only.

## 2026-07-21 · spec 001 / HAP-5 · Q-015 — Does the FR-025 "above-BU leaders see aggregates only" cap bind an above-BU leader who is genuinely in a subject's line-management chain?

FR-025 has two clauses: (1) individual-level assessment data is readable by "the individual, their manager,
and those above them in the management chain"; (2) "Leaders above BU level see aggregates only." In HIG's
single-tree org model these clauses overlap: a Group Leader / Portfolio Leader / HIG Executive sits ABOVE a
BU member in that member's own upward management chain (member -> team lead -> director -> BU Lead -> Group
Leader -> ...). So clause (1) read literally grants those above-BU leaders individual-score access, while
clause (2) says they get aggregates only. To honour clause (2) the seam must CAP individual reads at the
BU-Lead tier — but identifying "the BU-Lead tier boundary" in the chain is exactly the unratified
depth-derivation of **Q-014** (the BU01 collapse defeats every structural BU-boundary heuristic: BU01 homes
Exec, Portfolio Leader, Group Leader and BU Lead simultaneously, so "same BU as the subject" cannot
distinguish the BU-Lead ceiling from the leaders above it). Group Leader / Portfolio Leader also have **no**
explicit RoleGrant and **no** ratified structural anchor today, so the seam cannot even identify them as
above-BU leaders to exclude them.

**Provisional answer in effect (per CLAUDE.md §6.3; uncertainty rounds up, but bounded by what is
structurally possible without Q-014):** the HAP-5 seam grants individual-score reads **only** through the
transitive management chain (self + upward ancestors, **excluding contractor managers** per Q-006), which is
fully structural and Q-014-independent. `RoleScope` grants the Group Leader / Portfolio Leader / HIG
Executive / Platform Admin roles **no individual-read capability by construction** (AC-2 / FR-025 clause 2 at
the role-scope layer) — the gateway never derives individual access from a role, only from the chain. The
residual consequence: an above-BU leader who is *also* a genuine line-management ancestor of a subject
retains individual access **via the chain** (clause 1), because capping that at the BU-Lead tier requires the
Q-014 structural anchor. This is a deliberate, documented deferral — the seam is structured so the cap drops
in as a single predicate once Q-014 ratifies a "leads this unit" anchor.

**Blocks:** nothing locally (synthetic data; SC-005's own bar is "zero reads *outside* the chain", which the
chain rule enforces exactly). **This is a G1 precondition:** G1 witnesses every seeded role attempting
individual-score access, and whether an in-chain above-BU leader should be capped is precisely an
owner/DPIA call to make there. HAP-5's L3 red-team is asked to rule on whether the chain-only grant is an
acceptable interim posture or whether the seam must instead deny ALL transitive (non-direct-manager) reads
until Q-014 lands (the more restrictive alternative, which would also under-grant legitimate BU-Lead access).
**Status:** SUPERSEDED by the L3 ruling below (the chain-only provisional was found to over-grant and was
reversed in-story).

### Q-015 ruling — 2026-07-21, HAP-5 L3 panel round 1 (session-lead adjudication, binding)

The panel split. `hap-domain-specialist` BLOCKED on the over-grant (everything else spec-faithful);
`hap-code-reviewer` CHANGES-REQUIRED (this + a record note; code otherwise clean); `hap-red-team` SIGN-OFF
conditional (finding real but deferrable with a G1 flag). Both reviewers confirmed the concrete leak: the
gateway's `AuthorizeIndividualRead` consulted only the chain and never `RoleScope`, so an above-BU leader —
a chain ancestor of ~everyone below them in the single-tree org — got individual reads FR-025 clause 2
forbids (e.g. `GrantsIndividualRead(HAP-GRP-01, HAP-SEED-IND) = true`). `RoleScope.AllowsIndividualRead`
already computed `false` for those roles but was **dead code**, never wired into the gateway.

**Session-lead ruling (fix-now restrictively; defer only the Q-014-bound edge, fail-closed).** The over-grant
has two separable parts:

- **PART 1 — the GROSS transitive over-grant is closed now (does NOT need Q-014):** an above-BU leader reading
  individuals several hops down their subtree. The gateway now requires BOTH the chain grant AND the reader's
  structurally-derived role carrying `RoleScope.AllowsIndividualRead = true`, AND the subject within reach
  (Manager ⇒ DIRECT reports only). This does NOT break cross-BU or direct-manager reads (Manager / BU-delegate
  keep the capability). The reader's role is derived STRUCTURALLY — explicit `RoleGrant` (HigExecutive /
  PlatformAdmin / GroupViewer disqualify; BuDelegate = within-BU) then org position (has active direct reports
  ⇒ Manager, else Individual) — NEVER from `HierarchyRoleResolver` depth labels (Q-014).
  **⚠ Scope of "denied even as an ancestor" — corrected in round 3 (see below):** this holds for
  Exec/PlatformAdmin (explicit grants) and for all TRANSITIVE (2+ hop) reads, but NOT for the one-hop
  direct-report read by an ungranted hierarchy Portfolio/Group Leader — see the round-3 correction.

- **PART 2 — deferred, fail-closed (genuinely Q-014-bound):** the fine residual — a hierarchy BU Lead who,
  because of BU01-collapse, is structurally an ancestor of people outside their true BU. Identifying
  "above-BU" precisely is exactly Q-014. The seam does NOT attempt the full structural cap. Where the reader's
  role/scope is structurally AMBIGUOUS such that a within-BU read cannot be PROVEN without Q-014, the gateway
  FAILS CLOSED (denies): an ungranted transitive (skip-level / BU-wide) read is refused. A hierarchy BU Lead
  gets BU-wide reads only via an explicit `BuDelegate` grant covering the subject's BU (or once Q-014 ratifies
  a "leads this unit" anchor). This is a **hard G1 precondition**: **HAP-8 MUST NOT wire a live individual-read
  endpoint until the BU-tier cap lands, and G1 cannot certify until Q-014/Q-015 resolves it.** (Copied into
  the HAP-8 story Context.)

`hap-red-team`'s explicit warning was honoured: the seam does NOT adopt "deny ALL transitive reads" as a blunt
rule — direct-manager reads (incl. cross-BU) and BuDelegate-scoped BU-wide reads still work; only
un-attributable transitive reads fail closed.

**Blocks:** nothing locally (synthetic data). G1 precondition as above; HAP-8 endpoint-wiring precondition as
above.
**Status:** RESOLVED for the seam (PART 1 implemented + tested, Category=PrivacyReporting); PART 2 deferred to
Q-014 with a documented fail-closed posture and G1/HAP-8 preconditions. **See the round-3 correction below —
the residual is larger than PART 1's first wording implied and the true extent is now recorded.**

### Q-015 correction — 2026-07-21, HAP-5 L3 panel round 3 (hap-red-team, corroborated by domain + code)

Round-2 panel: `hap-domain-specialist` SIGN-OFF, `hap-code-reviewer` SIGN-OFF, `hap-red-team` BLOCKED — but the
block was **false assurance in the record, not a code defect** (all three reviewers judged the CODE correct).
Ruling: correct the record, expand the G1 witness, pin the behavior with a test. The authorization algorithm
is unchanged.

**The true residual (accurately stated).** Hierarchy above-BU leaders (Portfolio Leader, Group Leader) are
NEVER seeded an explicit `OrgRole` grant — only "Platform Admin" and "HIG Executive" get explicit grants. So
`ClassifyReader` falls them through `HasDirectReports` → **Manager**, and they CAN read the individual scores
of their **IMMEDIATE DIRECT reports**:
`AuthorizeIndividualRead(Ungranted(HAP-GRP-01), HAP-BUL-01) = ALLOWED` (Group Leader → direct-report BU Lead);
`AuthorizeIndividualRead(Ungranted(HAP-PF-01), HAP-GRP-01) = ALLOWED` (Portfolio Leader → direct-report Group
Leader). Only the TRANSITIVE / subtree read (2+ hops) is closed; the one-hop direct-report read by an above-BU
hierarchy leader is **NOT** closed. PART 1's "above-BU roles denied even as a genuine ancestor" is therefore
true only for Exec/PlatformAdmin and for transitive reads — **false for the hierarchy tiers at one hop.**

**Why the code stays (all three reviewers agree).** Distinguishing a hierarchy Group Leader from an ordinary
Manager — both simply "have direct reports" — needs the Q-014 "leads this unit" anchor. The only code
alternative is denying ALL ungranted direct-manager reads, which breaks the core FR-025 clause-1 manager grant
(the over-restriction `hap-red-team` explicitly warned against). Domain's reading: the direct-report read is a
LEGITIMATE one-hop manager-review relationship (a BU Lead is themselves an assessment subject who needs a
moderating manager). Whether clause-2 should ALSO deny that direct read is a **genuine spec ambiguity and an
OWNER decision at G1** — not resolvable in code now.

**G1 precondition — expanded (binding).** The owner must specifically rule: *does an above-BU hierarchy leader
(Portfolio/Group) get to read their immediate direct report's individual score, or aggregates only?* The G1
witness script MUST include `HAP-PF-01 → HAP-GRP-01` and `HAP-GRP-01 → HAP-BUL-01` and show they currently
return **Allowed**, so the owner sees the actual behavior and decides whether it is acceptable or must flip to
denied once Q-014 supplies the anchor. (Recorded into HAP-12 — the G1/audit story — Context.)

**Pinned by test.** `OrgGraphRealDirectoryTests.PINNED_ungranted_above_BU_hierarchy_leader_CAN_read_immediate_
direct_report_pending_Q014_G1` asserts the current ALLOW for both cases (and the transitive DENY as contrast),
Category=PrivacyReporting, documented to FLIP to `Assert.False` when Q-014 lands and the owner rules
restrictive. The residual is now a tested, visible fact — not prose-only.

**Status:** RECORD CORRECTED. Code unchanged (round-2 domain + code sign-offs stand over this docs+test delta).
Residual honestly recorded + pinned; owner ruling on the one-hop above-BU direct read is a G1 decision.
