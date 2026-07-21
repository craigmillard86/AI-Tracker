# HIG AI Maturity & Initiative Register — Application Specification

**Status:** Draft v0.1 for review
**Author:** Craig (Vanguard / HIG AI)
**Scope:** All HIG business units (~23 BUs)
**Delivery model:** Standalone internal web application

---

## 1. Purpose

HIG needs a single, group-wide view of two things:

1. **How mature each business unit is in its adoption of AI** — measured against a structured framework, re-assessed on a regular cadence, and trendable over time at individual, team, BU, and group level.
2. **What AI initiatives are actually running across the group** — a living register capturing each initiative's purpose, stage, ownership, value, and governance posture.

Today this exists as a spreadsheet covering one BU's engineering organisation. It cannot scale to 23 BUs, cannot enforce access control on individual scores, cannot track change over time, and contains no initiative register. This application replaces it.

The two modules are deliberately linked: initiatives are tagged to the maturity dimensions they advance, so leadership can see not just where a BU is weak, but whether anything is being done about it. The register also provides demand-side evidence for group AI platform investment (Project Cogito): it shows what BUs are building, with which models and vendors, and where consolidation onto shared infrastructure would pay off.

## 2. Users and roles

| Role | Description | Sees |
|---|---|---|
| **Individual** | Any employee invited to assess | Own assessments (self + manager scores after moderation), own team's aggregate, framework reference |
| **Manager** | Line manager of a team | Everything Individual sees, plus individual scores for direct reports; performs manager review |
| **BU Lead (EVP)** | EVP/Director of a business unit | All teams and individuals in their BU; BU-level initiative register (edit) |
| **Group Leader** | Leader of an operating group | Aggregate rollups and initiative register (read) for **all BUs in their group** |
| **Portfolio Leader** | Portfolio-level leadership | Aggregate rollups and initiative register (read) for **all groups in their portfolio** |
| **HIG Executive** | Group President, COO, Vanguard | **Consolidated all-HIG view**: every portfolio, group, and BU aggregate; full initiative register (read) |
| **Platform Admin** | Vanguard / platform team | Framework authoring, cycle management, BU onboarding, org sync configuration, user/role administration |

**Visibility is granted by default via the organisational hierarchy** — no per-request access. The application models the Harris structure explicitly: **Person → Team → BU → Group → Portfolio**. Each BU is registered with its group and portfolio mapping, and leadership roles derive their scope from that mapping automatically.

Access to individual-level assessment data follows the management chain only; leaders above BU level see aggregates. All aggregate views apply a minimum group size (suppress any aggregate covering fewer than N=4 individuals to prevent inference of individual scores).

## 3. Module 1 — AI Maturity Assessment

### 3.1 Framework model

The framework is **data, not code**. First-class entities:

- **Framework** — e.g. "AI in the SDLC", with description and owner.
- **Framework Version** — immutable once a cycle has used it; new versions can add/remove/reword dimensions and level descriptors. Historic assessments remain tied to the version they were scored against.
- **Dimension** — e.g. *How AI is Leveraged, Examples/Usage, Work Unit, SDLC Process, Timing, Value Measured, Impact* (the current seven).
- **Level Descriptor** — per dimension, per level (0–3): *Level 0 Waterfall/Agile, Level 1 AI Assisted, Level 2 AI Directed, Level 3 AI Delegated*, with the descriptive text from the current Reference sheet.

Multiple frameworks must be supported from day one. **The v1 framework is "AI Maturity in the SDLC", carried over from the current spreadsheet and documented in full in Appendix A.** Non-engineering functions across HIG BUs (finance, support, sales ops, clinical/regulatory teams) will need parallel frameworks in later phases. A BU is mapped to one or more frameworks; individuals are assessed against the framework(s) applicable to their function.

### 3.2 Assessment cycles

- Group Admin opens a **cycle** (e.g. "FY27 Q1") per framework, with open/close dates.
- **Participation is mandatory for every individual in a registered BU.** Once a BU is onboarded, all in-scope individuals are automatically invited each cycle — there is no per-person or per-manager opt-out.
- On open, invitations are generated for all in-scope individuals (derived from org sync + BU/framework mapping). Email notifications with magic-link/SSO deep link.
- Automated reminders to non-responders, with escalation summaries to managers and the BU Lead as the close date approaches; live completion dashboards per team/BU/group (mirroring the current Completion % column, but real-time). Completion % is itself a reported metric up the hierarchy.
- Cycles lock at close; late submissions require manager or admin override.
- Cadence: **monthly**. Each cycle runs the full self + manager review flow; the short cycle keeps trend data fresh and makes movement visible quickly. To keep the burden proportionate, the assessment form pre-populates with the individual's previous cycle scores so a "no change" month takes seconds, and manager review defaults to carrying forward prior moderated scores unless the self-score changed.

### 3.3 Scoring workflow (self + manager review)

1. **Self-assessment.** Individual scores 0–3 per dimension, with inline access to level descriptors. Optional free-text evidence/comment per dimension (strongly encouraged — it makes moderation meaningful).
2. **Manager review.** Manager sees the self-score and evidence, then records their own score per dimension. Manager may agree (default = adopt self-score) or diverge, with a comment required where divergence ≥ 2 levels.
3. **Moderated score.** The manager score is the score of record for rollups. Self-scores are retained and surfaced as a **calibration delta** metric (mean |self − manager| per dimension/team/BU) — a first-class signal of over/under-confidence and framework ambiguity.
4. **Full transparency to the individual:** the individual sees their per-dimension manager scores, comments, and any divergence from their self-score, alongside their overall moderated result. Disputes go offline (no in-app appeals workflow in v1).

### 3.4 Rollups and analytics

All rollups computed from moderated scores, per dimension and overall:

- Individual → Team (manager) → BU → Group → Portfolio, extending the current spreadsheet's rollup chain up the Harris hierarchy, plus:
- **Trend over cycles** — the headline chart: BU, group, and portfolio maturity trajectory per dimension.
- **Distribution views** — histograms of levels per dimension (averages hide bimodal teams; the current data shows exactly this, e.g. a Level 2 research team inside a Level 1 BU).
- **Calibration delta** views (self vs manager).
- **Completion/participation** per cycle.
- Role-based filtering throughout; small-group suppression applies (§2).

### 3.5 Org structure

- **BU registration:** each BU is onboarded with its applicable framework(s) and its directory source. The **BU → Group → Portfolio mapping is synced from Active Directory / Entra ID** (organisational attributes), with a manual override layer in the app for corrections.
- **Contractors are excluded** from assessment scope. Contractor status is derived from the directory (employee type attribute); excluded individuals do not receive invitations and do not count toward headcount or completion %. This policy is configurable per cycle if it changes later.
- Nightly sync from **Microsoft Entra ID / Graph** (or HRIS export where a BU's directory is unreliable): person, email, manager, job title, BU attribute, employee type.
- Manual override layer for corrections (BU assignment, dotted lines), with audit trail.
- Leavers are deactivated, not deleted; their historic scores remain in aggregates for the cycles they participated in.

## 4. Module 2 — AI Initiative Register

### 4.1 Initiative entity

| Field group | Fields |
|---|---|
| **Identity** | Name, BU, sponsor (exec), owner (delivery lead), description, date registered |
| **Classification** | Category — **aligned to the Harris AI Dashboard taxonomy**: *AI Product Feature / Pre-Built Agent deployed at customers / Custom Agent deployed at customers / Digital Worker (internal to Harris) / Other (internal-only, not group-reported)*; function(s) affected; maturity dimensions advanced (multi-select against framework dimensions); **AI-DLC level of the initiative (1–3)** — the level of AI autonomy the initiative embodies, required for Harris weekly level-breakdown counts |
| **Lifecycle** | Stage: *Idea → Evaluation → Pilot → Production → Scaled → Retired*, with stage history and dates. Harris stage mapping: Idea + Evaluation → *Ideation*; Pilot → *Development*; Production + Scaled → *Production*; Retired → *Ideas Tried but Stopped* (stage history preserves the stage at which it was stopped) |
| **Customers** | **# unique customers in production** (for customer-deployed categories), updated with the weekly status note |
| **Technology** | Models/providers used (e.g. Claude, Azure OpenAI, self-hosted open-weight), vendors/tools, whether it consumes the group AI platform (Cogito) or standalone infrastructure |
| **Value** | Value hypothesis (qualitative), **structured NR capture for Harris reporting: current-year Direct NR and Indirect NR, each split One-Time / Recurring, in $USD, with a brief description per line**; measured value to date and measurement method; effort/cost band |
| **Governance & risk** | Data sensitivity (none / internal / PII / PHI / clinical), regulatory relevance (e.g. EU MDR, IEC 62304, UK GDPR), risk tier (low/medium/high), approval status and approver, human-oversight model, notes |
| **Status** | RAG status, last update date, **weekly status note from the initiative owner** (register nags owners whose entry hasn't been updated in the last 7 days; overdue-update counts roll up to BU data-quality scores) |

### 4.2 Register behaviours

The register is **record-keeping for progress reporting only** — it is not an approval gate, and registering an initiative confers no authorisation. The governance and risk fields (§4.1) are informational: they let leadership see the risk profile of activity across the group, but any actual approvals happen through existing BU/group processes outside the application.

- Any Manager+ can create an initiative for their BU; BU Lead can edit/curate all entries in their BU. No approval workflow in v1.
- Full-text search and faceted filtering (BU, stage, category, risk tier, model/vendor, dimension).
- **Duplication/consolidation view:** initiatives grouped by category/technology across BUs to surface where three BUs are independently building the same thing — a core input to group platform strategy.
- **Weekly update discipline:** active initiatives (Evaluation → Scaled) require a weekly owner update — a lightweight RAG + one-line note, designed to take under a minute. Email reminder on day 7, escalation to the BU Lead after two missed weeks. Retired/Idea-stage entries are exempt.
- Stale-entry reporting and a data-quality score per BU (update timeliness plus completeness of value and governance fields).
- Export to CSV/Excel; read API for downstream reporting.

### 4.3 Cross-module views

- BU scorecard: maturity per dimension **alongside** count/stage of initiatives tagged to each dimension. "Level 0 on *Value Measured* with zero initiatives addressing it" should be visible at a glance.
- Group heatmap: 23 BUs × dimensions, coloured by moderated maturity, with initiative counts overlaid.

## 5. Non-functional requirements

- **Authentication:** Entra ID SSO (OIDC); no local accounts. Role assignment derived from directory groups plus in-app grants for Group Viewer/Admin.
- **Hosting:** Azure (consistent with group direction and Cogito) — e.g. App Service or AKS, Azure Database for PostgreSQL, Azure Communication Services or SMTP relay for notifications.
- **Stack (proposed):** .NET 8 Web API + React/TypeScript front end + PostgreSQL, or equivalent — aligned with existing internal tooling skills (OpsPortal is .NET/React, easing any future convergence).
- **Data protection:** Assessment data is employee personal data under UK GDPR. Requirements: DPIA before group rollout; purpose limitation stated in-app (development, not performance management — say this explicitly to drive honest self-assessment); retention policy (suggest raw individual scores retained 3 years, aggregates indefinitely); right-of-access support; full audit log of who viewed individual-level data.
- **Audit & logging:** Immutable audit trail for score changes, role grants, org overrides, and individual-data views.
- **Availability:** Internal business tool; 99.5% during business hours is sufficient. Daily backups, tested restore.
- **Accessibility:** WCAG 2.2 AA for the assessment flow (it will be used by every employee in scope).
- **Localisation:** English-only v1; string externalisation from the start (HIG BUs span multiple countries).

## 6. Reporting & dashboards

- In-app dashboards per role (individual, manager, BU, group) covering §3.4 and §4.3.
- Scheduled PDF/email digest per cycle close for BU Leads and group leadership.
- Optional Power BI connectivity via read-only reporting schema or API for BUs that want to blend with their own data.

### 6.1 Harris AI Dashboard submissions

EVPs are required to submit to the group **Harris AI Dashboard** weekly and monthly. The application generates a **pre-filled submission report per BU** matching the Harris form structure, so the EVP's job becomes review-and-transcribe rather than data assembly. (The Harris dashboard has no known API; output is an on-screen report plus PDF export mirroring the form layout. If an API becomes available, direct submission is a Phase 3 candidate.)

**Weekly submission — produced from register + weekly declaration:**

| Harris form section | Source in this application |
|---|---|
| AI-DLC level, date expected to reach next level, status (RAG) | **Weekly BU AI-DLC declaration** (§6.2) — declared by the EVP, with a supporting evidence panel showing the BU's measured maturity distribution from the assessment module |
| Per category (AI Product Features, Pre-Built Agents, Custom Agents, Digital Workers): counts in Ideation / Development / Production, broken down by initiative AI-DLC level 1/2/3 | Register: live counts by category × mapped stage × initiative AI-DLC level |
| # unique customers in production per category | Register: sum of per-initiative customer counts |
| Ideas Tried but Stopped (by stage and level) | Register: Retired initiatives, counted at the stage held when retired |

**Monthly submission — produced from register financials + monthly BU metrics:**

| Harris form section | Source in this application |
|---|---|
| Per category: 2026 Direct NR and Indirect NR, One-Time / Recurring, $USD, with descriptions | Register: aggregated per-initiative NR lines (YTD, "aggregate up to and including the submission month") |
| Support — internal use (estimated time savings %, fewer people?, support ratio impact) | **Monthly BU metrics form** (§6.2) |
| Support — customer use (# unique customers supported YTD, total tickets YTD, # resolved 100% by AI YTD, # AI-assisted YTD) | Monthly BU metrics form |
| API / System of Record — are other applications calling our SOR? (current month only) | Monthly BU metrics form |

### 6.2 BU-level capture points (Harris reporting inputs)

Two lightweight forms exist solely to feed the Harris submission; both are completed by the EVP or a delegate:

- **Weekly BU AI-DLC declaration:** declared AI-DLC level (0–3), date expected to reach next level, RAG status, optional internal note. The declaration screen shows the measured maturity data (floor-rule level distribution and mean trend) alongside, so the declared level is evidenced rather than guessed — and divergence between declared and measured levels is itself reportable to group leadership.
- **Monthly BU metrics:** the Support (internal and customer) and SOR questions above. YTD fields auto-carry the prior month's values for editing; SOR usage is current-month only, per the Harris form's instructions.

Reminder scheduling for both follows the notification rules in §5 (email only), aligned to the Harris submission deadlines.

## 7. Phasing

**Phase 1 — MVP (agentic build with Claude Code; build effort measured in days, MVP live ~2 weeks from approval — calendar governed by directory audit, DPIA, and onboarding rather than code production):**
Org sync, SSO, framework engine with v1 SDLC framework loaded, cycle management, self + manager assessment flow, team/BU/group rollups, initiative register with the Harris-aligned taxonomy, AI-DLC level, stage mapping, and NR capture, the weekly BU AI-DLC declaration and monthly BU metrics forms, and the **Harris AI Dashboard pre-filled submission report (weekly + monthly)** — this is the immediate EVP pain point, so it ships in the MVP. HHA onboards first and runs **Cycle 1, the group's first measured maturity baseline** (the current spreadsheet's assessment data is synthetic and is not migrated — see §8).

**Phase 2 — adoption-driving features plus deeper analytics (+1–2 weeks, agentic delivery):**
**Benchmarking league tables and group heatmap** (per dimension, visible to leadership by default), **level-up playbooks** linked to each assessment result, **showcase and duplication/consolidation views** on the register, and the **idea-intake pipeline** feeding the Ideation stage — plus trend analytics across cycles, calibration-delta views, distribution charts, governance/value field enforcement on the register, stale-entry nudges, digest emails.

**Phase 3:**
**Usage telemetry integrations** (AI tool activity and licence utilisation per BU, Cogito consumption) to complement self-reported scores, **champions network and enablement/training tracking**, additional frameworks for non-engineering functions, Power BI schema, cross-module scorecards/heatmaps, API for Cogito demand-planning integration, and direct Harris dashboard submission if an ingestion API becomes available.

## 8. Relationship to the current spreadsheet

The existing spreadsheet is a **design artefact, not a data source**: its assessment rows are synthetic, created to prove the framework, scoring model, and rollup logic. Accordingly:

- Reference sheet → Framework v1 (dimensions + level descriptors) — this is the one part carried over as-is.
- Org Structure sheet → structural template only; real org data comes from directory sync at onboarding.
- Individual Assessment / rollup sheets → **not migrated**. The first real data is Cycle 1, run at HHA immediately after MVP go-live — the group's first measured maturity baseline. Contractors are excluded from scope per the participation policy.

## 9. Decisions made

1. **Participation:** mandatory for all individuals in registered BUs. **Contractors are excluded** (directory employee-type attribute).
2. **Score visibility to individuals:** full transparency — individuals see per-dimension manager scores, comments, and divergence, plus their overall moderated result.
3. **Register purpose:** record-keeping for progress reporting only; no approval/governance gate in v1.
4. **Leadership access:** granted by default via the organisational hierarchy — EVP sees their BU, Group Leader sees all BUs in their group, Portfolio Leader sees all groups in their portfolio, and **Group President, COO, and Vanguard hold a consolidated all-HIG view**.
5. **v1 framework:** "AI Maturity in the SDLC" as defined in the current spreadsheet (Appendix A). Non-engineering frameworks deferred to Phase 3.
6. **Hierarchy source of truth:** BU → Group → Portfolio mapping synced from Active Directory / Entra ID, with in-app override.
7. **Notifications:** email only — no Teams integration.
8. **Cadence:** maturity assessment cycles run **monthly**; initiative owners provide **weekly** status updates on active initiatives.
9. **Level derivation:** weakest-dimension floor — maturity level = minimum dimension score. Mean score retained as the continuous trend metric (Appendix A).
10. **Harris AI Dashboard reporting:** the application is the system of record feeding the EVP's weekly and monthly Harris AI Dashboard submissions, generating pre-filled reports matching the Harris form structure (§6.1). Register taxonomy, stages, and financial fields are aligned to the Harris forms.

## 10. Open questions

1. Which AD/Entra attributes reliably carry the group/portfolio mapping and employee type across all 23 BUs? (Directory hygiene audit needed before rollout — expect inconsistency between BU tenants/domains.)
2. Confirm the Harris stage mapping (Pilot → Development; Production + Scaled → Production) with whoever owns the Harris AI Dashboard definitions, and the intended meaning of an initiative's "level" (we've interpreted it as the AI-DLC autonomy level the initiative embodies).
3. Does the Harris dashboard team plan an ingestion API? If so, direct submission replaces the transcribe step (Phase 3).

---

## Appendix A — v1 Framework: AI Maturity in the SDLC

Four levels, seven dimensions, scored 0–3 per dimension. Level descriptors below are shown to assessors inline during scoring.

**Levels:** Level 0 — Waterfall / Agile · Level 1 — AI Assisted · Level 2 — AI Directed · Level 3 — AI Delegated

| Dimension | Level 0: Waterfall / Agile | Level 1: AI Assisted | Level 2: AI Directed | Level 3: AI Delegated |
|---|---|---|---|---|
| **How AI is leveraged** | Sporadic task support | Consistent AI assistance used by individuals | As a team member executing work under human direction | A delivery system of agents operating in the background |
| **Examples / Usage** | Various tasks within the SDLC such as writing requirements, writing code, debugging, etc. | AI suggests lines of code, explains unfamiliar modules, helps debug, or drafts tests | AI is prompted through idea clarification and design, creates specs, architects, generates code, and tests | A bug is assigned to AI; it triages, replicates, builds, tests, and returns a proposed solution |
| **Work unit** | Tasks and features | Tasks | Features | Full build (tasks, bugs, features, maintenance) |
| **SDLC process** | Traditional SDLC remains intact | Traditional SDLC remains intact (faster execution) | SDLC becomes a faster guided pipeline with AI | SDLC steps collapse into an asynchronous agent loop |
| **Timing** | Weeks | Weeks | Days / hours | Hours (asynchronous) |
| **Value measured** | % of committed work delivered on time | % of tickets influenced by AI | % of lines of code built by AI | % of tickets deflected away from developers |
| **Impact** | n/a | Max 1× improvement | 10× improvement (with effort) | 20×+ potential ballpark improvement |

**Scoring rules:** two measures are reported side by side. The **overall maturity score** is the mean of the seven dimension scores (a continuous 0–3 value, used for trend lines and rollup averages, as in the current spreadsheet). The **maturity level label** uses a **weakest-dimension floor**: an individual's level is the *minimum* score across the seven dimensions — you are only Level 2 when every dimension is at least 2. Rollups at each level of the hierarchy report the mean score per dimension plus the distribution of floor-based levels (e.g. "12% of BU at Level 1+"). Note this supersedes the spreadsheet's rounded-mean labels: expect most of the current population to label as Level 0 under the floor rule (nearly everyone has at least one zero-scored dimension), which is intentional — the floor makes the label an honest "no weak links" claim and progress against it meaningful.
