# HIG AI Adoption Platform (HAP)

An internal web application measuring and driving AI adoption across HIG's business
units: monthly AI-DLC maturity assessments, a register of AI initiatives, and
pre-filled Harris AI Dashboard submissions generated from live data.

This is a **fully local build**: the repo is the entire operation. It runs
end-to-end with Docker and local toolchains — no cloud service, tracker, or
credentials. See `CLAUDE.md` (the agent contract) and
`.specify/memory/constitution.md` (the governing constitution).

## Stack

- **Backend:** .NET 8 Web API (`backend/`, solution `backend/Hap.sln`)
- **Frontend:** React 18 + TypeScript (strict), built with Vite (`app/`)
- **Database:** PostgreSQL
- **Local runtime:** docker-compose — api, app, postgres, mailpit (email capture)

## Prerequisites

Docker Desktop, .NET 8 SDK, Node 20+. No credentials, no network beyond package restore.

## Run locally

```bash
# 1. Generate the synthetic directory (deterministic — canonical seed baked into the wrapper)
./scripts/synth/generate.sh

# 2. Start the stack: api, app, postgres, mailpit
docker compose up -d --build

# 3. Import the directory + seed framework v1 and Harris taxonomy (idempotent)
curl -X POST localhost:8080/api/admin/sync            # or: sign in as Platform Admin → Admin → Run sync

# 4. Open the app
#    app:      http://localhost:5173
#    api:      http://localhost:8080
#    mailpit:  http://localhost:8025   (all outbound email lands here)
```

Sign-in is the local dev provider's role picker — one seeded user per role:
Individual, Manager, BU Lead, Group Leader, Portfolio Leader, HIG Executive,
Platform Admin.

> Scaffold note: steps 1 and 3 above reference the synthetic-directory generator
> (`scripts/synth/generate.sh`) and the admin-sync endpoint, both delivered in
> later stories (HAP-2, HAP-3). The commands are reproduced verbatim from
> `specs/001-maturity-initiative-register/quickstart.md`, which is authoritative.

## Gate of record

```bash
./scripts/verify.sh
```

Builds both stacks warnings-as-errors, spins a disposable Postgres, applies
migrations idempotently, runs backend + frontend tests, lint, typecheck, and
**always** the `Category=PrivacyReporting` suite. Green exit or the story is not
reviewable. Run it under Git Bash on Windows: `bash scripts/verify.sh`.

## Repo map

```
backend/   .NET 8 (Hap.Api, Hap.Domain, Hap.Infrastructure, Hap.Synth + tests)
app/       React/TypeScript client (Vite)
scripts/   verify.sh (gate of record) + synth generators (later stories)
docs/      spec, backlog, design system, decisions, frameworks
specs/     feature specs, plans, quickstart
```
