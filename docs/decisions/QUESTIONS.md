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
