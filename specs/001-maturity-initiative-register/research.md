# Research — Phase 0 technical decisions

All Technical Context unknowns resolved. Stack, ports, and privacy architecture were fixed by the constitution, DR records, and the owner brief; the decisions below are the remaining genuine choices, each with rationale and rejected alternatives. Every new dependency named here still gets its L2 review at story time — this file decides direction, not sign-off.

## D1. Seam enforcement mechanism

**Decision**: `Hap.Api/Authorization/AssessmentReads` is the only type allowed to query `Assessments`/`AssessmentScores`. Enforced three ways: (a) EF entity configurations for those tables live in an `internal` namespace consumed only by the seam's gateway; (b) an architecture test (`Hap.Architecture.Tests`, `Category=PrivacyReporting`) fails if any other namespace references those DbSets or table names; (c) integration tests exercise every endpoint as every seeded role.

**Rationale**: A compile-time + test-time double lock makes "no query path outside the seam" falsifiable, which is what G1 and the red-team brief need. **Rejected**: repository-pattern indirection everywhere (speculative abstraction, Art. IX.4); separate database schema with grants (heavier ops for the same guarantee locally).

## D2. Complement suppression algorithm (FR-014)

**Decision**: Suppression evaluated at publication over the fixed hierarchy tree. A node's aggregate is suppressed if `n < 4`, or if `0 < parent.n − node.n < 4` (its complement within the parent is identifiable), evaluated with already-suppressed siblings excluded from the published set. Suppressed cells render as "Suppressed" with the reason, never zero/blank (FR-071). Historical aggregates keep the suppression verdict computed at cycle close (stored with the rollup snapshot), so later org shrinkage cannot retro-expose (FR-071) — and recomputation cannot silently unsuppress history.

**Rationale**: Closes the subtraction attack within v1's only publication surface (fixed rollups) with an O(children) rule that is easy to test exhaustively against the synthetic hierarchy's engineered edge cases. **Rejected**: noise/rounding (Art. VI.4 requires figures reconcile exactly — noise conflicts); full SDC machinery (no ad-hoc slicing exists in v1 to warrant it).

## D3. Session & identity port shape

**Decision**: `IIdentityProvider` exposes challenge/sign-in and returns a claims principal (person id + granted roles). Local dev provider renders a role-picker sign-in listing seeded users (one per role minimum) and issues an ASP.NET Core auth cookie. The seam consumes only the claims principal — provider-agnostic, so the Entra OIDC adapter later replaces sign-in without touching authorization.

**Rationale**: Cookie auth is the simplest correct local mechanism and mirrors what OIDC middleware produces, keeping the seam contract identical. **Rejected**: JWT bearer (adds token plumbing the local build doesn't need); magic links via mailpit (adds flow complexity; deep-links can still target the dev sign-in).

## D4. Rollup materialisation

**Decision**: Rollups computed on read for open cycles; at cycle close a rollup snapshot per org node (mean per dimension, floor distribution, n, suppression verdict) is persisted. Trend charts read snapshots; the live dashboard reads current cycle on the fly.

**Rationale**: <30s/BU is trivially met either way at this scale, but snapshots make history immutable (supports FR-071 historical-visibility rule and honest trends over org changes) and make reconciliation testable against a fixed artifact. **Rejected**: always-live computation (history mutates as org moves); full OLAP/reporting schema (Phase 3's Power BI concern, YAGNI now).

## D5. Harris submission generation & reconciliation

**Decision**: `Hap.Domain/Submissions` generates a submission document from: register counts (category × mapped stage × level, "Other" excluded), declaration, metrics, and NR YTD aggregation — using the stage-mapping and taxonomy **tables**, not code. Each generated submission is persisted with its line items and the as-of timestamp. The reconciliation suite recomputes every figure via independent hand-written SQL (not EF, not the production query) and asserts equality; tagged `Category=PrivacyReporting`.

**Rationale**: Persisting the generated document gives G2 a fixed artifact to reconcile line-by-line, and independent-query verification is the constitution's own definition of reconciliation (Art. VI.4). **Rejected**: regenerate-on-view only (nothing stable to witness at G2); reusing production queries in tests (circular — proves nothing).

## D6. PDF export (FR-049)

**Decision**: Print stylesheet on the submission review screen; "Export PDF" invokes the browser print dialog. No PDF library.

**Rationale**: Satisfies "PDF export mirroring the form layout" with zero new dependencies and keeps layout single-sourced with the on-screen report. **Rejected**: QuestPDF/wkhtmltopdf (new L2 dependency + duplicate layout definition for no MVP gain; revisit if scheduled digest attachments (Phase 2) need server-side rendering).

## D7. Email & scheduling

**Decision**: MailKit SMTP adapter pointed at mailpit. Reminder/escalation jobs run in a .NET hosted service on `PeriodicTimer`, with an admin-only `POST /api/admin/notifications/run` to trigger deterministically in tests and demos.

**Rationale**: One small dependency; jobs are plain queries + sends, testable without a scheduler framework. **Rejected**: Quartz/Hangfire (persistent-store schedulers are overkill for daily/weekly ticks); `System.Net.Mail.SmtpClient` (obsolete per Microsoft guidance).

## D8. Synthetic data generation

**Decision**: `Hap.Synth` console project (shares the single .NET toolchain), seeded PRNG with the seed recorded in output metadata; emits a directory-export JSON consumed by the `SyntheticDirectoryAdapter`. `scripts/synth/` holds thin shell wrappers with the canonical seeds. Generated population: 23 BUs across 6 groups / 3 portfolios, 300–800 people per BU, engineered edge cases — sub-4 teams, a sub-4 BU, single-team BU (complement case), manager gaps, mid-cycle moves, contractors, leavers.

**Rationale**: Deterministic output is required by the constitution ("never silently change a synth generator's distributions" — distributions live in one reviewed file); .NET console avoids a second language runtime. **Rejected**: TypeScript/Node generator (second toolchain in verify.sh for no benefit); Bogus/faker libraries (uncontrolled distribution drift across versions — hand-rolled tables are deterministic by construction).

## D9. Frontend stack details

**Decision**: Vite + React 18 + TypeScript strict; react-router-dom; thin typed `fetch` client (no TanStack Query); `@fontsource/montserrat` + `@fontsource/inter-tight` self-hosted (constitution: no runtime network — mockups' Google Fonts links are docs-only); `tokens.css` custom properties generated once from DESIGN.md values with a Vitest guard asserting tokens match the addendum; hand-rolled SVG for bars/sparklines; Vitest + RTL + vitest-axe (axe assertions on the assessment flow screens = WCAG AA acceptance criteria, SC-007).

**Rationale**: Fewest dependencies that satisfy the design and a11y obligations; every item here is individually justifiable at its L2 review. **Rejected**: chart library (two SVG primitives don't warrant one); CSS framework/Tailwind (DESIGN.md is the system; a framework fights it); Playwright e2e in MVP (QA phase drives the app manually per story; revisit if regressions demand it).

## D10. Test database strategy

**Decision**: `verify.sh` starts a disposable Postgres container (compose run, random port), applies migrations idempotently, runs both stacks' suites, tears down. Integration tests get the connection string via environment; per-test isolation via transaction rollback, and truncate-based reset between fixtures (no Respawn dependency).

**Rationale**: Keeps container lifecycle in the gate of record where the constitution puts it; no Testcontainers dependency to review. **Rejected**: Testcontainers-dotnet (duplicates what verify.sh already owns); SQLite-in-memory (dialect drift would undermine suppression/aggregation SQL fidelity — exactly the queries that must be trustworthy).
