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
**Status:** **RESOLVED 2026-07-21 — owner-ratified RESTRICTIVE → [DR-0006](DR-0006-contractor-manager-no-individual-access.md).** A contractor manager gets no individual-score access; reviews escalate to the first employee manager. The seam already implements this (`SeamOptions.ContractorManagerPolicy` default), so it is now ratified behaviour, not provisional. See the dated update below.

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
**Status:** **RESOLVED-AS-DEFERRED 2026-07-21 (owner decision).** The owner ruled: **keep depth-from-root for now** — accept it as the synthetic-only provisional and revisit before real-data onboarding; do NOT build the structural "leads this unit" anchor yet. This is safe because (a) depth is exactly correct on the synthetic generator, and (b) the seam's individual-read path does not consume the depth tiers at all — it uses the management chain + explicit grants + `HasDirectReports` (see [DR-0005](DR-0005-above-bu-direct-report-read.md)), so the interim-cover misassignment cannot leak an individual score through the shipped seam. **Remaining real-data-onboarding precondition (NOT a build blocker):** on a real org, correctly scoping a BU Lead's BU-wide individual read needs either an explicit `BuDelegate` grant or the anchor; without it a real BU Lead is treated as an ordinary Manager (direct reports only — under-grant, fail-closed). Reopen and build the anchor when real-data onboarding is scheduled.

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

**Status:** **RESOLVED 2026-07-21 — owner-ratified ALLOW → [DR-0005](DR-0005-above-bu-direct-report-read.md).** The owner ruled the one-hop above-BU direct read is **ALLOWED** (a direct line-manager, any tier, reads their immediate direct report for moderation; transitive/broad reads stay aggregates-only). The residual is therefore **ratified intended behaviour, not a leak** — the shipped seam already implements it. **HAP-8 cross-person read block is LIFTED** for the synthetic build (DR-0005). The pinned test now asserts ratified behaviour and does NOT flip; **HAP-8 (next seam-touching story) must reframe its name/comment from "pending Q-014" to "ratified per DR-0005."** Real-data caveat lives with [Q-014] (deferred).

## 2026-07-21 · HAP-7 (cycle management) · Q-016 — BU/framework mapping: no junction table modeled

Root spec (line 158, 311) describes BU registration as selecting "applicable framework(s)" per BU — implying
a BU↔Framework many-to-many mapping the admin configures. FR-002/FR-003 both say invitations cover "onboarded
BUs mapped to it [the framework]" / derive from "org sync + BU/framework mapping". But the binding plan
artifact, data-model.md, models no such table: `BusinessUnit` has no framework field, and `Cycle`/
`CycleInvitation` reference only `framework_version_id` — there is no BU↔Framework junction anywhere in the
Data Model doc HAP-7 was told to build from.

**Provisional answer in effect:** since this local build seeds exactly one framework (`ai-maturity-sdlc`,
HAP-6) and data-model.md — the artifact this story cites as its Files/plan source — models no mapping table,
"onboarded BUs mapped to the framework" is read as **every onboarded BU** for Phase 1 MVP (the mapping is
vacuously total with a single framework). HAP-7 does not add a BU↔Framework junction table.

**L2 panel round-1 escalation (hap-domain-specialist):** this is not a cosmetic gap deferred to "someday" — it
is a **hard blocker for any second-framework story**. Once a second framework is seeded, `CycleService.OpenAsync`
as shipped will silently over-invite: it invites every active person in every onboarded BU to EVERY framework's
cycle, with no way to scope a BU to only some of the frameworks it should participate in. This is silent
over-participation (not a crash, not a test failure) — exactly the kind of drift the constitution's "uncertainty
rounds up" posture exists to catch before it reaches real data. Whichever story introduces framework #2 MUST
either (a) add the BU↔Framework junction table to data-model.md and thread it through `CycleService.OpenAsync`'s
eligibility query, or (b) explicitly re-ratify "every onboarded BU participates in every framework" as intended
behaviour — it cannot silently inherit HAP-7's single-framework provisional reading.

**Blocks:** nothing locally (single-framework build). **Hard blocker for the first second-framework story** —
that story cannot proceed on HAP-7's provisional reading without one of the two resolutions above.
**Owner action:** confirm the single-framework-implies-total-mapping reading, or clarify whether data-model.md
should gain a BU↔Framework table now, ahead of any second framework.
**Status:** OPEN (provisional answer in effect; escalated to a hard blocker for the next framework story)

## 2026-07-21 · HAP-7 (cycle management) · Q-017 — Post-close submission lock and cycle-close are primitives, not endpoints; BU onboarding has no write path or audit trail yet

Three related HAP-7 scope-boundary findings, all arising because Assessment/AssessmentScore, the full
cycle-close orchestration (auto-adoption/snapshots/suppression), and the BU onboarding admin workflow
referenced by root spec line 31, belong to later stories, not this one:

**(a) Submission lock.** AC 5 requires "score submission is rejected (423/409) unless a late override exists."
HAP-7's own Files list (`Hap.Domain/**` Cycle state machine, migration #3 Cycle/CycleInvitation, cycle
endpoints) does not include Assessment/AssessmentScore — those are HAP-8's migration #4 (self-assessment,
`PUT …/scores`, `POST …/submit`) and HAP-9's moderation endpoints; neither of those stories' own acceptance
criteria currently mention consulting a late-override grant. HAP-7 therefore ships the LOCK PRIMITIVE only:
`Cycle.AllowsSubmission(bool hasLateOverride)` (pure domain) plus `CycleService` queries for whether a
late-override row exists for (cycle, person) — proven by domain/integration tests against those primitives
directly, not through an actual assessment-submission HTTP call (no such call can exist yet). **HAP-8 and
HAP-9 must call this primitive when they build their submission/moderation write paths, or post-close
rejection will silently not exist.** This dependency was not previously recorded in either story's Context.

**(a-addendum, L2 panel round-1 advisory) Close-time handoff.** contracts/api.md documents that
`POST /api/cycles/{id}/close` runs auto-adoption (FR-068) + rollup snapshots + suppression verdicts (research
D2/D4) — but HAP-7's `CycleService.CloseAsync` is a bare `Open → Closed` transition; the auto-adoption/snapshot
work is deferred to HAP-10 (`docs/backlog/HAP-10-cycle-close.md`, migration #5) in a code comment only, the
same silent-drop risk (a)'s late-override handoff had before this note. **HAP-10 must hook its auto-adoption +
snapshot + suppression-freeze logic directly into `CycleService.CloseAsync`** (or a wrapper that still runs the
Open→Closed transition through it) rather than building a parallel close path — mirrored here exactly as (a)'s
submission-lock handoff was, so it is not silently dropped either.

**(b) BU onboarding — write path.** `BusinessUnit.IsOnboarded` (HAP-3) is set `false` at `Create()` and never
mutated anywhere in the codebase — no admin endpoint exists to onboard a BU, even though root spec line 31
lists "BU onboarding" as a Platform Admin capability and HAP-7's own AC 3 (mid-cycle onboarding test) requires
onboarding a BU as test setup. No other backlog story currently owns this write path. HAP-7 adds a minimal
`BusinessUnit.SetOnboarded(bool)` + `[PA] POST /api/admin/business-units/{id}/onboard` since it is required to
make HAP-7's own acceptance criteria testable and fits the story's already-stated file scope
(`Hap.Domain/**` + "endpoints in `Hap.Api`").

**(b-elevation, L2 panel round-1, hap-domain-specialist) BU onboarding — audit trail is a future L3 obligation,
not a HAP-7 gap to close now.** Every other admin mutation in this codebase writes an `AuditLog` row (org
overrides, role grants); this one does not, because no `AuditAction` case fits "BU onboarded" and none of
HAP-7's cited FRs call for one. Constitution Art. VI.4 requires Harris submission figures to reconcile to
underlying records, and `IsOnboarded` directly gates which people feed cycle participation/completion figures
that ultimately reconcile into those submissions. **HAP-7 deliberately does NOT add the audit row here** —
doing so would pull an L2 story into the L3 audit-write path (`Hap.Infrastructure/Audit/**`) for an advisory
fix disconnected from any current reconciliation consumer. Instead: **whichever Wave-2 story first makes the
`IsOnboarded` flag feed a reconciled participation or Harris figure MUST add an `AuditLog` row (a new
`AuditAction` case) to the onboarding write path as part of that story, and that addition is L3** (it touches
Harris submission generation's aggregation inputs) **regardless of how small the mutation itself looks.**

**Blocks:** nothing locally — all three are additive/deferred obligations, not open defects. **Owner action:**
confirm (a) HAP-8/HAP-9 story files should be updated to cite the late-override consult obligation, (a-addendum)
HAP-10's story file should be updated to cite the CloseAsync hook-in obligation, (b) the BU-onboarding endpoint
belongs here rather than a dedicated future story, and (b-elevation) the audit-row obligation is correctly
assigned to the future Wave-2 story that first reconciles the flag, not retrofitted here.
**Status:** OPEN (provisional answers in effect; HAP-7 proceeds on all three; (a-addendum) and (b-elevation)
are forward obligations on HAP-10 and a future Wave-2 story respectively)

## 2026-07-22 · HAP-9 (manager moderation) · Q-018 — Is a manager's moderation write itself audited (ScoreChange), or only the read (IndividualView)?

The story's binding audit requirement is the `[A]` read: `GET /api/team/members/{id}/assessment` writes exactly one
`IndividualView` `AuditLog` row (fail-closed). contracts/api.md's "Contract tests (minimum)" lists only the
`IndividualView` audit assertion; `PUT /api/team/reviews/{id}` (the moderation write) is **not** marked `[A]` and no
acceptance criterion mentions auditing it. But **FR-050** enumerates "score changes" as a MUST-audit action, and the
moderation write produces the moderated score of record (FR-010) — the single most reporting-consequential score
change in the system. HAP-8 set a precedent of NOT auditing *self* score writes ("viewing your own data is not
audited"), but that is a caller==subject case; moderation is a manager writing another person's score of record.

**Provisional answer in effect (per CLAUDE.md §6.3; uncertainty rounds up on the safeguarding seam):** a successful
moderation write stages exactly one `ScoreChange` `AuditLog` row (actor = moderating manager, subject = the report,
detail = assessmentId/cycleId + count of dimensions moderated), on the same context/transaction as the score writes
and the `Submitted → Moderated` transition, so an audit-write failure rolls the whole moderation back (fail-closed),
mirroring `OrgOverrideService`. This is additive to the mandatory `IndividualView` row on the member-assessment GET
and does not affect that endpoint's exactly-one-row count (different action, different endpoint). Self score writes
stay unaudited (HAP-8 precedent).

**Blocks:** nothing — a reversal (contract's IndividualView-only minimum is intended) is a trivial removal of one
staged row + its test. **Owner/panel action:** confirm the moderation write should be `ScoreChange`-audited, or ratify
the contract's IndividualView-only minimum.
**Status:** **RESOLVED 2026-07-22 — KEEP (HAP-9 L3 panel, domain specialist = spec authority).** FR-050 explicitly
names "score changes" as a MUST-audit action, distinct from HAP-8's caller==subject self-write exemption; the
moderation write produces the score of record, so it MUST be audited. Keep the single `ScoreChange` row, atomic +
fail-closed with the score writes and the state transition (red-team confirmed the transaction is atomic). Additive
to the mandatory `IndividualView` row on the member GET.

## 2026-07-22 · HAP-9 (manager moderation) · Q-019 — Does FR-063 carry-forward also carry the prior cycle's moderation COMMENT?

FR-063 defaults manager review to carrying the prior cycle's moderated score forward when the self-score is unchanged.
When that prior moderated score diverged ≥2 from the self-score (a sustained Δ≥2 moderation), the carried-forward
DEFAULT is itself a Δ≥2 decision — which FR-009 says requires a manager comment. The prior cycle's comment exists, but
carrying it forward silently would let a sustained Δ≥2 be re-accepted with no fresh manager attention each cycle
(defeating the point of the comment requirement), while NOT carrying it forces a fresh comment every cycle even when
nothing changed.

**Provisional answer in effect (per CLAUDE.md §6.3; strict FR-009):** carry the prior *score* forward but NOT the
prior *comment* — each cycle's Δ≥2 moderation must capture a fresh comment. The member-read payload flags this per
dimension (`defaultCommentRequired`), so the client pre-empts the forced-comment state on the DEFAULT and an
"accept all defaults" PUT with no comment is rejected 422 exactly as an edited Δ≥2 is (GET and PUT agree). This is what
the shipped fix implements, so no behaviour hangs on the answer — a "carry the comment too" ruling would be an additive
relaxation (pre-fill the prior comment as the default), not a rework.

**Blocks:** nothing. **Owner action:** confirm fresh-comment-each-cycle, or rule that a sustained-unchanged Δ≥2 may
carry the prior comment as its default.
**Status:** OPEN (provisional answer in effect; HAP-9 ships the strict-FR-009 reading)

## 2026-07-22 · HAP-9 (manager moderation) · Q-020 — Who moderates a senior leader whose reviewer-of-record has no individual-read capability?

The HAP-9 L3 red-team found (and the fix now enforces) that **moderation ⊆ read**: a caller who cannot READ a subject's
individual scores cannot moderate them. This is correct and closes a score-oracle leak. But it exposes a real gap in the
assessment *model*: a senior leader's reviewer-of-record is often a role FR-025 clause 2 caps at aggregates-only — e.g. a
Portfolio Leader's direct manager is the HIG Executive, who has NO individual-read capability. Under the fix, the Exec
(correctly) cannot moderate the Portfolio Leader — and neither can anyone else (a skip-level manager above the reviewer
of record is also denied, and there is no other reviewer). So **a senior leader whose reviewer-of-record lacks read
capability goes un-moderated.**

**Provisional answer in effect (per CLAUDE.md §6.3; fails closed):** such assessments are simply not manager-moderated;
at cycle close they **auto-adopt the self-score** as the moderated score of record, flagged `unmoderated` (the existing
FR-068 mechanism, HAP-10) — no new moderation path is invented, and no access rule is relaxed. The security fix (moderation
⊆ read) stands **regardless** of how this is ruled. Possible owner rulings: (a) accept auto-adoption for these few
top-of-tree roles; (b) designate an explicit moderator via a `BuDelegate`-style grant or a dedicated senior-review role;
(c) rule that above-BU leaders self-attest without moderation. Each is additive (a grant/role or a policy note), not a
change to the seam's read rule.

**Blocks:** nothing locally. **This is an assessment-model gap the owner must rule on** (flagged prominently in the HAP-9
story notes). **Owner action:** decide how senior leaders (whose reviewer-of-record is aggregates-only) get moderated,
or ratify auto-adoption for them.
**Status:** OPEN (provisional answer in effect; the security fix is independent of the ruling)

### Q-020 addendum (a) — 2026-07-22, HAP-9 re-panel (domain, scope boundary)

The moderation⊆read denial of an explicit-grant leader **extends DR-0005, it does not contradict it.** DR-0005 ratified a
one-hop direct read+moderate "regardless of the manager's tier" — but that ruling is about a **grant-LESS hierarchy**
Group/Portfolio Leader, who classifies as a plain `Manager` via `ClassifyReader`'s `HasDirectReports` fallback and keeps
their one-hop grant. An **explicit-grant** holder (HIG Executive / Platform Admin / Group-Viewer) is classified by their
grant FIRST — they never reach the `HasDirectReports` branch — and `RoleScope.IndividualReadCapability` returns false for
them by construction (FR-025 clause 2). So the two rulings partition cleanly: DR-0005 governs the hierarchy-tier reader;
Q-020 governs the explicit-grant reader. No conflict.

### Q-020 addendum (b) — 2026-07-22, HAP-9 re-panel (code, BU-delegate escalation edge — fold into this ruling)

A related edge the same owner ruling should cover (pre-dates the batch-3 fix; fails closed, so not patched now): a
**BU-delegate** who becomes the ESCALATED reviewer-of-record of a subject homed OUTSIDE their delegated BU (e.g. the
subject's contractor manager is skipped and the delegate is the next eligible ancestor across a BU boundary) is **denied
moderation** — `AuthorizeIndividualRead`'s BusinessUnit reach requires the subject be homed in the delegated BU (or a
direct report), so the read conjunct fails and, with it, moderation. This is a safe under-grant (fails closed), and it is
the same shape of "who moderates when the natural reviewer can't" question as the senior-leader gap above, so it rides
the same Q-020 owner ruling rather than a separate fix.

## Q-021 — the docker-compose runtime cannot boot end-to-end (local-runtime drift)

**Raised:** 2026-07-22 (session lead, while starting the stack for an owner UI test) · **Type:** runtime/infra gap, not a spec ambiguity

Running the stack per `specs/001-maturity-initiative-register/quickstart.md` does **not** produce a working app — three
gaps, none caught by `verify.sh` (which uses its own DB harness and never exercises the compose runtime path):

1. **No schema migration at startup.** `Program.cs` never calls `db.Database.Migrate()`, and nothing else does, so the
   compose Postgres starts empty (`relation "people" does not exist`). Migrations only ever run inside `verify.sh`.
2. **Synth data + framework files never reach the api image.** `backend/Dockerfile` publishes only the API to `/app`;
   `directory.json`, `seed-users.json`, and `docs/frameworks/ai-maturity-sdlc.v1.json` are read from `AppContext.BaseDirectory`
   (=`/app`) at runtime but are neither COPY'd into the image nor bind-mounted in `docker-compose.yml`. (The framework
   locator's own comment already acknowledges this "known gap.")
3. **Bootstrap deadlock.** `LocalDevProvider.SignInAsync` requires a synced `Person`; `POST /api/admin/sync` requires a
   `PlatformAdmin` session — so no one can perform the first sync. There is no seed-on-boot or bootstrap admin.

The owner tested on 2026-07-22 via a **manual, runtime-only bootstrap** (host `dotnet ef database update` → `docker cp`
the three files in → SQL-seed one admin Person+grant → drive sync/framework/cycle over the API). That instance is
ephemeral (lost on api-container recreate) and no repo files were changed for it.

**Proposed fix (a small "runtime bootstrap" story):** startup `Migrate()` behind a dev/env flag; COPY the synth output +
`docs/frameworks` into the runtime image (or bind-mount in compose); and a seed-on-boot (or a documented, auth-exempt
first-run bootstrap) so `docker compose up` + the quickstart actually work. Reconciles the stack with constitution Art.
IX.1 ("`docker compose up -d --build` is the whole stack").

**Owner decision needed:** file this as a new backlog story, or treat it as **HAP-1 (scaffold-and-gate) drift** to be
repaired under that story's remit? **Status:** OPEN.

## Q-022 — auto-adoption (FR-068) vs a post-close late override (Q-017a): may an auto-adopted assessment be re-moderated?

**Raised:** 2026-07-22 (HAP-10 dev) · **Type:** behaviour reconciliation between two shipped features · **Status:** PROVISIONAL (fail-safe; flag for panel/owner)

Cycle close auto-adopts every Submitted-but-unmoderated assessment (FR-068). Separately, a late override reopens the
submission/moderation window for an individual **after** close (Q-017a; HAP-9). The two collide when an override is granted
**post-close** (which the HAP-9 acceptance tests do — `Post_close_moderation_is_locked_423_unless_a_late_override_exists`
and `Queue_CanModerate_honours_a_post_close_late_override…`): by then close has already auto-adopted the report, so its
state is `AutoAdopted`, and the pre-HAP-10 moderation guard (`state == Submitted`) would now reject the very moderation the
override is meant to enable (409). Excluding override-holders from auto-adopt at close does NOT solve it — at close there is
no override yet.

**Provisional resolution (implemented):** a late override lets a manager **re-moderate an `AutoAdopted` assessment**
(`AutoAdopted → Moderated`), replacing the auto-adopted self-score placeholder with a genuine manager review and clearing
the `unmoderated` flag. Moderation now accepts `Submitted` **or** `AutoAdopted` (domain `Assessment.Moderate`, seam
`ModerateAsync`, queue `CanModerate`, member-read `editable`), still gated by the same submission lock (post-close requires
the override) and unchanged for `Moderated`/`InProgress`/`NotStarted` (still 409). This preserves both features and keeps
the HAP-9 tests green. Rationale: auto-adoption is a *provisional* placeholder for "review never happened"; an override is
an explicit instruction to let the review happen after all, so it should supersede the placeholder.

**Why it needs a ruling:** it slightly widens the `Moderate` contract (an L2/L3 moderation-path change) and means a
rollup snapshot taken at close can later be contradicted by a subsequent moderation of an override-holder — but only for a
person explicitly re-opened by an admin/manager, and the snapshot itself remains immutable (a re-close/regeneration
question, not a mutation). If the owner prefers auto-adoption to be terminal (override reopens *submission* only, never
*moderation*), revert to `Submitted`-only and instead exempt override-holders at close — but that requires overrides to be
granted before close, contradicting the current HAP-9 tests.

## Q-023 — cross-BU / manager-less people in Team rollups: teamless / BU-direct

**Raised & ruled:** 2026-07-22 (HAP-10 dev + panel round-1 domain specialist) · **Type:** aggregation-model ruling · **Status:** PROVISIONAL-IN-EFFECT, L3-panel-decidable, NO owner needed (same class as Q-008/Q-016)

At cycle close the Team rollup node is keyed by manager id, but BU/Group/Portfolio nodes are keyed by home-BU containment. Two synth/admin edges make a person's home BU differ from their manager's: **HAP-EDGE-XBU-REPORT** (home BU02, managed by a BU01 lead) and an admin `OverrideField.Manager` re-point. Keying a Team by manager id alone then places a member in a team rooted in a BU they do not belong to → Σ(team n) can exceed a BU's n → `SuppressionEvaluator` throws (500 close) or freezes a wrong (under-)suppression verdict.

**Ruling (domain, implemented):** a Team node exists only where the report **and** their manager share a **home BU**. Anyone without a same-BU manager — manager-less (BU heads) OR cross-BU-managed — is **TEAMLESS**: counted only at BU/Group/Portfolio/AllHig via their **home BU**, in no Team node. Rejected alternatives: per-BU team fragments (no support — CLAUDE.md §8.3, hierarchy mappings are data not code, so we don't fabricate synthetic team nodes); routing scores through the manager's BU (home-BU attribution is root spec §3.5/Q-016 and stays unchanged). Their manager still REVIEWS them via the existing DR-0006/FR-070 reviewer-of-record path — this changes only the aggregate Team-rollup node. Consequence: every Team nests within exactly one BU → Σ(team n) ≤ BU n always → no throw, correct frozen suppression, and the reconciliation invariant is the team-homed carve-out (see the HAP-10 snapshot-totals AC, spec-corrected). Locked by `A_cross_bu_managed_report_is_teamless…` and `A_manager_less_scored_bu_head_is_teamless…`.

## Q-022 addendum — reconciliation is "as of close" (frozen snapshot legitimately diverges from post-close raw edits)

**Added:** 2026-07-22 (HAP-10 panel round-1 F3). Because a post-close late override may re-moderate an already-auto-adopted assessment (the Q-022 ruling), the raw `AssessmentScore` rows for that cycle can change AFTER the `RollupSnapshot` was frozen at close. This is **intended immutable history, not a desync**: the snapshot is the authoritative "as of close" figure, and any future reconciliation check MUST compare a freshly-recomputed close-state (or read the snapshot itself), never live post-override rows against the frozen snapshot. Enforced by the append-only snapshot (byte-for-byte freeze test `A_post_close_override_re_moderation_never_alters_the_frozen_snapshot`). If the owner later wants post-override edits reflected in trend history, that is a NEW re-close/regeneration story (a new immutable snapshot), never an in-place mutation.

## 2026-07-22 · HAP-11 (rollups & BU dashboard) · Q-024 — May aggregate-read scope for Group/Portfolio/Executive be resolved from hierarchy-tier labels (Q-014) when the individual-read seam forbids them?

HAP-11 exposes aggregate rollup reads (`GET /api/bus/{buId}/dashboard`, `GET /api/org/{nodeType}/{nodeId}/rollup`, `GET /api/me/team/summary`, all **[S]**). Scoping a Group/Portfolio Leader to their own node needs a "leads this unit" anchor — exactly the Q-014 depth-derivation that the *individual-read* seam is forbidden to consume. The only explicit grants are `HigExecutive` (→ AllHig) and `BuDelegate(bu)` (→ that BU); a hierarchy Group/Portfolio Leader has **no** explicit grant, so without the hierarchy label they cannot be scoped at all, and the seeded Group/Portfolio Leader fixtures (which carry no grant) could not read their own rollup — the role-matrix AC would be unmeetable.

**Provisional answer in effect (per CLAUDE.md §6.3):** aggregate-read scope IS resolved from `HierarchyRoleResolver`'s `BuLeadOfBusinessUnitId` / `GroupLeaderOfGroupId` / `PortfolioLeaderOfPortfolioId` anchors (**FR-022** — "derive leadership role visibility automatically from the org hierarchy"), plus the explicit grants above. This is consistent with the owner's Q-014 posture (RESOLVED-AS-DEFERRED: keep depth-from-root for the synthetic build) and is **strictly separated** from the individual-read gate: `AssessmentReads` is untouched and remains grant/structural-only, so no individual score is reachable through any of these hierarchy labels. Exposure is further bounded because **every aggregate is N<4 + complement suppressed** — a mis-scoped leader (via the resolver's uniform-depth limitation) could at worst see a *sibling* node's already-suppressed aggregate means, a confidentiality (not individual-privacy) concern. Fail-closed: a caller the resolver cannot anchor and who holds no grant gets 404.

**Blocks:** nothing locally (synthetic data). **G1 owner-ratification requested** (rides the Q-014 real-data anchor decision): confirm hierarchy-derived aggregate scope is acceptable, or require an explicit leader-anchor / `GroupViewer`-style grant for group/portfolio aggregate reads. If ruled restrictive, the fallback is explicit-grant-only aggregate scope (a scope-resolver change + test update; the rollup maths and suppression are unaffected).

### Q-024 ruling — 2026-07-22, HAP-11 panel round 1 (session lead)
**ACCEPT-PROVISIONAL; owner ratification at G1.** FR-022 mandates hierarchy-derived aggregate visibility; the individual-read cap (`AssessmentReads`) is untouched; and every aggregate is N<4 + complement suppressed (now **hierarchy-globally** — see BR1/Q-026), so a mis-scoped leader can at worst reach a *sibling* node's already-suppressed aggregate — a confidentiality, never an individual-privacy, concern. The G1 witness script includes a Group/Portfolio Leader reading their own vs a sibling node.

**GroupViewer wiring (resolved here):** `OrgRole.GroupViewer` is an explicit grant with **no group anchor** on the `RoleGrant` row (data-model.md: `BuDelegate(bu_id)` carries a BU id; the "explicit viewer grants" carry none). So a `GroupViewer` grant **cannot be scoped to a specific group** structurally, and is therefore **deliberately NOT wired** into aggregate scope in v1: a person who is a hierarchy Group Leader gets their group via the hierarchy anchor (above); a bare `GroupViewer` grant with no hierarchy position resolves to no group and falls through to their own team / 404 (fail-closed). Wiring `GroupViewer` to a group needs a group-anchor column on `RoleGrant` — a schema change deferred with the Q-014 real-data anchor.
**Status:** OPEN — provisional in effect (ACCEPT), owner ratification at G1; GroupViewer exclusion recorded.

## 2026-07-22 · HAP-11 (rollups & BU dashboard) · Q-025 — "floor-level histogram per dimension (FR-018)": what is actually delivered?

The story AC reads "distribution view returns floor-level histogram per dimension (FR-018)". FR-016 defines a *person's* floor as the min across their dimensions (one value per person, not per dimension), and `RollupSnapshot` stores per-dimension **mean** plus one **node-level** floor-level distribution (count of people by floor level) — it does not store a per-dimension level histogram. The binding mockup (`dashboard-bu.html`) shows per-dimension mean bars + a node-level floor-distribution KPI ("78% at L1+, 22% at L0"), not per-dimension histograms.

**Provisional answer in effect (per CLAUDE.md §6.3):** deliver per-dimension **means** (FR-015) and the node's **floor-level histogram** (FR-016/018 — people-by-floor-level), both reconcilable across the live/snapshot dual path. A per-dimension level *histogram* is neither stored nor reconcilable and is not produced; the mockup's per-dimension "level" chip is `round(mean)` derived in the UI (presentation only). This satisfies US3 scenario 1 ("distribution of floor-based levels, e.g. 12% Level 2+") and the mockup.

**Blocks:** nothing. **Owner/panel action:** confirm the node-level floor distribution + per-dimension means reading, or clarify if a genuinely per-dimension level histogram is wanted (would need a new stored figure at close — an L2 snapshot-schema change, re-opening HAP-10).

## 2026-07-22 · HAP-11 (rollups & BU dashboard) · Q-026 — cross-level differencing: suppression strengthened from per-parent to hierarchy-global

**Raised & resolved:** 2026-07-22 (HAP-11 red-team BR1, round 1) · **Type:** hardening of a shipped safeguard · **Status:** RESOLVED — no new DR (hardens research D2, does not change its semantics)

The per-parent `SuppressionEvaluator.EvaluateLevel` (HAP-10, research D2) enforced the differencing defence ONE parent-child level at a time. Red-team proved a cross-level leak: a node suppressed high in the tree is recoverable by summing PUBLISHED nodes at other levels (`AllHig(14) − GroupA(11) = 3` recovers a suppressed sub-4 branch's N and its per-dimension mean). Research D2's STATED goal is "closes the subtraction attack within v1's only publication surface (fixed rollups)"; the per-level implementation did not fully achieve that goal across levels.

**Resolution (implemented):** a new `HierarchySuppression.Close` runs after the per-parent pass inside the shared `RollupPipeline` (so it binds BOTH live reads and the frozen close snapshot): equal-membership collapse (single-child/no-slack chains suppress together) + a fixpoint over the tree identities (`parent = Σchildren + teamless-slack`, teamless as an unknown phantom) that suppresses additional published nodes until no suppressed node's count is determined by the published set. Protecting the count-system also protects every mean (identical known-set + structure). Exhaustively property-tested (`HierarchySuppressionTests`) + an integration attack test (`Executive_cannot_recover_a_suppressed_sub4_branch_by_differencing`).

**Why no DR:** this HARDENS D2 toward its own stated goal (close the subtraction attack), it does not reverse or re-scope a ratified decision. It DOES strengthen HAP-10's frozen verdicts (more suppression) — but HAP-10's own suppression fixture has no cross-level leak (its two suppressed sibling teams share a parent with two unknowns), so all HAP-10 CycleClose tests pass unchanged; the strengthening is documented as-built in `docs/wiki/cycles-and-assessment.md`. Flagged to the session lead; a DR can be minted if the panel prefers one.
**Blocks:** nothing. A G1 witness item (differencing across levels, all seven roles).

### Q-025 ruling — 2026-07-22, HAP-11 panel round 1 (session lead)
**OWNER-RULING-NEEDED (non-blocking; provisional in effect).** The UI is mockup-faithful (per-dimension mean bars + a node-level floor-level distribution KPI, now derived from the honest per-person floor distribution per BB1, not a rounded mean). But FR-018's text wants true **per-dimension** level histograms "to surface bimodal teams." The owner must rule: (a) accept the current node-level-distribution + per-dimension-means shape (revise the FR-018 wording), or (b) require genuine per-dimension level histograms — which needs a new stored figure at cycle close (an L2 `RollupSnapshot` schema/migration change re-opening HAP-10) and a matching revision to the binding `dashboard-bu.html` mockup. Flagged prominently because it touches a **binding mockup** and possibly a schema.
**Status:** OPEN — OWNER-RULING-NEEDED; provisional (current shape) in effect.
