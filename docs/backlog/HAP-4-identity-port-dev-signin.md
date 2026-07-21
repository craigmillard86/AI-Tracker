---
id: HAP-4
title: IIdentityProvider port, local dev provider, and role-picker sign-in
epic: E1-foundations
wave: 0
fr: [FR-055, FR-056]
risk: L3                # trigger: HierarchyRoleResolver walks the management chain — CLAUDE.md §7 L3
                         # "management-chain resolver"; reclassified from L2 by panel re-derivation on
                         # 2026-07-21 (hap-domain-specialist finding, session-lead ruling) — first
                         # match / higher class wins, and a reviewer's re-derived class disagreeing
                         # with the declared one forces the higher class per the lead's binding rule.
status: done
estimate: {dev: M, qa: S}
worklog:
  - {phase: dev, start: 2026-07-21T17:35:20Z, end: 2026-07-21T19:08:03Z, mins: 92}
  - {phase: qa, start: 2026-07-21T19:13:25Z, end: 2026-07-21T19:25:15Z, mins: 11}
closure:
  sha: 4f3f83b
  files: [backend/src/Hap.Api/Identity/**, backend/src/Hap.Api/AdminEndpoints.cs, backend/src/Hap.Api/FrameworkEndpoints.cs, backend/src/Hap.Api/Program.cs, app/src/screens/signin/**, app/src/design/tokens.css, backend/tests/Hap.Api.Tests/Identity/**, docs/wiki/identity.md]
  tests: "backend 166 (Api 98 incl. Identity + escalation + fresh-QA adversarial); PrivacyReporting 22; frontend 74; both EF migrations (#1 org/audit, #2 framework) idempotent; verify.sh ALL GREEN"
  risk: L3
  reclassified_from: L2
  panel: [hap-code-reviewer, hap-domain-specialist, hap-red-team, hap-design-reviewer]
  date: 2026-07-21
  note: "Reclassified L2→L3 (§7 management-chain-resolver trigger, hap-domain-specialist re-derivation, lead ruling). The added red-team pass FOUND a concrete privilege escalation (authenticated Individual self-reparenting under the org root via the 401-only override endpoint to mint a Portfolio Leader label); ruled fix-now (not deferred): PlatformAdmin (403) now gates all admin surfaces and the escalation is a passing regression test."
  g1_preconditions:
    - "Q-014 (hierarchy-tier derivation): depth-from-root tier assignment assumes a uniform-depth org tree — correct for the synthetic generator, misassigns on real org shapes (interim/dual-hat layer, missing tier). Owner-ratification item (Q-006 precedent). HAP-5 (or whichever story consumes HierarchyRoleResolver output for visibility scope) MUST NOT do so until Q-014 is owner-ratified with a structural 'leads this unit' anchor OR HAP-5's own L3 red-team independently clears the derivation. Blocks any non-synthetic BU onboarding."
    - "Admin-endpoint [PA] gate: DISCHARGED here (PlatformAdmin policy on /api/admin/sync, /api/admin/overrides, /api/admin/frameworks) — this closes the HAP-3 wave-0 deferral for the admin surfaces. HAP-5 still owns the assessment-read 403/404 role-scope matrix."
  carry_forward:
    - "HAP-5 handoff (load-bearing): BU-scoped (BuDelegate) grants flatten to a bare role string in the cookie — BusinessUnitId scope is NOT carried in the claim, and claims persist up to the 8h sliding window. The Authorization seam MUST re-read RoleGrant rows from the DB for any BU-scoped decision, never trust the cookie claim alone."
    - "Future role-grant revoke endpoint (Q-013 addendum, QA finding): the two bootstrap fixtures (Platform Admin, HIG Executive) re-acquire their grant from the seed label on every sign-in via LocalDevProvider.EnsureExplicitGrantAsync — a DB revoke does not stick across a sign-out/sign-in for these two. Not a cross-identity escalation; the revoke endpoint must special-case these fixtures."
    - "Identity-audit-actor wiring: OrgOverrideService writes actorPersonId:null (HAP-3 deferral). A future story should populate actorPersonId from the authenticated principal's person_id claim and retire CreateOverrideRequest.CreatedBy as a client-supplied field."
---
## Story
As any of the seven seeded roles, I can sign in via the local dev provider's role picker and receive a session whose computed roles drive everything I subsequently see, so the app authenticates through the identity port exactly as a future Entra adapter would.

## Context
- Spec: FR-055 (identity port; dev provider local; Entra deferred), FR-056 (roles = hierarchy-derived + explicit grants); "Users and roles" table (who sees what).
- Plan: research **D3** (cookie auth; claims principal shape identical to what OIDC middleware would produce); contracts/api.md "Ports — IIdentityProvider" + "Auth" endpoints. Hierarchy-derived roles are COMPUTED from org structure (data-model.md RoleGrant note), never stored.
- Files: `backend/src/Hap.Api/Identity/**` (port + LocalDevProvider + cookie config), `app/src/screens/signin/**` (role picker — **no mockup exists**; see QUESTIONS.md Q-004: build minimal, DESIGN.md-conformant, cards + buttons only), seed users from `seed-users.json` (HAP-2).
- Out of scope: Entra ID OIDC adapter (deferred decision-recorded story). The port must compile against a second hypothetical adapter — contract test only, no implementation.
- Blocked by: HAP-3
- Parallelisable: yes, with HAP-6 (disjoint files; HAP-6 owns the migration chain slot)

## Acceptance criteria
- [ ] `GET /auth/signin` lists the seeded users (one per role minimum) from seed data, not hard-coded; `POST /auth/signin {userKey}` sets an auth cookie; `POST /auth/signout` clears it.
- [ ] The claims principal contains person_id + explicit roles only; hierarchy roles (Manager, BU Lead, Group Leader, Portfolio Leader) are computed per request from org data — integration test: promoting a person's manager link changes their computed role without re-sign-in.
- [ ] A contract test asserts the principal shape matches contracts/api.md (person matched by external_ref) — the seam consumes nothing provider-specific (research D3).
- [ ] `GET /api/me` returns profile + computed roles for each of the seven seeded users (parameterised integration test).
- [ ] Unauthenticated requests to any `/api/*` endpoint return 401 (test sweeps all mapped routes). This MUST include the HAP-3 admin endpoints (`POST /api/admin/sync`, `GET/POST /api/admin/overrides`), which shipped un-gated in wave-0 (HAP-3) with the guard left as a marked extension point (`AdminEndpoints.cs` `MapGroup("/api/admin")`).
- [ ] Sign-in screen uses tokens.css only (no new colours/type sizes); strings externalised (FR-067); axe scan passes (vitest-axe).
- [ ] `./scripts/verify.sh` green.

## Attempts / notes

- 2026-07-21: No prior attempts found (`git log --all --grep "HAP-4"` empty). Read spec FR-055/056,
  research D3, contracts/api.md (Ports/Auth/Self scope), data-model.md RoleGrant note, QUESTIONS.md
  Q-004, and the full HAP-2/HAP-3 org model (Person/BusinessUnit/GroupOrg/Portfolio/RoleGrant/
  DirectoryImportService/AuditWriter) plus the DirectoryGenerator's engineered org shape.
- Two genuine ambiguities found; both provisional per CLAUDE.md §6.3, logged to QUESTIONS.md Q-013/Q-014
  (renumbered from Q-011/Q-012 on rebase onto main — HAP-6 landed its own Q-011/Q-012 first; see the
  dated renumbering note below), proceeding on the provisional rather than blocking:
  - **Q-013** (was Q-011): who seeds the explicit `RoleGrant` rows (PlatformAdmin/HigExecutive) for the
    two seed fixtures that need them, given the role-grant admin endpoint is a later story (HAP-3/7/12/18)?
    Provisional: `LocalDevProvider` idempotently ensures the grant at sign-in time, driven entirely by
    the seed-users.json `role` field (never a hardcoded external_ref — Hap.Api does not reference
    Hap.Synth), audited via the existing `RoleGrant` AuditAction.
  - **Q-014** (was Q-012): the domain model documents hierarchy roles as "computed from org structure" but neither
    `BusinessUnit`/`GroupOrg`/`Portfolio` nor `Person` carries an explicit "this person leads this unit"
    pointer, and no EF migration is available to add one (HAP-6 owns the slot). Naive heuristics (home-BU
    coincidence, subtree span) provably misclassify against the generator's own engineered edge cases
    (BU01 hosts the entire Exec→Portfolio→Group→BU leadership chain simultaneously; the cross-BU report
    edge case; the null-manager directory-gap edge case). Provisional: classify by **manager-chain depth
    from a validated root** (root = active, no manager, ≥1 active report — this excludes the null-manager
    directory-gap fixture, which has 0 reports): depth 1 = Portfolio Leader, depth 2 = Group Leader,
    depth 3 = BU Lead, of whichever BU/Group/Portfolio the person is themselves homed in. Generic
    "Manager" = has ≥1 active direct report, independent of depth. Verified against all seven seed users
    plus the BU01-collapse and null-manager fixtures in `HierarchyRoleResolverTests`. Flagged for the
    future Authorization (L3) seam story to reconsider if an explicit leader pointer is ever added.
- No mockup for the sign-in screen (Q-004) — built minimal, tokens.css only, cards + buttons.
- Added a Vite dev-server proxy (`/auth`, `/api` → the API origin, configurable via
  `VITE_API_PROXY_TARGET` for the docker-compose network) instead of CORS, so the browser only ever
  talks to the app origin and cookie auth needs no cross-origin exception.
- Did not wire `OrgOverrideService`'s `actorPersonId: null` TODO (org-overrides audit rows) to the new
  principal — that touches HAP-3's `AdminEndpoints`/`OrgOverrideService` request contract (dropping the
  `createdBy` free-text field in favour of the authenticated principal's person id) and is left as its own
  follow-up story. **Update (2026-07-21, code-reviewer advisory, round 2):** the precondition originally
  named here ("once `RequirePlatformAdmin()` lands") is now satisfied — BLOCKING 5 landed it in this very
  story. The actual remaining work is: whichever future story wires per-request audit attribution should
  populate `actorPersonId` from `ClaimsPrincipal.FindFirstValue("person_id")` (available on every request
  past the `[Authorize("PlatformAdmin")]` gate) rather than leaving it `null`, and drop `CreateOverrideRequest.CreatedBy`
  as a client-supplied field once that lands (the authenticated actor is a strictly more trustworthy source
  than free text the caller types in).
- **KNOWN MERGE-TIME ITEM (flagged by session lead, HAP-6 coordination):** HAP-6 (parallel branch) adds
  `MapFrameworkEndpoints()` — `GET /api/frameworks/current` and `[PA] /api/admin/frameworks/**` — mapped
  directly on `app` in its own copy of `Program.cs`, predating this story's `app.MapGroup("/api")
  .RequireAuthorization()` group. Whichever of us merges SECOND must explicitly re-home
  `MapFrameworkEndpoints()` under the authorized `api` group (same treatment `AdminEndpoints` got here —
  see `AdminEndpoints.MapAdminEndpoints(this IEndpointRouteBuilder app)` for the pattern).
  `Hap.Api.Tests/Identity/UnauthenticatedSweepTests.cs`'s route sweep enumerates `EndpointDataSource` by
  path prefix (`/api/**`), not by which group mapped a route — so it will catch a stray anonymous
  `/api/frameworks/*` endpoint automatically and fail red, with no test edit needed either way. A failing
  sweep right after the second merge is exactly the safety net working as designed, not a flake.
- **RECLASSIFIED L2 → L3 (2026-07-21, panel finding + session-lead ruling):** self-classified this story
  L2 at Phase 1 against the declared trigger ("IIdentityProvider implementations & session handling").
  The L2 panel's `hap-domain-specialist` correctly identified that `HierarchyRoleResolver` walks
  `Person.ManagerPersonId` chains — CLAUDE.md §7's L3 "management-chain resolver" trigger, a
  path-independent trigger separate from (and not superseded by) the L2 identity/session trigger I cited.
  Per the lead's binding rule, first match / higher class wins whenever a reviewer's re-derived class
  disagrees with the declared one. This is not a defect in the Q-014 provisional, the fail-closed
  exception handling, or the edge-case test coverage — it is a panel-composition gap self-classification
  cannot catch on its own (a management-chain resolver needs `hap-red-team`, which an L2 panel doesn't
  include). Full L3 panel (`hap-code-reviewer` + `hap-domain-specialist` + `hap-red-team`) now in flight;
  design reviewer (`hap-design-reviewer`, for the sign-in UI) also in flight from the original L2 sizing.
  No code change accompanies this reclassification — doc-only, per the lead's explicit instruction not to
  churn the tip while `hap-red-team` reviews the resolver (its brief includes the uniform-depth-tree
  misassignment hypothesis and the deactivated-root subtree case the domain specialist raised).
- **`./scripts/verify.sh` green run evidence (record):** ran the full gate of record end-to-end against a
  disposable Postgres before the initial dev-complete report. Tail:
  ```
  ==> [8/9] Frontend production build
  ✓ built in 579ms
  ==> [9/9] Asserting no external font request in built output

  verify.sh: ALL GREEN
  ```
  Full run: backend build clean; 116 backend tests green (Hap.Domain.Tests 6, Hap.Architecture.Tests 3,
  Hap.Synth.Tests 41, Hap.Api.Tests 66); `Category=PrivacyReporting` suite green (9 tests); frontend
  lint/typecheck/73-tests/build all green; migrations applied idempotently twice (second run: "No
  migrations were applied"). `hap-code-reviewer` independently confirmed green at tip `b0ea55d`.
- **L3 panel round 1 remediation (2026-07-21) — blocking items 1–4 applied, doc-only advisories recorded,
  one item held for the red-team package:**
  - **Fixed (hap-code-reviewer #1):** `LocalDevProvider.SignInAsync` carried a stray `external_ref` claim
    beyond the contract shape (contracts/api.md + AC2: "person_id + explicit roles only") that nothing
    consumed — removed.
  - **Fixed (hap-code-reviewer #2):** `ContractShapeTests` was named "…carries_exactly…" but only asserted
    `person_id` present and the Role-claim set empty — never the FULL claim set, which is exactly why the
    stray `external_ref` claim slipped through undetected. Added `AssertExactClaimSet` (asserts the
    principal's entire `Claims` collection, not a subset) and a second case exercising a person WITH an
    explicit grant, so both "no grants" and "one grant" paths prove nothing extra rides along.
  - **Design fix (hap-design-reviewer #4):** `.signin-card-panel` had no `box-shadow`, violating DESIGN.md
    A4 ("Cards/panels: … card-elevation shadow"). Not an addendum gap — the value was already documented in
    DESIGN.md's Shadow Evidence table (`card-elevation`: `0px 10px 20px 0px rgba(37, 82, 95, 0.08)`), just
    never transcribed to tokens.css. Added `--shadow-card` to `app/src/design/tokens.css` +
    `tokens.expected.ts` (the token-guard test asserts both stay in sync) and applied
    `box-shadow: var(--shadow-card)` to `.signin-card-panel`. `.signin-role-card` items correctly keep no
    shadow (nested cards get no shadow per A4: "no nested shadows").
  - **Design advisory applied (cheap):** `.signin-role-card:disabled` used `opacity: 0.6`; A4's button spec
    is "disabled = 40% opacity" — changed to `0.4`.
  - **Code advisory recorded, not fixed (A2):** `LocalDevProvider.EnsureExplicitGrantAsync`'s
    check-then-insert has no unique index on `(PersonId, Role)`, so concurrent sign-ins as the same
    not-yet-granted fixture could race and insert two grant rows. Harmless for the dev provider (idempotent
    query re-reads on next sign-in; at worst a duplicate audit-adjacent row in a synthetic local build) —
    noted for whoever builds the real role-grant admin endpoint (HAP-3/7/12/18 list) to add the index then.
  - **Code advisory recorded, not fixed (A3) — LOAD-BEARING HANDOFF CONSTRAINT FOR HAP-5:** BU-scoped
    (`BuDelegate`) `RoleGrant` rows flatten to a bare `ClaimTypes.Role` string in the cookie
    (`LocalDevProvider.SignInAsync`) — the grant's `BusinessUnitId` scope is NOT carried in the claim, and
    the cookie persists up to 8h (sliding). This is acceptable per research D3 (the principal only needs to
    prove WHICH roles, not their scope, at the identity layer), but it means **the future Authorization seam
    (HAP-5, or whichever story builds `backend/src/Hap.Api/Authorization/**`) MUST re-read `RoleGrant` rows
    from the database for any BU-scoped decision — it must NEVER trust the cookie's role claim alone to infer
    which BU a `BuDelegate` grant covers.** A grant revoked or re-scoped mid-session would otherwise still
    show as an unscoped "BuDelegate" claim until the cookie expires.
  - **HELD, not fixed yet (folded into the red-team package per lead's instruction):**
    `HierarchyRoleResolver.cs` classifies a depth-1/2/3 person as Portfolio/Group/BU Leader even with ZERO
    active reports (only the depth check gates the tier; `IsManager` is computed and exposed separately but
    not used to gate the tier fields). Do not fix until the red-team's resolver findings land, so the fix is
    made once, coherently, alongside whatever else red-team surfaces.

- **L3 panel round 1 — `hap-red-team` VERDICT (2026-07-21): VIOLATION FOUND, sign-off WITHHELD.** Proved a
  concrete privilege-escalation path, every link green on the pre-fix tip: an authenticated Individual calls
  `GET /auth/signin` (anonymous, leaks every seeded `external_ref`) → `POST /api/admin/overrides` (gated only
  by "any authenticated session" — 401, no PlatformAdmin check) to reparent themselves under `HAP-EXEC` →
  `HierarchyRoleResolver` then labels them "Portfolio Leader" (depth 1). Latent today (no score/aggregate
  tables exist yet to read with the elevated label) but confirms the resolver is unsafe for a future story to
  build visibility scope on, and confirms the L3 reclassification was the right call. Session-lead ruling:
  two more BLOCKING items (5, 6 below) on top of the code+design notes above; full L3 panel re-reviews once
  all land and the red-team's own escalation test goes green.

- **BLOCKING 5 (session-lead ruling — closed in this story, NOT deferred to HAP-5):** the mutating admin
  endpoints (`POST /api/admin/sync`, `POST /api/admin/overrides`, `GET /api/admin/overrides`) now require the
  `PlatformAdmin` policy (403 for any authenticated non-admin), in addition to the existing "any authenticated
  session" gate (401 for anonymous). `PlatformAdmin` was already an explicit `RoleGrant` claim in the
  principal (`LocalDevProvider`), so `RequireAuthorization("PlatformAdmin")` on `AdminEndpoints`'s group
  (`backend/src/Hap.Api/AdminEndpoints.cs`) plus a matching policy registration
  (`IdentityServiceCollectionExtensions.cs`) closes it without new infrastructure. Added
  `Hap.Api.Tests/Identity/RedTeamEscalationTests.cs`:
  `Individual_cannot_self_escalate_to_Portfolio_Leader_via_the_override_endpoint` (asserts 403 AND "no
  Portfolio Leader after" — the red-team's exact repro) plus a companion positive case proving a real
  PlatformAdmin is not blocked by the same gate. This discharges HAP-4's own `[PA]` auth-gating acceptance
  criterion in full (401 done at round 1, 403 done here) rather than leaving a live escalation on `main`.
  **Ripple:** three existing HAP-3/QA tests that signed in as the plain-Manager fixture "LEAD" before hitting
  `/api/admin/overrides` now needed LEAD to carry the "Platform Admin" seed-role label instead (so the
  dev-seed bootstrap grants it) — `OrgOverrideAuditTests.SeedTwoBuSnapshotAsync` and
  `QaAdversarialTests.SeedSnapshotAsync`, one line each, no assertions changed.
  **HAP-5 AC note update:** the admin-endpoint `[PA]` gate is now fully closed here; HAP-5 still owns the
  assessment-read 403/404 role-scope matrix (the actual visibility seam over `Assessments`/`AssessmentScores`),
  which is untouched by this story.

- **BLOCKING 6 (session-lead ruling — partial fix + hard documentation, per the split above):** applied the
  code reviewer's advisory (A-code-1): `HierarchyRoleResolver` now gates the Portfolio/Group/BU Leader tier
  fields on `IsManager` as well as depth — a depth-1/2/3 person with zero active reports (a vacated
  leadership slot) no longer gets a leadership label. New fixture + test:
  `HierarchyRoleResolverTests.Depth_three_with_zero_active_reports_gets_no_leadership_label` (`BULEAD2`, depth
  3, zero reports). The deeper misassignment red-team demonstrated — an interim/dual-hat layer
  (`Exec→PortfolioLeader→GroupLeader→InterimCover→RealBuLead`) mislabels the interim cover "BU Lead" and
  demotes the real BU Lead to "Manager" only — is **not fixed here**, per the lead's explicit instruction: it
  needs a structural "leads this unit" anchor (owner decision + a reviewed migration), not available in this
  story. Reinforced in QUESTIONS.md Q-014 (dated 2026-07-21 red-team update) as a hard G1 precondition: HAP-5
  must not consume these labels for visibility scope until Q-014 is owner-ratified or independently cleared.

- **G1 cosmetic note (red-team, no code change):** the dev-seed `RoleGrant` audit row
  (`LocalDevProvider.EnsureExplicitGrantAsync`) records `actorPersonId = subject.Id` — a self-grant, since
  there is no real "granting admin" identity in the local dev bootstrap path. Acceptable for a documented,
  synthetic-only dev bootstrap (QUESTIONS.md Q-013); flagged here so it is not mistaken for a genuine
  self-service privilege grant when this audit trail is reviewed at G1.
- **REBASE ONTO MAIN (2026-07-21, HAP-6 merged first):** rebased this branch onto `main` after HAP-6's
  framework engine merged (`git rebase main`). Resolved: `Program.cs`/`FrameworkEndpoints.cs` (HAP-6's
  `MapFrameworkEndpoints()` re-homed under the authorized `/api` group — `GET /frameworks/current` at the
  blanket authenticated level, `/admin/frameworks` under the `PlatformAdmin` policy, same treatment as
  `AdminEndpoints`); `TestSupport.cs` (HAP-6's framework-table TRUNCATE additions auto-merged cleanly
  alongside this story's `ISeedUserSource` additions — no conflict); and `QUESTIONS.md`'s numbering
  collision — HAP-6 landed its own Q-011 (framework auto-activation) and Q-012 (two-Active-versions
  invariant) first, so this story's Q-011 (RoleGrant bootstrap) and Q-012 (hierarchy-tier derivation)
  are renumbered **Q-013 and Q-014** respectively, throughout this story file, QUESTIONS.md, and every
  code comment that cited them (grep-verified after rebase). HAP-6's own story file
  (`docs/backlog/HAP-6-framework-engine.md`) independently documented the same reconciliation in advance
  ("HAP-6 keeps Q-011/Q-012, HAP-4 renumbered to Q-013/Q-014") — confirms the direction chosen here.
- **FRAMEWORK-ENDPOINT AUTH PLACEMENT (2026-07-21, folds into BLOCKING 5):** as the merging-second
  branch, extended the PlatformAdmin gate to a third admin surface. `FrameworkEndpoints.MapFrameworkEndpoints`
  signature changed from `this WebApplication app` to `this RouteGroupBuilder api` (mirroring
  `AdminEndpoints`): `GET /frameworks/current` maps on `api` directly (inherits the blanket "any
  authenticated session" requirement — it drives the assessment UI for every role); `/admin/frameworks`
  maps as `api.MapGroup("/admin/frameworks").RequireAuthorization("PlatformAdmin")` — same policy, same
  treatment as `AdminEndpoints`'s group. Route pattern note: the admin sub-group's `MapGet("", ...)`/
  `MapPost("", ...)` resolve to a **trailing-slash** path, `/api/admin/frameworks/`, not
  `/api/admin/frameworks` — verified against the live `EndpointDataSource` before hard-coding the
  assertion (see `UnauthenticatedSweepTests.cs`'s comment).
  - `UnauthenticatedSweepTests` extended: sanity assertions now also require the frameworks routes to be
    present in the enumerated route table before the sweep runs (still auto-covers them regardless, by
    path prefix, not by which group mapped them).
  - `RedTeamEscalationTests` gained two new cases per the lead's explicit instruction:
    `Non_admin_gets_403_on_every_platform_admin_surface_including_frameworks` (all three admin surfaces
    — sync, overrides, frameworks — return 403 for an authenticated Individual) and
    `Non_admin_can_read_frameworks_current_the_blanket_authenticated_level` (the same Individual reaches
    `GET /api/frameworks/current` past the auth gate — 404 because no framework is seeded in that test's
    minimal setup, never 401/403 — proving the blanket-vs-admin split is correctly wired).
  - **HAP-6's own test suite also needed the same auth retrofit** its earlier tests were written before
    this story's auth gate existed, exactly like HAP-3's tests did in round 1: `FrameworkEndpointsTests`
    (all 7 tests) and `FrameworkQaAdversarialTests` (the HTTP-calling tests; the two that exercise
    `FrameworkSeeder` directly via DI were untouched) now bootstrap a synced Platform-Admin-labelled
    person and sign in before calling `/api/admin/frameworks` or `/api/frameworks/current` — no
    assertion about framework-content behaviour itself changed.
- **Verify green against the fully integrated tip:** `dotnet build` clean; full `scripts/verify.sh`:
  both EF migrations (#1 HAP-3 org/audit, #2 HAP-6 framework engine) applied cleanly then idempotently
  no-op on the second run; 153 backend tests green (Domain 21, Architecture 6, Synth 41, Api 85 — up
  from HAP-4's own 70 plus HAP-6's suite, all now auth-aware); `Category=PrivacyReporting` suite green
  (11 tests); frontend 74 tests green; lint/typecheck/build all clean. Tail: `verify.sh: ALL GREEN`,
  exit code 0.

- **L3 panel round 2 — ALL FOUR SIGN-OFFS, ZERO BLOCKING (2026-07-21, at tip `0a4d9b4`):**
  - **`hap-red-team`: SIGN-OFF.** Round-1 escalation CLOSED — verified all three admin surfaces
    (`/api/admin/sync`, `/api/admin/overrides`, `/api/admin/frameworks`) are gated by the `PlatformAdmin`
    policy against the full live endpoint inventory (not a hand-picked subset); confirmed the
    trailing-slash `/api/admin/frameworks/` route is genuinely covered, not accidentally exempted by a
    string-mismatch; confirmed the `IsManager` gate closes the zero-active-reports leadership
    misassignment class; confirmed a deactivated root fails closed (returns `HierarchyRoles.None`, not a
    stale/cached tier). The residual interim-layer misassignment (Q-014) is correctly left unfixed and
    strongly documented as a G1 precondition rather than silently accepted.
  - **`hap-code-reviewer`: SIGN-OFF.** Independently re-derived L3 (concurring with the domain
    specialist's original finding); confirmed `verify.sh` green first-hand against tip `0a4d9b4`; verified
    all three round-1 blockers (stray `external_ref` claim, `ContractShapeTests` exactness, missing verify
    evidence) are genuinely fixed, not just asserted fixed.
  - **`hap-domain-specialist`: SIGN-OFF.**
  - **`hap-design-reviewer`: SIGN-OFF.** `--shadow-card` value confirmed exact against DESIGN.md's Shadow
    Evidence table.

  Zero blocking notes from any of the four. Clear to close Dev and hand off to QA.

- **Dev clock-out (2026-07-21):** measured wall-clock (frontmatter `worklog`, from `.wallclock-HAP-4-dev`)
  is 92 minutes against a `dev: M` (1-day human-equivalent) estimate — well under, not over, so no
  >4× overrun note applies. Logged exactly as measured, per CLAUDE.md §12: never back-filled, never
  shaved, never "felt like." `status: qa`.

- **QA (2026-07-21) — fresh instance, no shared context with Dev, adversarial per CLAUDE.md §9 /
  constitution Art. III.3 (L3, per this story's own reclassification note above).** Re-derived
  correctness from the story's acceptance criteria, `contracts/api.md`, `data-model.md`, and the
  code as it stands at tip `edb226f` — not from Dev's prose claims. Read every production file under
  `backend/src/Hap.Api/Identity/**`, `AdminEndpoints.cs`, `FrameworkEndpoints.cs`, `Program.cs`,
  `IdentityServiceCollectionExtensions.cs`, the sign-in screen + its CSS/strings, and every existing
  test in `Hap.Api.Tests/Identity/**` before writing a single new assertion.

  **Per-clause acceptance-criteria verdict (literal, one check per clause):**
  1. `GET /auth/signin` lists seeded users from seed data (traced `ChallengeAsync` →
     `ISeedUserSource.GetUsersAsync` → `FileSeedUserSource` reads `seed-users.json`; no hard-coded
     list anywhere in `Hap.Api`) — **PASS**. `POST /auth/signin {userKey}` sets the cookie
     (`SignInFlowTests.Post_auth_signin_sets_a_cookie_that_authenticates_subsequent_requests`,
     re-run green) — **PASS**. `POST /auth/signout` clears it
     (`SignInFlowTests.Post_auth_signout_clears_the_session` + my own
     `Signed_out_cookie_cannot_be_replayed_against_a_gated_admin_endpoint`, both green) — **PASS**.
  2. Principal carries `person_id` + explicit roles only (`LocalDevProvider.SignInAsync` builds
     exactly `[person_id, Role*]`; `ContractShapeTests.AssertExactClaimSet` asserts the WHOLE claim
     set, not a subset) — **PASS**. Hierarchy roles computed per request, not stored/cached
     (`HierarchyRoleResolver.ResolveAsync` re-queries `Person` fresh every call; no caching layer
     anywhere in the DI graph — confirmed by reading `IdentityServiceCollectionExtensions.cs`'s
     `AddScoped` registrations) — **PASS**. Integration test proving promotion without re-sign-in
     (`SignInFlowTests.Promoting_a_persons_manager_link_changes_their_computed_role_without_re_sign_in`,
     re-run green) — **PASS**.
  3. Contract test asserts principal shape matches `contracts/api.md` (person matched by
     `external_ref`; seam consumes nothing provider-specific) — `ContractShapeTests` (both cases) +
     `Port_accepts_a_second_independent_adapter_implementation` (compile-time proof a second,
     wholly independent `IIdentityProvider` satisfies the interface untouched), re-run green —
     **PASS**.
  4. `GET /api/me` returns profile + computed roles for each of the seven seeded users —
     `SignInFlowTests.Get_me_returns_profile_and_computed_roles_for_every_seeded_role` is a
     `[Theory]` with one case per role (Individual/Manager/BuLead/GroupLeader/PortfolioLeader/
     HigExecutive/PlatformAdmin), re-run green, all 7 present — **PASS**.
  5. Unauthenticated `/api/*` → 401, explicitly including the HAP-3 admin endpoints — verified
     TWO independent ways: Dev's `UnauthenticatedSweepTests` (prefix-filtered to `/api`, enumerates
     `EndpointDataSource` live rather than a hand list) re-run green, AND my own
     `Every_mapped_endpoint_anywhere_in_the_app_is_either_api_gated_auth_anonymous_or_healthz`
     (deliberately does NOT filter to `/api` first — walks the ENTIRE route table and classifies
     every route by hand, so an admin route mapped outside `/api` would not hide from a prefix
     filter the way it would from Dev's sweep). Both green, zero routes found outside the
     known-anonymous allowlist (`/healthz`, `/auth/signin`, `/auth/signout`) or the authorized
     `/api` group — **PASS**.
  6. Sign-in screen: tokens.css only — I independently grepped every `var(--...)` used in
     `SignInScreen.css` against `tokens.css` and confirmed all 28 custom properties exist there
     (none introduced ad hoc). Strings externalised — every visible string in `SignInScreen.tsx`
     reads from `strings/en.ts`'s `signIn` block; `strings-guard.test.tsx` (frontend suite) passed.
     Axe scan — `signin.test.tsx`'s `has no detectable accessibility violations` case, re-run green
     as part of the full `npm run test` (74/74 frontend tests green) — **PASS**.
  7. `./scripts/verify.sh` green — full end-to-end run (fresh disposable Postgres on port 26828,
     both migrations applied then idempotently no-op on the second run, 166 backend tests total
     [Domain 21 + Architecture 6 + Synth 41 + Api 98 — the +13 over Dev's reported 85 is my own new
     `QaFreshAdversarialTests` class], `Category=PrivacyReporting` suite 22/22 [9 pre-existing +
     13 mine, confirmed by independently counting `[Trait("Category","PrivacyReporting")]`
     occurrences before the run so nothing could silently drop], frontend 74/74, lint/typecheck/build
     clean, no external font request). Tail: `verify.sh: ALL GREEN`, exit 0 — **PASS**.

  **Mandatory adversarial attempts (CLAUDE.md §9.3/9.4) — every attempt documented with outcome:**

  - **§9.3 scope note:** no `Assessments`/`AssessmentScores`/rollup tables exist yet (HAP-8) —
    "read a score outside the chain", "obtain a <4 aggregate", and "desync a rollup" are OUT OF
    REACH BY CONSTRUCTION for this story. Stated explicitly, not silently skipped.

  - **(a) Fresh privilege-escalation re-run, independent of `RedTeamEscalationTests`.** New test file
    `Hap.Api.Tests/Identity/QaFreshAdversarialTests.cs` (13 tests, all green, tagged
    `Category=PrivacyReporting`):
    - `Plain_manager_cannot_reach_directory_sync` — a plain **Manager** (not just Individual) POSTs
      `/api/admin/sync` → 403. **Attempted, blocked.**
    - `BU_lead_cannot_self_grant_via_the_override_endpoint` — a **BU Lead** (already holds "Manager"
      per the resolver) attempts the exact self-reparent-under-exec escalation the red-team proved
      round 1, now as a higher-privilege attacker than Individual → 403, zero `OrgOverride` rows
      created. **Attempted, blocked.**
    - `Route_variants_around_the_admin_surfaces_never_leak_data_to_a_non_admin` — trailing-slash,
      no-trailing-slash, and GET-vs-POST variants of `/api/admin/frameworks` and
      `/api/admin/overrides` as an Individual → every variant either 403 (gated) or 404 (genuinely
      unmapped — confirmed by a companion route-table assertion that the no-slash form is not a
      distinct anonymous endpoint), never 200/201. **Attempted, blocked.**
  - **(b) Independent full-endpoint inventory**, not reusing Dev's filter logic — see clause 5 above.
    **No ungated admin path found.**
  - **(c) Attempt to mint a leadership label without qualifying:**
    `With_no_valid_root_anywhere_in_the_graph_nobody_gets_a_leadership_label` — a person with zero
    manager AND zero reports (so no valid root exists to walk to) → `HierarchyRoles.None`, confirming
    `ComputeDepthFromRoot`'s fail-closed `null` path holds even in a graph shape Dev's own fixtures
    didn't try. **Attempted, defeated.** Separately, `Interim_layer_misassignment_is_confirmed_but_...`
    reproduces the KNOWN Q-014 residual (interim/dual-hat depth misassignment) independently — I did
    NOT trust the story notes' description, I rebuilt the org shape myself and confirmed INTERIM
    wrongly reads "BU Lead" while REALBULEAD is demoted to "Manager" only — then proved, via a live
    `GET /api/me` call as INTERIM, that the mislabel is confined to INTERIM's own self-scope response
    and touches no other person's data or endpoint (no visibility-scoped consumer exists in this
    story). Confirms the session-lead's framing was accurate, doesn't just take it on faith. Per the
    ruling above, this residual remains correctly UNFIXED here (needs Q-014's structural anchor) and
    is already reinforced in QUESTIONS.md as a G1 precondition — QA found no NEW instance of it
    leaking, only confirmed the known, already-flagged one is confined as claimed.
  - **(d) Session/cookie attacks:**
    - `A_tampered_cookie_value_is_treated_as_unauthenticated...` — a syntactically-plausible but
      cryptographically-invalid `hap_auth` cookie value sent standalone → 401, never treated as a
      valid downgraded session. **Attempted, blocked.**
    - `Signed_out_cookie_cannot_be_replayed_against_a_gated_admin_endpoint` — same `HttpClient`
      (same cookie jar) replays a request after `/auth/signout` → 401. **Attempted, blocked.**
    - `Revoking_a_PlatformAdmin_grant_mid_session_does_not_immediately_revoke_the_live_cookie` —
      **FINDING, sharper than advisory A3 anticipated.** Confirmed empirically (not just trusted
      from prose) that (i) deleting a signed-in admin's `RoleGrant` row directly in the DB leaves
      their live cookie still authorizing past the `PlatformAdmin` policy (matches advisory A3 —
      claims are baked into the cookie ticket, not re-read from `RoleGrant` per request), AND
      (ii) — the sharper part — a full sign-out + sign-in cycle does **not** clear it either, for
      HAP-ADMIN/HAP-EXEC specifically: `LocalDevProvider.EnsureExplicitGrantAsync` idempotently
      RE-DERIVES and RE-INSERTS the grant from the seed-users.json role label on every sign-in, so
      "revoking" one of these two bootstrap fixtures' grants via direct DB edit is silently undone
      the next time they sign in. **This is NOT a cross-identity escalation** — HAP-ADMIN only ever
      re-acquires its OWN designated role, nothing leaks to any other fixture — and is consistent
      with Q-013's stated synthetic-local-only bootstrap design (there is no revoke endpoint in this
      story's scope to begin with). Not a HAP-4 blocking defect. Flagged forward: whichever future
      story builds a real role-grant admin/revoke endpoint (Q-013's "later story" list) must either
      special-case HAP-ADMIN/HAP-EXEC or accept that their PlatformAdmin/HigExecutive grants are
      effectively un-revocable via that endpoint alone while the dev-seed bootstrap runs unchanged
      alongside it — added as a QUESTIONS.md Q-013 addendum below.

  **Negative-path tests added (QA work, honestly attributed):** all 13 in
  `Hap.Api.Tests/Identity/QaFreshAdversarialTests.cs`, tagged `[Trait("Category","PrivacyReporting")]`
  so they run on every `verify.sh`: `Plain_manager_cannot_reach_directory_sync`,
  `BU_lead_cannot_self_grant_via_the_override_endpoint`,
  `Route_variants_around_the_admin_surfaces_never_leak_data_to_a_non_admin` (4 cases),
  `Get_variant_of_the_no_slash_admin_frameworks_path_is_unmapped_not_silently_authorized`,
  `Every_mapped_endpoint_anywhere_in_the_app_is_either_api_gated_auth_anonymous_or_healthz`,
  `With_no_valid_root_anywhere_in_the_graph_nobody_gets_a_leadership_label`,
  `A_tampered_cookie_value_is_treated_as_unauthenticated_not_as_a_valid_downgraded_session`,
  `Signed_out_cookie_cannot_be_replayed_against_a_gated_admin_endpoint`,
  `Revoking_a_PlatformAdmin_grant_mid_session_does_not_immediately_revoke_the_live_cookie`,
  `Interim_layer_misassignment_is_confirmed_but_stays_confined_to_the_misclassified_persons_own_self_scope`.

  **`verify.sh` evidence (QA re-run, independent of Dev's):** fresh disposable Postgres (port 26828),
  both migrations idempotent, 166 backend tests green (up from Dev's 153 by exactly my +13),
  `Category=PrivacyReporting` 22/22 green, frontend 74/74 green, lint/typecheck/build clean, no
  external font request. Tail: `verify.sh: ALL GREEN`, exit 0.

  **QA OUTCOME: PASS.** Every acceptance-criterion clause verified literally against the running
  system, not trusted from Dev's notes. Every mandatory adversarial attempt was made and every one
  was blocked; no violation path found; the one finding (self-healing dev-seed grant on
  re-sign-in) is a documented, non-blocking, forward-flagged operational note, not a defect in this
  story's own scope. `hap-red-team`'s round-1 finding is independently confirmed closed by a
  different attacker profile (BU Lead, not just Individual) and by route variants the original
  finding didn't need to consider. The Q-014 residual is independently reproduced and independently
  confirmed confined to self-scope, matching the session-lead's framing rather than assuming it.
  Recommend: proceed to Phase 4 closure; carry the Q-013 addendum finding into the future role-grant
  admin endpoint story's design.

- **QA clock-out (2026-07-21):** measured wall-clock (from `.wallclock-HAP-4-qa`,
  `2026-07-21T19:13:25Z` → `2026-07-21T19:25:15Z`) is 11 minutes against a `qa: S` estimate — well
  under, not over. Logged exactly as measured, never shaved, never "felt like." Status remains
  `qa` (closure is the session lead's, per CLAUDE.md §9 — QA does not self-merge).
