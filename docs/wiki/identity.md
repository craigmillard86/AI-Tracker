# Identity, sessions & role derivation — as built

_Subsystem shipped by HAP-4 (FR-055, FR-056). Describes shipped behaviour only; WHAT/WHY live in `docs/spec/` + `specs/`, status in `docs/backlog/`, decisions in `docs/decisions/`._

## What exists

Authentication through a **port** (`IIdentityProvider`), so the app authenticates the same way a future Entra adapter would. This build ships the local dev provider only; the Entra adapter is a deferred, decision-recorded story. A contract test proves the seam compiles against a second independent adapter, carrying nothing provider-specific.

### The port and dev provider (`Hap.Api/Identity`)

- `IIdentityProvider`: `ChallengeAsync` / `SignInAsync(userKey)` / `SignOutAsync`.
- `LocalDevProvider` looks up a seed user by `external_ref` (from `seed-users.json` via `ISeedUserSource`/`FileSeedUserSource` — never hardcoded), resolves the matching `Person`, and builds the claims principal. Rejections: `PersonNotSyncedException` (409), `InactiveUserException` / `UnknownSeedUserException` (400).
- `GET /auth/signin` lists the seeded users (role picker); `POST /auth/signin {userKey}` sets the auth cookie; `POST /auth/signout` clears it.

### The claims principal — explicit only

The principal carries **exactly** `person_id` plus one role claim per **explicit `RoleGrant` row** — nothing hierarchy-derived is ever stored in the principal or the cookie (asserted by an exact-claim-set contract test). Platform Admin and HIG Executive grants are bootstrapped idempotently at sign-in from the seed user's role label, each written with its audit row.

### Computed hierarchy roles (`HierarchyRoleResolver`)

Manager / BU Lead / Group Leader / Portfolio Leader are **computed per request** from live org data (never stored), so promoting a person's manager link changes their role without re-sign-in. Derivation: manager-chain **depth from a validated root** (root = active person, no manager, ≥1 active report), gated on `IsManager` (a depth-1/2/3 person with zero active reports gets no leadership label). `HierarchyRoles` carries the actual `BusinessUnitId`/`GroupId`/`PortfolioId` the person leads, ready for the visibility seam.

**Known limitation (Q-014, owner-ratification gate):** depth-from-root assumes a uniform-depth org tree — correct for the synthetic generator, but an interim/dual-hat manager layer or a missing tier misassigns. **No consumer may build visibility scope on these computed tiers until Q-014 is owner-ratified with a structural anchor.** HAP-4 itself never does — `GET /api/me` is self-scope only.

### Sessions

ASP.NET cookie auth: cookie `hap_auth`, HttpOnly, SameSite=Lax, 8h sliding. `OnRedirectToLogin`/`OnRedirectToAccessDenied` are overridden to return 401/403 directly (this is an API, not a browser-redirect app). The frontend talks same-origin via a Vite dev-server proxy, so the Lax cookie never crosses an origin. No session fixation (a fresh ticket per sign-in, no pre-auth session).

## Authorization boundary

Every `/api/**` route is under `app.MapGroup("/api").RequireAuthorization()` — unauthenticated requests get 401 (enforced by a route-table sweep, not a hand-list). On top of that, a **PlatformAdmin (403) policy** gates all admin surfaces: `/api/admin/sync`, `/api/admin/overrides`, and `/api/admin/frameworks`. `GET /api/frameworks/current` and `GET /api/me` are blanket-authenticated (any role); `/auth/*` and `/healthz` are anonymous.

This closes the HAP-3 wave-0 deferral for the admin endpoints (they shipped functional but un-gated). It also closed a red-team-found escalation: before the gate, an authenticated non-admin could self-reparent via the override endpoint and mint a leadership label — now a passing regression test (`RedTeamEscalationTests`).

**Caveat for the visibility seam:** BU-scoped (`BuDelegate`) grants flatten to a bare role string in the cookie — the `BusinessUnitId` scope is not carried, and claims persist up to the 8h window. Any BU-scoped authorization decision must re-read `RoleGrant` rows from the DB, never trust the cookie claim alone.
