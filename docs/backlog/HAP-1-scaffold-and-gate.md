---
id: HAP-1
title: Scaffold both stacks, docker-compose runtime, and verify.sh gate of record
epic: E1-foundations
wave: 0
fr: [FR-057, FR-058, FR-059, FR-067]
risk: L2                # trigger: scripts/verify.sh itself + every initial dependency (NuGet & npm)
status: todo
estimate: {dev: L, qa: M}
worklog: []
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
