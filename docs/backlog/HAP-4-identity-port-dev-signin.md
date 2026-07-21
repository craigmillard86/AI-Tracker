---
id: HAP-4
title: IIdentityProvider port, local dev provider, and role-picker sign-in
epic: E1-foundations
wave: 0
fr: [FR-055, FR-056]
risk: L2                # trigger: IIdentityProvider implementations & session handling
status: todo
estimate: {dev: M, qa: S}
worklog: []
closure: null
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
- [ ] Unauthenticated requests to any `/api/*` endpoint return 401 (test sweeps all mapped routes).
- [ ] Sign-in screen uses tokens.css only (no new colours/type sizes); strings externalised (FR-067); axe scan passes (vitest-axe).
- [ ] `./scripts/verify.sh` green.

## Attempts / notes
