---
id: HAP-1
title: Scaffold both stacks, docker-compose runtime, and verify.sh gate of record
epic: E1-foundations
wave: 0
fr: [FR-057, FR-067]   # FR-057: infra portion only (mailpit sink + compose runtime); FR-058/FR-059 are deployment-scope, intentionally not cited — see notes
risk: L2                # trigger: scripts/verify.sh itself + every initial dependency (NuGet & npm)
status: qa
estimate: {dev: L, qa: M}
worklog:
  - {phase: dev, start: 2026-07-21T13:28:02Z, end: 2026-07-21T13:50:38Z, mins: 23}
  - {phase: qa, start: 2026-07-21T14:04:08Z, end: 2026-07-21T14:15:24Z, mins: 11}
closure: null
---
## Story
As the platform team, we need the full local skeleton — .NET solution, React app, docker-compose runtime, and the verify.sh gate — so every subsequent story has a green, reviewable baseline to build on.

## Context
- Spec: "Functional Requirements — Infrastructure & Notifications" (FR-057 infra portion, FR-058, FR-059, FR-067); "Assumptions — Stack & Hosting".
- Plan: "Technical Context", "Project Structure — Source Code", research **D6/D7/D9/D10** (print-CSS, mailpit, frontend stack, test-db strategy).
- Design: `docs/design/DESIGN.md` (tokens; A1 type roles, A6 app frame) — this story creates `app/src/design/tokens.css` from it and the deep-navy shell + left nav only. Fonts self-hosted via `@fontsource/montserrat` + `@fontsource/inter-tight` (constitution: no runtime network).
- Files created: `backend/Hap.sln`; projects `backend/src/{Hap.Api,Hap.Domain,Hap.Infrastructure,Hap.Synth}` and `backend/tests/{Hap.Domain.Tests,Hap.Api.Tests,Hap.Architecture.Tests}` (all projects created HERE so later stories never touch the .sln); `app/` (Vite + React 18 + TS strict, ESLint, Prettier, Vitest + RTL + vitest-axe); `docker-compose.yml` (api, app, postgres, mailpit); `scripts/verify.sh`.
- Blocked by: — (first story)
- Parallelisable: no (everything depends on this)

## Acceptance criteria
- [ ] `docker compose up -d --build` from a clean clone starts api (8080), app (5173), postgres, mailpit (8025) with no external credentials; `curl localhost:8080/healthz` returns 200.
- [ ] `./scripts/verify.sh` runs green end-to-end: builds both stacks **warnings-as-errors**, starts a disposable Postgres on a random port, applies EF migrations idempotently (running twice = no-op), runs dotnet tests (incl. `--filter Category=PrivacyReporting`, currently zero tests but the filter step exists and is always-on), vitest, eslint, `tsc --noEmit`, then tears the container down. Non-zero exit on any failure.
- [ ] Solution contains all 7 projects listed in Context; `dotnet build` clean with `TreatWarningsAsErrors=true` in a shared `Directory.Build.props`.
- [ ] App shell renders: deep-navy `#002c36` top bar + left nav (DESIGN.md A6), light content surface, no Google Fonts request (assert no external URL in built output: `grep -r "fonts.googleapis" app/dist` finds nothing after `npm run build`).
- [ ] `app/src/design/tokens.css` defines custom properties for every A2 colour, A1 type role, spacing and radius token; a Vitest test asserts token values equal DESIGN.md's documented values (research D9).
- [ ] All UI strings in the shell come from an externalised strings module, not literals in components (FR-067); ESLint rule or test guards it.
- [ ] README section "Run locally" matches quickstart.md commands verbatim.

## Attempts / notes

### Attempt 1 (dev) — 2026-07-21, branch `HAP-1-fr-057-scaffold-and-gate`

**Risk L2 confirmed** — trigger: `scripts/verify.sh` is created here, and this story
introduces every initial NuGet and npm dependency (§7 "any new dependency").

**Approach.** Authored the scaffold directly rather than fanning out to sub-builders:
every piece is coupled through `verify.sh`, and `tokens.css` must match DESIGN.md
value-for-value against a guard test — a single coherent baseline was the lower-risk
path and let me iterate the whole tree against Docker and the gate in one loop.

**Verify-green evidence.** `bash scripts/verify.sh` → **exit 0**, "verify.sh: ALL GREEN".
Steps executed: (1) backend build Release warnings-as-errors, 0 warnings/0 errors;
(2) disposable Postgres on random port 32938; (3) `dotnet tool restore` (dotnet-ef 8.0.8);
(4) `dotnet ef database update` run twice — both "No migrations were applied. The database
is already up to date." (idempotent no-op, zero migrations by design); (5) backend tests
3/3 passed; (6) `--filter Category=PrivacyReporting` runs and exits 0 with zero matching
tests; (7) frontend `npm ci` + lint (0) + typecheck (0) + vitest 65/65 passed;
(8) `vite build` OK; (9) font-guard grep of `dist` for `fonts.googleapis` → none.

**Acceptance-criterion 1 (compose) validated out-of-band.** `docker compose up -d --build`
brought up api/app/postgres/mailpit with no credentials; `curl localhost:8080/healthz` →
`200 {"status":"ok"}`; app 5173 → 200; mailpit 8025 → 200; postgres healthy. Torn down
with `docker compose down -v`.

**Dependencies added (justification, per L2 rule).**
- Backend (NuGet): `Microsoft.EntityFrameworkCore` 8.0.8, `Npgsql.EntityFrameworkCore.PostgreSQL`
  8.0.4, `Microsoft.EntityFrameworkCore.Design` 8.0.8 (migration seam); `Microsoft.NET.Test.Sdk`
  17.10.0, `xunit` 2.8.1, `xunit.runner.visualstudio` 2.8.2 (test runner);
  `Microsoft.AspNetCore.Mvc.Testing` 8.0.8 (healthz integration test). Local tool: `dotnet-ef` 8.0.8.
- Frontend (npm): runtime — `react`/`react-dom` 18.3.1, `@fontsource/montserrat` 5.0.19,
  `@fontsource/inter-tight` 5.0.20 (self-hosted fonts, no runtime network). Dev — `vite` 5.3.3,
  `@vitejs/plugin-react` 4.3.1, `typescript` 5.4.5, `vitest` 1.6.0, `@testing-library/react` 16.0.0,
  `@testing-library/jest-dom` 6.4.6, `vitest-axe` 0.1.0, `jsdom` 24.1.0, `eslint` 8.57.0 + react/
  ts plugins, `prettier` 3.3.2, `@types/*`. All pinned exact.

**Known advisory (flag for panel).** `npm audit` reports 3 dev-only advisories — esbuild/vite
dev-server SSRF (moderate) and vitest API/UI-server RCE (critical). All require the dev server
to be reachable by a hostile web origin; they do not affect production build output and are not
reachable from the gate (no dev server runs in CI, no network beyond restore). Clearing the
vitest one needs a breaking v4 upgrade. Left pinned to keep the green baseline; recommend the
panel decide whether to schedule the upgrade. No provisional spec assumptions were needed;
no QUESTIONS.md entries.

**Decisions of note.**
- EF migration step targets `Hap.Infrastructure` as its own startup project via a
  `DesignTimeDbContextFactory` reading `HAP_DB_CONNECTION`, so `dotnet ef` never boots the web host.
- Externalised-strings guard (FR-067) is a render-based Vitest test: it walks every text node the
  shell renders and asserts each is a value in the strings module (belt to the eslint config's braces).
- Token guard imports `tokens.css?raw` and asserts every custom property against `tokens.expected.ts`
  (the transcribed DESIGN.md values), plus a no-undocumented-extras check. 62 token assertions.
- Added `.gitattributes` pinning `*.sh` to LF so `verify.sh` survives Windows checkouts.
- axe a11y check disables `color-contrast` (needs a real canvas, unavailable in jsdom); contrast is
  an app-screen concern for later UI stories.

### Panel round 1

- **B1 (2026-07-21):** `fr:` trimmed — FR-058/FR-059 removed as deployment-scope over-claims;
  FR-057 cited for the infra portion only (email sink + event plumbing arrives with HAP-18). The
  change-log row at closure must carry the same qualification.
- **A1 (advisory):** `--space-4xl`/`--space-5xl` kept (AC#5 requires every DESIGN.md spacing token)
  and annotated "marketing-scale — NOT for app screens" in both `tokens.css` and `tokens.expected.ts`.

### Panel sign-offs (L2 — zero blocking notes)

**code-reviewer (L2 panel), HAP-1 — APPROVED.** Class independently re-derived as L2 (verify.sh + initial dependencies; no L3 path touched). verify.sh audited for correctness: fail-fast under `set -euo pipefail`, always-on PrivacyReporting step, genuinely idempotent zero-migration EF step, explicit font-guard exit. Warnings-as-errors applied to all 7 projects via Directory.Build.props. Guard tests (strings, tokens, layering) confirmed non-vacuous; onion layering verified clean by construction. No hard-coded framework/Harris content, no speculative abstraction, deps pinned and justified, commits/branch conventions correct, README↔quickstart verbatim. Zero blocking notes; five advisories logged for follow-up (chiefly: schedule the vitest/vite dev-dependency upgrade). Relied on the dev's detailed verify-green evidence plus static audit rather than a re-run. — reviewed on branch `HAP-1-fr-057-scaffold-and-gate`.

**Domain specialist (L2): APPROVED (round 2).** Design tokens verified value-for-value against DESIGN.md (base palette, A2 RAG/maturity-ramp/feedback, A1 app type roles, A6 spacing, radii); guard is non-circular (asserts tokens.css against independently transcribed tokens.expected.ts). Shell A6/A7-compliant (deep-navy #002c36 top bar + left nav, light content surface, no hero type, no Roboto). No framework/Harris/hierarchy strings in C#/TS (Art. II.4). Local-only runtime confirmed (Art. X); fonts self-hosted via @fontsource. FR-067 externalised strings delivered with render-walking guard. FR citation corrected to [FR-057 infra-portion, FR-067]; FR-058/FR-059 dropped as deployment-scope over-claims (blocking B1, resolved in d8732f5). Advisory A1 accepted (annotated non-app); A2 (guard does not detect DESIGN.md drift) noted, non-blocking; A3 (bin/obj tracking) verified clear by the lead — `git ls-files` shows zero tracked bin/obj paths.

**Lead note for closure:** change-log fr_ids = `FR-057;FR-067` with the infra-portion qualification (per B1); follow-up candidate story: vitest/vite dev-dependency major upgrade (code-reviewer advisory 1).

### QA (Phase 3) — 2026-07-21, fresh agent, no shared context with dev

Adversarial pass re-derived from artifacts. **Verdict: PASS.** Every clause verified by
running the commands, not trusting dev evidence. Final `bash scripts/verify.sh` → **exit 0,
"verify.sh: ALL GREEN"** on a clean tree (4 vitest files / 66 tests, 3 backend test projects,
0 build warnings/errors).

**Scope (a)/(b)/(c) non-applicability.** This story creates no assessment data, scores, or
rollups — the `Assessments`/`AssessmentScores` tables and the visibility seam do not yet exist.
The QA definition's mandatory attempts — (a) read a score outside the management chain, (b) obtain
a sub-4 aggregate, (c) make a rollup/Harris figure disagree with underlying rows — have **no
surface to exercise** here and were **not fabricated**. They attach to the RBAC/assessment wave
(HAP-12, G1). Confirmed by inspection: no controllers beyond `/healthz`, no domain read paths.

**Per-acceptance-criterion verdicts (literal):**
1. **PASS** — `docker compose up -d --build` from the worktree brought up api/app/postgres/mailpit
   with no credentials. `curl localhost:8080/healthz` → `200 {"status":"ok"}`; app `localhost:5173`
   → `200` (Vite dev server, ready ~7s after container start); mailpit `localhost:8025` → `200`;
   postgres `(healthy)`. Torn down with `docker compose down -v` (volume removed). Note: a first
   probe read `5173=000` because it fired ~2s after container start before Vite had bound — a
   probe-timing artifact, not a defect; re-run polling the app port confirmed `200`.
2. **PASS** — full `verify.sh` runs green end-to-end. Observed all 9 steps: backend build Release
   `0 Warning(s)/0 Error(s)`; disposable Postgres on random port (20058 this run); `dotnet tool
   restore`; `ef database update` run twice, both "No migrations were applied. The database is
   already up to date." (idempotent no-op); backend tests 3/3; `--filter Category=PrivacyReporting`
   step executes and exits 0 with zero matches; frontend `npm ci`/lint/typecheck/vitest 66/66;
   `vite build`; font grep of dist. Non-zero exit proven (see negative-path NP1).
3. **PASS** — `backend/Hap.sln` references all 7 projects (Hap.Api, Hap.Domain, Hap.Infrastructure,
   Hap.Synth + Hap.Domain.Tests, Hap.Api.Tests, Hap.Architecture.Tests). `Directory.Build.props`
   sets `TreatWarningsAsErrors=true` for all; Release build clean.
4. **PASS** — `AppShell.tsx` renders deep-navy (`--color-deep-navy` #002c36) top bar + left nav
   (`AppShell.css` .app-topbar/.app-nav) over a pure-white content surface. `grep -r "fonts.googleapis"
   app/dist` → nothing; no external font host in dist assets (only W3C SVG/XML namespaces + a
   reactjs.org error-decoder string, neither a font request); fonts bundled locally as woff/woff2
   via `@fontsource`.
5. **PASS** — `tokens.css` defines every A2 colour (base palette, RAG + tints, L0–L3 maturity ramp,
   feedback states), every A1 app type role, the A6 spacing scale, and radius roles; the Vitest
   token guard asserts each against `tokens.expected.ts` (62 assertions) incl. a no-undocumented-extras
   check. Values spot-checked against DESIGN.md — match.
6. **PASS** — all shell copy comes from `src/strings/en.ts` via `strings`; the render-walking
   `strings-guard.test.tsx` asserts every visible shell text node is a value in the strings module.
7. **PASS** — README "Run locally" fenced command block is verbatim-identical to
   `specs/001-maturity-initiative-register/quickstart.md` "Run" block (steps 1–4).

**Negative-path testing (QA-window; each injected, observed, reverted — tree left clean):**
- **NP1 — gate fails on a build warning.** Injected an unused local (`CS0219`) into `Program.cs`;
  full `verify.sh` → **exit 1**, failing at step 1 (warnings-as-errors converts it to an error).
  Reverted.
- **NP2 — font guard fails on external font.** Injected a `fonts.googleapis` reference into
  `app/dist`; verify's step-9 grep detected it (would exit 1). Removed; dist clean.
- **NP3 — token guard fails on drift.** Mutated `--color-brand-teal` #008099→#123456; the token
  test failed with the exact expected/actual diff. Reverted.

**Permanent QA guard added** (`app/src/__tests__/no-external-fonts.test.ts`): source-level assertion
that no `src/**` file references an external font host (`fonts.googleapis`/`fonts.gstatic`/external
`@import url(http…)`), complementing verify.sh's post-build dist grep by catching the constitution
Art. X (no runtime network) violation at source. Proven non-vacuous: passes on the clean tree, fails
(naming the offending file) when a violation is injected into `App.tsx`; clean under lint + typecheck;
included in the final green run (66th test).

**QA outcome: PASS** — all 7 acceptance criteria met literally, gate green end-to-end, negative paths
confirm the gate's guarantees bite. No blocking findings. Status left at `qa`; closure (squash merge,
closure block, change-log row) is the session lead's.
