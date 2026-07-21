# Implementation Plan: HIG AI Maturity & Initiative Register (Phase 1 MVP)

**Branch**: `001-maturity-initiative-register` | **Date**: 2026-07-21 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/001-maturity-initiative-register/spec.md` (71 FRs, clarified 2026-07-21) + owner technical context (stack fixed by decision record; constitution and CLAUDE.md binding).

**FR citation note**: the owner brief said to cite FR-IDs from the root spec; per DR-0002 the citation scheme is `FR-NNN` from **this feature spec**, which traces to the root spec. Stories below cite feature-spec FR-NNN.

## Summary

Build the fully local Phase 1 MVP: org hierarchy from a synthetic directory, dev-provider sign-in for all seven roles, framework engine seeded from versioned data, global monthly assessment cycles with self + manager moderation, rollups behind a single L3 visibility seam (chain-of-command + role scope + N<4 suppression with complement defense), an initiative register aligned to the Harris taxonomy, and pre-filled weekly/monthly Harris submission reports that reconcile 100% to underlying records. Twenty stories in three waves; HAP-12 flags Gate G1 readiness, HAP-20 flags Gate G2 readiness.

## Technical Context

**Language/Version**: Backend C# / .NET 8 (Hap.Api, Hap.Domain, Hap.Infrastructure + tests). Frontend TypeScript (strict) / React 18.

**Primary Dependencies**: EF Core + Npgsql (data), MailKit (SMTP → mailpit), ASP.NET Core cookie auth (session over the `IIdentityProvider` port). Frontend: Vite, react-router-dom, `@fontsource/montserrat` + `@fontsource/inter-tight` (self-hosted fonts — no Google Fonts fetch at runtime). Dev/test: xUnit, Testcontainers **not** used (verify.sh manages the disposable Postgres), Vitest + React Testing Library + vitest-axe, ESLint + Prettier. Every new dependency is an L2 trigger at story time.

**Storage**: PostgreSQL via EF Core; forward-only migrations, compiled idempotently in verify.sh.

**Testing**: TDD. xUnit (domain unit + API integration via WebApplicationFactory against dockerised Postgres), architecture tests enforcing seam-bypass ban, Vitest/RTL/axe for the app. Every test touching RBAC, suppression, audit, or submission maths carries `Category=PrivacyReporting` (or the Vitest tag equivalent) and runs on every `./scripts/verify.sh`.

**Target Platform**: Local docker-compose only — services: `api`, `app`, `postgres`, `mailpit`. No cloud, no external credentials; network use limited to package restore.

**Project Type**: Web application (backend + frontend).

**Performance Goals**: Rollup compute + Harris submission generation < 30s per BU (SC-008); role dashboards load < 5s at 23 BUs / ≥10,000 people / ≥2,000 teams; org sync (synthetic import) < 30 min (SC-009).

**Constraints**: All assessment reads through `Hap.Api/Authorization` (L3); audit append-only; framework/Harris/taxonomy content is versioned data, never code; synthetic data only; WCAG 2.2 AA on the assessment flow; UI bound to `docs/design/DESIGN.md` + `docs/design/mockups/`.

**Scale/Scope**: 23 BUs, ~6 groups, 3 portfolios, ≥10,000 people, ≥2,000 teams, monthly cycles, 8 mockup screens (7 in scope; heatmap is Phase 2).

## Constitution Check

*Gate run against constitution v1.1.2 before Phase 0 and re-checked after Phase 1 design. Result: **PASS, no violations to justify.***

| Article | Check | Result |
|---|---|---|
| I — Surfaces of truth | Plan artifacts live in `specs/001-…`; stories become `docs/backlog/` files at `/speckit-tasks`; no duplicated sources introduced | PASS |
| II — Spec before code | Every story cites FR-NNN from the clarified feature spec (DR-0002); framework/Harris/taxonomy content planned as seeded data, never code | PASS |
| III — Agent-maximal | Stories sized one agent / one worktree / one sitting; QA planned as separate adversarial agent per story | PASS |
| IV — Lifecycle | Plan feeds Phase 1 setup per story; drift sweep, worklogs, four-box closure unaffected | PASS |
| V — Risk classes | Each story carries a class from the CLAUDE.md §7 trigger table, first match wins, uncertainty rounded up (see story table) | PASS |
| VI — Privacy/reporting seam | Seam (HAP-5) and synthetic directory (HAP-2/3) land before any feature reading assessment data; suppression includes complement defense (FR-014); audit append-only (no update/delete mapped); reconciliation tests planned beside the aggregation (HAP-16) | PASS |
| VII — Gates & Wave-0 spike | HAP-5 is the Wave-0 spike hardened into the seam; HAP-12 flags G1 readiness; HAP-20 flags G2 readiness; no story self-certifies a gate | PASS |
| VIII — Honest time/money | Estimates set at story Phase 1 by the executing agent, not here | PASS |
| IX — Engineering standards | Stack as fixed; TDD; forward-only migrations; only the two decision-recorded ports (identity, directory); no plugin architecture; WCAG 2.2 AA in acceptance criteria | PASS |
| X — Hard don'ts | No cloud dependency, no real data, no hard-coded framework/Harris content planned | PASS |

## Project Structure

### Documentation (this feature)

```text
specs/001-maturity-initiative-register/
├── plan.md              # This file
├── research.md          # Phase 0 — consolidated technical decisions
├── data-model.md        # Phase 1 — entities, relationships, state machines
├── quickstart.md        # Phase 1 — run + validate end-to-end locally
├── contracts/
│   └── api.md           # Phase 1 — API surface by role scope + the two ports
└── tasks.md             # Phase 2 (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
backend/
├── Hap.sln
├── src/
│   ├── Hap.Api/                    # ASP.NET Core; controllers thin
│   │   ├── Authorization/          # THE VISIBILITY SEAM — L3 always
│   │   │   ├── ChainResolver…      # management-chain resolution
│   │   │   ├── RoleScope…          # role → org-scope predicates
│   │   │   ├── Suppression…        # N<4 + complement rules (FR-014)
│   │   │   └── AssessmentReads…    # the ONLY gateway to Assessments/AssessmentScores
│   │   └── Identity/               # IIdentityProvider port + LocalDevProvider
│   ├── Hap.Domain/                 # entities + pure logic, no EF
│   │   ├── Scoring/                # mean, floor, calibration delta (L2)
│   │   └── Submissions/            # Harris generation + aggregation (L3)
│   ├── Hap.Infrastructure/
│   │   ├── Persistence/            # DbContext, EF configs, forward-only Migrations/
│   │   ├── Audit/                  # append-only writer/reader (L3)
│   │   ├── Directory/              # IDirectorySource port + SyntheticDirectoryAdapter
│   │   └── Email/                  # MailKit → mailpit
│   └── Hap.Synth/                  # deterministic seeded generator (console)
└── tests/
    ├── Hap.Domain.Tests/
    ├── Hap.Api.Tests/              # integration incl. per-role privacy suites
    └── Hap.Architecture.Tests/     # seam-bypass ban, audit append-only ban

app/
├── src/
│   ├── design/                     # tokens.css generated from DESIGN.md values
│   ├── components/                 # inventory below
│   ├── screens/                    # one folder per mockup screen
│   └── api/                        # thin typed fetch client (no query library)
└── (Vitest colocated tests)

scripts/
├── verify.sh                       # gate of record (L2 to change)
├── synth/                          # wrappers invoking Hap.Synth with fixed seeds
└── board.sh                        # backlog board regeneration

docker-compose.yml                  # api, app, postgres, mailpit
```

**Structure Decision**: Web application layout matching the CLAUDE.md repo map. The seam is a physical namespace (`Hap.Api/Authorization`) so the L3 trigger and the architecture test have a crisp boundary; `Hap.Domain` stays EF-free so scoring/submission maths is unit-testable in isolation.

## Data model, contracts, validation

- Entities, relationships, and state machines: [data-model.md](data-model.md)
- API surface by role scope, suppression/audit markers, the two ports: [contracts/api.md](contracts/api.md)
- End-to-end run + gate-evidence walkthrough: [quickstart.md](quickstart.md)
- Technical decisions and rejected alternatives: [research.md](research.md)

## Story slicing & sequencing

One agent, one worktree, one sitting each. Risk from CLAUDE.md §7 (first match wins, uncertainty rounds up). Waves per constitution Art. VII. **Dependency rule enforced: nothing that reads assessment data lands before HAP-5.**

### Wave 0 — Foundations

| Story | Title | FRs | Risk | Depends on | Notes |
|---|---|---|---|---|---|
| HAP-1 | Scaffold & gate of record: solution, projects, app shell, docker-compose (api/app/postgres/mailpit), `verify.sh` | FR-057(infra), FR-058, FR-059, FR-067 | **L2** (verify.sh + initial dependencies) | — | Warnings-as-errors both stacks; fonts self-hosted; `tokens.css` from DESIGN.md |
| HAP-2 | Synthetic directory generator (`Hap.Synth` + `scripts/synth/`) | FR-020 (source), SC-008 scale | **L2** (feeds directory import; rounds up) | HAP-1 | Deterministic seed; 23 BUs / 6 groups / 3 portfolios; edge cases: sub-4 teams, manager gaps, mid-cycle moves, contractors, leavers |
| HAP-3 | Org model & directory import (people→team→BU→group→portfolio, overrides, leavers) | FR-020–FR-024 | **L3** (directory-import writes to people/hierarchy; audit writes) | HAP-2 | `IDirectorySource` port + synthetic adapter; override audit rows |
| HAP-4 | Identity port & dev sign-in (cookie session, role derivation, role-picker sign-in) | FR-055, FR-056 | **L2** (`IIdentityProvider` + session handling) | HAP-3 | Entra adapter explicitly out of scope; port design must not touch the seam |
| HAP-5 | **Visibility seam** (Wave-0 spike, hardened): chain resolver, role scopes, N<4 + complement suppression, architecture test banning bypass | FR-014, FR-025, FR-071 | **L3** | HAP-3, HAP-4 | Proves predicates against the full synthetic hierarchy incl. edge cases before anything reads scores |
| HAP-6 | Framework engine seeded from `docs/frameworks/ai-maturity-sdlc.v1.json`; version immutability | FR-001, FR-054 | **L2** (schema/migrations) | HAP-1 | No dimension name in C#/TS — architecture test greps seeded content |

### Wave 1 — Assessment core → G1

| Story | Title | FRs | Risk | Depends on | Notes |
|---|---|---|---|---|---|
| HAP-7 | Cycle management: global-per-framework monthly state machine, invitations, contractor exclusion, late override | FR-002–FR-006, FR-060 | **L2** (cycle state machine) | HAP-3, HAP-6 | Invitation set derived at open; mid-cycle onboarding joins next cycle |
| HAP-8 | Self-assessment API + UI (mockup `assessment-self.html`) | FR-007, FR-062, FR-066 | **L3** (read/write over Assessments) | HAP-5, HAP-7 | Pre-population, evidence, purpose-limitation banner, WCAG AA |
| HAP-9 | Manager moderation API + UI (mockup `assessment-moderation.html`) | FR-008–FR-012, FR-063 | **L3** | HAP-8 | Δ≥2 forced comment, carry-forward default, transparency view, audit rows on individual views |
| HAP-10 | Cycle close: auto-adopt unmoderated, leave status, manager-departure escalation | FR-068, FR-069, FR-070 | **L3** (writes moderated scores) | HAP-9 | Unmoderated flag excluded from calibration delta |
| HAP-11 | Rollups & BU dashboard (mockup `dashboard-bu.html`) | FR-013, FR-015–FR-019, FR-041 | **L3** (aggregate reads via seam) | HAP-5, HAP-10 | Mean + floor distribution, completion %, suppressed cells render "Suppressed", cross-module counts stubbed until HAP-13 |
| HAP-12 | Audit & GDPR surface: individual-view audit completeness, right-of-access export, retention job | FR-050–FR-053 | **L3** | HAP-9, HAP-11 | **Flags G1 readiness** — with HAP-5/8/9/11 shipped, every seeded role can be walked through the G1 script (quickstart.md §Gate G1) |

### Wave 2 — Register & Harris → G2

| Story | Title | FRs | Risk | Depends on | Notes |
|---|---|---|---|---|---|
| HAP-13 | Initiative register core + list UI (mockup `register-list.html`) | FR-026, FR-027, FR-034, FR-035 | **L2** (schema; taxonomy as seeded data) | HAP-4 | Harris taxonomy/category tables seeded, not enums; Manager+ create, BU Lead curate |
| HAP-14 | Initiative detail: forward-only stage history, NR lines, weekly updates, customers (mockup `register-detail.html`) | FR-028–FR-033 | **L2** | HAP-13 | Stage transitions immutable; corrections are new transitions |
| HAP-15 | BU capture forms: weekly AI-DLC declaration + monthly metrics (mockup `bu-forms.html`) | FR-047, FR-048 | **L2** (schema) | HAP-11 | Declaration shows measured evidence panel (via seam); YTD carry-forward |
| HAP-16 | **Harris submission engine** (weekly + monthly) + reconciliation suite | FR-043–FR-046, FR-064, FR-065 | **L3** (submission generation + aggregation) | HAP-14, HAP-15 | Stage mapping + "Other" exclusion as configuration data; every figure covered by an independent-query reconciliation test |
| HAP-17 | Harris submission UI + PDF print view (mockup `harris-submission.html`) | FR-049 | **L1** (display of L3-produced data; no aggregation logic in UI) | HAP-16 | Print stylesheet, no PDF library; declared-vs-measured divergence panel |
| HAP-18 | Notifications: cycle reminders/escalations, weekly-update nags, mailpit adapter | FR-037, FR-057, FR-061 | **L2** (notification scheduling) | HAP-7, HAP-14 | Hosted service + admin "run now" for deterministic tests |
| HAP-19 | Completion & data-quality reporting, CSV export, read API | FR-019, FR-038, FR-039, FR-040 | **L3** (completion reads over Assessments — rounds up) | HAP-11, HAP-14 | Data-quality score = timeliness + field completeness |
| HAP-20 | G2 readiness: end-to-end reconciliation evidence script + gate walkthrough | SC-004 | **L0** (test/docs only) | HAP-16, HAP-17, HAP-19 | **Flags G2 readiness**; consolidates PrivacyReporting suite coverage report |

## API surface (summary — full contract in contracts/api.md)

Grouped by the seam's role scopes. **[S]** = returns aggregates subject to N<4 + complement suppression. **[A]** = individual-level read, writes an audit row.

- **Self (any authenticated)**: `GET /api/me`, `GET/PUT /api/me/assessment` **[A self-view exempt]**, `GET /api/me/assessment/result` (moderated + divergence), `GET /api/me/team/summary` **[S]**, `GET /api/me/export` (right-of-access), `GET /api/frameworks/current`
- **Manager**: `GET /api/team/reviews`, `GET /api/team/members/{personId}/assessment` **[A]**, `PUT /api/team/reviews/{assessmentId}`, `GET /api/team/completion`
- **BU Lead**: `GET /api/bus/{buId}/dashboard` **[S]**, `GET /api/bus/{buId}/people/{personId}/assessment` **[A]** (chain only), declarations `GET/POST /api/bus/{buId}/declarations`, metrics `GET/POST /api/bus/{buId}/metrics`, submissions `GET /api/bus/{buId}/submissions/{weekly|monthly}`
- **Group / Portfolio / HIG Executive**: `GET /api/org/{nodeId}/rollup` **[S]** (aggregates only — no individual endpoint exists at these scopes), `GET /api/initiatives?scope=` (read)
- **Manager+ (register)**: `GET/POST /api/initiatives`, `GET/PUT /api/initiatives/{id}`, `POST /api/initiatives/{id}/stage`, `POST /api/initiatives/{id}/updates`, `GET /api/initiatives/export.csv`
- **Platform Admin**: `POST /api/cycles`, `POST /api/cycles/{id}/close`, `POST /api/admin/sync`, `POST /api/admin/overrides`, `GET /api/admin/audit`, `POST /api/admin/notifications/run`
- **Read API (downstream reporting)**: `GET /api/reporting/register` (FR-040; register only, no assessment data)

## UI component inventory

Screens implement their mockup (binding: layout/IA/states); tokens from DESIGN.md. Charts are hand-rolled SVG (no chart library).

| Screen (story) | Mockup | Existing addendum components used | **New components (specified in DESIGN.md §A8, added 2026-07-21)** |
|---|---|---|---|
| App shell (HAP-1) | all | deep-navy top bar + left nav (A6), buttons (A4) | — |
| Sign-in role picker (HAP-4) | — (no mockup; QUESTIONS.md if contested) | cards, buttons | — |
| Self-assessment (HAP-8) | assessment-self.html | forms (A4 inputs), banner colours (A2) | **LevelSelectorCard** (selectable descriptor card), **ProgressStepper** (dimension progress + projected floor), **PurposeBanner** |
| Manager moderation (HAP-9) | assessment-moderation.html | tables, badges, buttons | **DivergenceFlag** (Δ badge + forced-comment state), **ComparisonRow** (self vs manager) |
| BU dashboard (HAP-11) | dashboard-bu.html | cards, badges, RAG (A2), maturity ramp (A2) | **StatTile**, **DimensionBar** (SVG), **TrendSparkline** (SVG), **SuppressedCell** ("—" + reason, colour-independent) |
| Register list (HAP-13) | register-list.html | DataTable (A4 tables), badges/chips, buttons | **StaleRowFlag** (overdue treatment) |
| Register detail (HAP-14) | register-detail.html | cards, forms, badges | **StageTimeline** (forward-only history), **NRLineEditor** |
| BU forms (HAP-15) | bu-forms.html | forms, buttons | **EvidencePanel** (measured distribution beside declaration) |
| Harris submission (HAP-17) | harris-submission.html | tables, badges | **PrintLayout** (print stylesheet wrapper), reuses EvidencePanel + DivergenceFlag |

`heatmap-group.html` is **not** implemented (Phase 2, FR-042); HeatmapCell is not built.

## Out of scope — /speckit-tasks MUST NOT generate work for these

- Entra ID OIDC adapter, Microsoft Graph/Entra directory sync, HRIS connectors (ports only; adapters are deferred decision-recorded stories)
- Azure hosting, any cloud service, external credentials, real employee data, DPIA execution (pre-rollout activity outside this repo)
- Phase 2: group heatmap + league tables (FR-042, `heatmap-group.html`), duplication/consolidation view (FR-036), playbooks, showcase, idea-intake, digest emails, governance/value field enforcement, analytics view polish
- Phase 3: telemetry integrations, champions/enablement, non-engineering frameworks, Power BI schema, Cogito API, direct Harris API submission
- In-app appeals workflow, initiative approval workflow, Teams notifications, localisation beyond string externalisation, phone-width layouts (desktop-first, tablet fallback)

## Complexity Tracking

No constitution violations to justify. For the record, two judgment calls made in the *simpler* direction: no client data-fetching library (thin typed fetch client instead — one less L2 dependency, adequate at this scale) and no PDF library (print stylesheet satisfies FR-049). Reversal of either later is an ordinary L2 story, not a rework.
