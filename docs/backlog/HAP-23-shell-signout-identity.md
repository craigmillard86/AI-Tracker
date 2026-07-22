---
id: HAP-23
title: Shell sign-out control + live signed-in identity
epic: E1-foundations
wave: 2
fr: [FR-055, FR-056]
risk: L1                # React shell UI; reads the caller's own identity; calls the existing signout endpoint — no auth-decision logic, no assessment data path
status: todo
estimate: {dev: S, qa: S}
worklog: []
closure: null
---
## Story
As any signed-in user I can sign out from the app shell and I can see who I am currently signed in as — so I can end my session and (in the local dev provider) switch roles without clearing browser cookies by hand.

## Context
- **Found in owner acceptance testing 2026-07-22:** the running app has no way to log out. The plumbing already exists — `signOut()` in `app/src/api/client.ts` (`POST /auth/signout`) and the endpoint `IdentityEndpoints.MapPost("/auth/signout", …)` — but nothing in the shell calls it, and the top bar's "Signed in as …" is a hard-coded `strings.shell.rolePlaceholder`, not the real identity.
- Identity port: HAP-4 (FR-055/FR-056) shipped `POST /auth/signin` (dev role-picker) + `POST /auth/signout` + `GET /api/me` (returns the caller's `person_id` + roles). There is no separate sign-out FR — sign-out is part of the identity-port surface.
- Design: DESIGN.md A6 (fixed deep-navy top bar); the control lives in the existing `.app-user` span in `app/src/shell/AppShell.tsx`. Build from A1–A6 tokens only; no new tokens (addendum-update-first if a new component is needed). No mockup exists for the shell chrome (Q-004: admin/shell surfaces ship minimal, DESIGN.md-conformant).
- After sign-out the app must return to the dev sign-in role-picker (the unauthenticated state HAP-4 already renders).

## Acceptance criteria
- [ ] The shell top bar shows the ACTUAL signed-in identity — display name + role(s) — read from `GET /api/me` (replacing the hard-coded `rolePlaceholder`). Falls back gracefully if `/api/me` is unavailable.
- [ ] A sign-out control (button, keyboard-focusable, visible focus outline) in the top bar calls `signOut()` → `POST /auth/signout`, then returns the app to the sign-in role-picker (no stale authenticated view, no cached identity left in memory).
- [ ] Sign-out clears the client session cleanly: a subsequent `GET /api/me` (or any `/api/**` call) is unauthenticated (401) until the user signs in again.
- [ ] DESIGN.md A6 conformance: tokens only (deep-navy top bar), the control styled per the addendum; strings externalised (`en.ts`); colour never the sole signal.
- [ ] vitest-axe passes on the shell with the new control; keyboard-only sign-out works.
- [ ] `./scripts/verify.sh` green.

## Attempts / notes
