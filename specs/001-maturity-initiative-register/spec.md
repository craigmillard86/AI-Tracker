# Feature Specification: HIG AI Maturity & Initiative Register

**Feature Branch**: `001-maturity-initiative-register`

**Created**: 2026-07-21 · **Amended**: 2026-07-21 (owner review — 8 fidelity fixes; see checklist)

**Status**: Draft — Ready for Planning

**Input**: Root specification `docs/spec/hig-ai-maturity-platform-specification.md` (Draft v0.1) — group-wide AI adoption measurement and initiative tracking across ~23 HIG business units. Per DR-0002, the FR-NNN identifiers in this document are the citation scheme for commits and backlog stories.

---

## Clarifications

### Session 2026-07-21

- Q: What happens when a manager never completes moderation before the cycle locks? → A: Auto-adopt the self-score as the moderated score at cycle close, flagged "unmoderated"; unmoderated % is reported per team/BU alongside completion % (FR-068).
- Q: Does N<4 suppression need to defend against inference by differencing (e.g., BU minus team exposes a <4 complement)? → A: Yes — threshold plus complement suppression within the fixed hierarchy (FR-014, SC-006).
- Q: Is this feature spec scoped to Phase 1 MVP only, or Phases 1+2? → A: Phase 1 MVP only. User Story 8 (benchmarking) and other Phase 2/3 items are out of scope, deferred to future feature specs (see Out of Scope subsection; FR-036 and FR-042 tagged Phase 2).
- Q: Is an assessment cycle global per framework or per-BU? → A: One global monthly cycle per framework; a BU is in scope only once onboarded, and a BU onboarding mid-cycle joins at the next cycle open (FR-002).
- Q: Adopt the four recommended edge-case behaviours (leave status, manager departure, stage immutability, sub-4 BU display) as binding? → A: Yes, all four — converted to FR-069, FR-070, FR-028 (amended), FR-071.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Individual Self-Assessment (Priority: P1)

An employee invited to an assessment cycle completes their own maturity scores against seven SDLC dimensions, with optional free-text evidence per dimension. The form pre-populates with their prior-cycle scores (if any) to minimize effort on unchanged dimensions.

**Why this priority**: Self-assessment is the foundation of all rollups and reporting. Without it, the system cannot measure or trend maturity.

**Independent Test**: Individual can complete and submit a self-assessment in under 5 minutes (no change month) and see their submission confirmed. Can be tested with synthetic users in seeded roles.

**Acceptance Scenarios**:

1. **Given** an invited individual in an open cycle, **When** they open the assessment form, **Then** the form shows all seven dimensions with level descriptors (0–3) and optional evidence field, pre-populated with their prior-cycle scores if available.
2. **Given** an individual with filled self-scores, **When** they submit, **Then** the submission is recorded, marked as complete, and they receive a confirmation message.
3. **Given** an individual who returns to an open cycle after partial submission, **When** they reopen the form, **Then** their prior in-progress scores are restored.

---

### User Story 2 — Manager Review & Moderation (Priority: P1)

A manager reviews each direct report's self-assessment and records their own moderated score per dimension. The manager sees the self-score and evidence, and must comment if their score diverges ≥ 2 levels. The manager score becomes the official score for rollups; the individual sees both self and manager scores with divergence highlighted.

**Why this priority**: Manager moderation is the quality gate that prevents self-serving over-scoring and ensures calibration across the organization. Moderated scores feed all leadership reporting.

**Independent Test**: Manager can review and moderate self-assessments for a team of N individuals (N=3–10) in under 30 minutes. Individual receives visible feedback on divergence within 24 hours of moderation closure.

**Acceptance Scenarios**:

1. **Given** a manager with direct reports in an open cycle, **When** they open the Manager Review screen, **Then** they see a list of in-progress and completed self-assessments from their team, with completion status and time-to-moderate estimates.
2. **Given** a manager reviewing a self-score of "2" per dimension, **When** they enter a moderated score of "0", **Then** a comment field becomes mandatory (divergence ≥2), and they cannot submit without it.
3. **Given** a manager who enters moderated scores and submits, **When** the submission completes, **Then** the individual is notified and can view their moderated scores, comments, and divergence highlights within 1 hour.

---

### User Story 3 — BU Lead Viewing BU Maturity & Initiative Register (Priority: P1)

A BU Lead (EVP/Director) sees a dashboard showing their BU's moderated maturity per dimension (mean score + distribution of floor-based levels), alongside a modifiable initiative register where they can view, edit, and curate initiatives across their BU. The maturity data updates live as cycles close; the initiative register is the BU Lead's tool to tag initiatives to dimensions and ensure weekly owner updates.

**Why this priority**: BU Leads are the primary users of the application for internal management and have the duty to feed upward reporting (Harris submissions). Their need to reconcile maturity with active initiatives is a core design driver.

**Independent Test**: BU Lead can load their dashboard, review current cycle status, view all initiatives in their BU filtered by stage/category, and update an initiative's weekly status note in under 10 minutes.

**Acceptance Scenarios**:

1. **Given** a BU Lead at the end of a closed assessment cycle, **When** they open their BU Dashboard, **Then** they see the mean moderated score per dimension, the distribution of floor-based levels (e.g., "12% Level 2+"), and trend arrows showing movement from the prior cycle.
2. **Given** a BU Lead viewing their initiative register, **When** they filter by "Active (Evaluation–Scaled)", **Then** they see only initiatives in those stages with RAG status, last-update date, and a "Update Now" action for any overdue by >7 days.
3. **Given** an initiative with a stale weekly status (no update in 8+ days), **When** the BU Lead visits the register, **Then** the initiative is flagged as "Overdue Update" and the owner is listed for escalation.

---

### User Story 4 — Harris AI Dashboard Pre-Filled Submission Report (Priority: P1)

An EVP/BU Lead initiates a Harris submission report (weekly or monthly) and the system generates a pre-filled form matching the Harris AI Dashboard structure, populated from the initiative register, declared BU AI-DLC level, and measured maturity distribution. The EVP reviews the pre-filled data (ideally matches exactly), makes any corrections, and exports or transcribes to the Harris dashboard.

**Why this priority**: The Harris submission is the immediate business pain point this application solves. Pre-filling reduces EVP admin burden from 1–2 hours per submission to under 15 minutes review + transcribe. Stated as MVP requirement and Phase 1 deliverable.

**Independent Test**: EVP can generate a weekly Harris submission report, verify pre-filled initiative counts match the register's filter-by-stage+category view, and export a PDF in under 15 minutes. Counts reconcile line-by-line to underlying records.

**Acceptance Scenarios**:

1. **Given** an EVP opening the Harris Submission screen, **When** they select "Weekly" and a date, **Then** the system displays:
   - Pre-filled AI-DLC level (from weekly declaration) with evidence panel (measured maturity distribution)
   - Per-category counts: # in Ideation, Development, Production (broken by initiative AI-DLC level 1/2/3)
   - # unique customers in production per category
   - Retired initiatives counted at their final stage
2. **Given** pre-filled initiative counts, **When** the EVP reviews against the register filtered by mapped stage, **Then** the counts match exactly (100% reconciliation).
3. **Given** a completed Harris submission report, **When** the EVP exports to PDF, **Then** the PDF mirrors the Harris form layout and all numeric fields are editable for any last-minute corrections before transcription.

---

### User Story 5 — Group/Portfolio Leadership Viewing Consolidated Rollups (Priority: P2)

A Group Leader views all BUs in their group (or Portfolio Leader views all groups in their portfolio) with moderated maturity per dimension, initiative register filtered to their scope, and consolidated trend data. A HIG Executive (Group President, COO, Vanguard) holds a consolidated all-HIG view.

**Why this priority**: Group and portfolio leaders need visibility to understand progress and identify capability gaps across their span of control. This is non-blocking for MVP but essential for Phase 1 readiness.

**Independent Test**: Group Leader can load their dashboard (23 BUs × 7 dimensions) and filter/sort within 5 seconds. Can toggle to a specific BU's details.

**Acceptance Scenarios**:

1. **Given** a Group Leader at a dashboard, **When** they open the Heatmap view, **Then** they see all BUs in their group (rows) × dimensions (columns), colored by mean maturity score, with initiative counts overlaid.
2. **Given** a Group Leader with a portfolio of 3 groups (69 BUs), **When** a Portfolio Leader opens their consolidated view, **Then** they see rollups per group, with drill-down to BU level.
3. **Given** the HIG Executive, **When** they open the application, **Then** they see an all-HIG heatmap, all portfolios, and the full initiative register across all BUs.

---

### User Story 6 — Initiative Owner Weekly Status Update (Priority: P2)

An initiative owner (or the BU Lead on their behalf) updates an active initiative's weekly status: RAG status and a one-line note, designed to take <1 minute. If the initiative is overdue (no update in 7 days), the system sends an email reminder to the owner and an escalation to the BU Lead after 14 days overdue.

**Why this priority**: Weekly updates keep the register fresh for leadership reporting. Stale entries undermine data quality and reporting trust. P2 because the MVP ships with the register structure; enforcement and reminders follow.

**Independent Test**: Initiative owner can open a draft status note, update RAG and note, and submit in <2 minutes. Overdue initiative triggers an email within 1 hour of the 7-day mark.

**Acceptance Scenarios**:

1. **Given** an active initiative and today's date 7+ days since last update, **When** the register nags the owner via email, **Then** the email includes a deep link to the initiative's update form.
2. **Given** an owner who submits an update, **When** the submission completes, **Then** the "last updated" timestamp is recorded and the "Overdue" flag is cleared.
3. **Given** an initiative overdue by 14+ days, **When** the escalation runs, **Then** the BU Lead receives an escalation email listing all overdue initiatives and owners.

---

### User Story 7 — Monthly BU Metrics Form (Priority: P2)

An EVP/BU Lead completes a lightweight monthly form with Support (internal and customer) metrics and API/System of Record usage, pre-populated from prior month where applicable. Data feeds into the Harris submission's financial and support sections.

**Why this priority**: These metrics are required for the Harris monthly submission but don't block MVP if a fallback manual-entry approach is used for Cycle 1. P2 because the form structure is simple; integration with Harris submission is P1.

**Independent Test**: EVP can open the form, verify prior-month carry-forward, edit the fields, and submit in under 5 minutes.

**Acceptance Scenarios**:

1. **Given** a monthly metrics form opened in month N, **When** the EVP views the form, **Then** month N-1 YTD values are pre-populated and editable; current-month SOR usage starts at 0.
2. **Given** completed monthly metrics, **When** a Harris submission is generated, **Then** the submission's Support and SOR sections are pre-filled from the latest monthly metrics.

---

### User Story 8 — Benchmarking & League Tables — OUT OF SCOPE (Phase 2)

Deferred per clarification 2026-07-21: benchmarking league tables and comparison views are a Phase 2 feature (root spec §7) and will be specified in a future feature spec. Story number retained to keep references stable; no requirements in this spec implement it.

---

### User Story 9 — Org Sync & BU Onboarding (Priority: P1)

Platform Admin configures a nightly sync from Entra ID/Graph (or HRIS export), pulling person, manager, job title, BU attribute, and employee type. BU registration happens via admin workflow: assign BU to group/portfolio, select applicable framework(s), and configure contractor exclusion. The system generates the org hierarchy and automatically invites in-scope individuals when cycles open.

**Why this priority**: Org structure is the seam that enables role-based visibility and automatic participant invitation. Without it, every cycle requires manual list entry. Essential for scale to 23 BUs.

**Independent Test**: Admin can configure org sync, run an initial sync, and verify the person/manager/BU hierarchy is correctly imported and visible in the system within 30 minutes (using test data or sandbox).

**Acceptance Scenarios**:

1. **Given** a Entra ID sync configured, **When** the nightly sync runs, **Then** new people are created, manager relationships are updated, and BU assignments are synced with no data loss or orphaned records.
2. **Given** a BU with 100 people (80 employees + 20 contractors), **When** a cycle opens with contractor exclusion enabled, **Then** invitations are sent to exactly 80 people; the 20 contractors are marked "Excluded" and do not receive invitations.
3. **Given** a person who changes manager mid-cycle, **When** the next sync runs, **Then** the manager relationship is updated, and the new manager can view the individual's in-progress assessment in their next review period.

---

### Edge Cases

- Person on leave or detached from a manager during a cycle → resolved (FR-069): still invited if registered; shown with "leave" status in the manager's review list; manager linkage restored by subsequent directory syncs.
- Manager leaves HIG mid-cycle → resolved (FR-070): already-moderated scores stand; pending reviews escalate to the departing manager's manager.
- Retroactive initiative stage change (e.g., "Production" → "Evaluation") → resolved (FR-028): stage history is immutable and forward-only; Harris submissions read the stage held at submission time.
- BU or team smaller than 4 people → resolved (FR-071): individual-level views unaffected; aggregates display as "Suppressed"; historical aggregates that met N≥4 at the time remain visible.
- What if the Harris form structure changes during a cycle? (Recommend: submission reports are tied to a framework version; new form structure becomes available in the next cycle's submission generation.)

### Out of Scope — Deferred to Phase 2/3 Feature Specs

This spec covers **Phase 1 MVP only** (root spec §7). Explicitly out of scope here, to be specified later via their own `/specify` pass:

- **Phase 2**: benchmarking league tables and group heatmap presentation (US8; FR-042), level-up playbooks, showcase views, duplication/consolidation view (FR-036), idea-intake pipeline, governance/value field enforcement, stale-entry nudge tuning, digest emails, deeper trend/calibration/distribution analytics views. (The underlying data these views need — moderated scores per cycle, calibration deltas, stage history — **is** captured in Phase 1.)
- **Phase 3**: usage telemetry integrations, champions network / enablement tracking, non-engineering frameworks, Power BI schema, Cogito demand-planning API, direct Harris dashboard submission via API.

---

## Requirements *(mandatory)*

### Functional Requirements

**Module 1: Assessment Framework & Cycles**

- **FR-001**: System MUST support multiple frameworks, each with versions (immutable once a cycle uses them), dimensions (7 per v1 SDLC framework), and level descriptors (0–3 per dimension). Framework content is data, never code: the v1 framework ("AI Maturity in the SDLC") is seeded from `docs/frameworks/ai-maturity-sdlc.v1.json` (authoritative content: root spec Appendix A).
- **FR-002**: System MUST manage assessment cycles with open/close dates, tied to a framework version. A cycle is **global per framework**: one cycle open at a time per framework, spanning all onboarded BUs mapped to it; a BU onboarding mid-cycle joins at the next cycle open. Cycles MUST lock at close; late submissions require manager or admin override.
- **FR-003**: System MUST auto-generate invitations for all in-scope individuals (from org sync + BU/framework mapping) when a cycle opens.
- **FR-004**: System MUST enforce mandatory participation: individuals in registered BUs cannot opt out of an open cycle.
- **FR-005**: System MUST support contractor exclusion (configurable per cycle): contractors receive no invitations and do not count toward headcount/completion %.
- **FR-006**: System MUST persist contractor status per individual from directory sync (employee type attribute); manual override layer available for corrections with audit trail.
- **FR-060**: Assessment cycles MUST run on a monthly cadence, each running the full self + manager review flow. (Cadence keeps trend data fresh; the pre-population and carry-forward defaults in FR-062/FR-063 keep the burden proportionate.)
- **FR-061**: System MUST send automated reminders to non-responders and escalation summaries to managers and the BU Lead as the cycle close date approaches, and MUST provide live completion dashboards per team/BU/group throughout the open cycle.
- **FR-069**: A person on leave or temporarily detached from a manager during a cycle MUST still be invited (if in a registered BU), MUST appear in the manager's review list with a "leave" status, and MUST have manager linkage restored automatically by subsequent directory syncs.

**Module 1: Assessment Scoring Workflow**

- **FR-007**: System MUST allow individuals to score themselves 0–3 per dimension with optional free-text evidence, and submit self-assessment.
- **FR-008**: System MUST allow managers to view direct-report self-assessments and record moderated scores 0–3 per dimension.
- **FR-009**: System MUST require a manager comment where moderated score diverges ≥2 levels from self-score.
- **FR-010**: System MUST use manager (moderated) score as the official score for all rollups and reporting.
- **FR-011**: System MUST retain self-scores and surface calibration delta (mean |self − manager| per dimension/team/BU) for analysis.
- **FR-012**: System MUST show individuals their moderated scores, comments, and divergence highlights after manager review completes.
- **FR-062**: The self-assessment form MUST pre-populate with the individual's previous cycle scores (where they exist), so a "no change" month takes seconds to confirm.
- **FR-063**: Manager review MUST default to carrying forward the prior cycle's moderated score per dimension unless the individual's self-score for that dimension changed.
- **FR-068**: If manager review is incomplete when the cycle closes, the system MUST auto-adopt the individual's self-score as the moderated score, flagged "unmoderated". Unmoderated assessments remain in all rollups; the unmoderated % MUST be reported per team/BU alongside completion %, and unmoderated scores MUST be excluded from calibration-delta metrics (their delta is definitionally zero).
- **FR-070**: When a manager leaves HIG mid-cycle, their already-moderated scores MUST stand; their pending reviews MUST escalate to the departing manager's manager (who becomes the reviewer of record for those assessments).

**Module 1: Rollups & Analytics**

- **FR-013**: System MUST compute rollups from moderated scores: Individual → Team → BU → Group → Portfolio.
- **FR-014**: System MUST apply small-group suppression: suppress any aggregate covering N<4 individuals, **and** suppress (or coarsen) an aggregate when publishing it alongside its parent/sibling aggregates in the fixed hierarchy would expose a complement group of N<4 by differencing (e.g., a BU of 7 with one team of 4, or a BU containing a single team). v1 publishes only fixed hierarchy rollups — no ad-hoc slicing — so the complement check is evaluated within the hierarchy tree.
- **FR-071**: Suppressed aggregates MUST display as "Suppressed" (never as zero or blank); individual-level views permitted by the management chain are unaffected by suppression; historical aggregates that met N≥4 at the time they were computed remain visible even if the group later shrinks below 4.
- **FR-015**: System MUST compute and report mean score per dimension (continuous 0–3) for trend analysis and rollup averages.
- **FR-016**: System MUST compute and report floor-based level labels: an individual/team's level = minimum dimension score (only level 2 when all dimensions ≥2).
- **FR-017**: System MUST track trend over cycles: chart BU, group, portfolio maturity trajectory per dimension.
- **FR-018**: System MUST provide distribution views (histograms of levels per dimension) to surface bimodal teams.
- **FR-019**: System MUST report completion and participation % per cycle, trended over time.

**Module 1: Organization Structure**

- **FR-020**: System MUST sync from Entra ID / Graph (or HRIS export) nightly: person, email, manager, job title, BU attribute, employee type.
- **FR-021**: System MUST model the Harris org hierarchy: Person → Team (manager) → BU → Group → Portfolio.
- **FR-022**: System MUST derive leadership role visibility automatically from the org hierarchy: EVP sees their BU; Group Leader sees all BUs in their group; Portfolio Leader sees all groups in their portfolio; HIG Executive sees all-HIG.
- **FR-023**: System MUST provide a manual override layer (BU assignment, dotted lines) with audit trail for directory corrections.
- **FR-024**: System MUST deactivate leavers (not delete); their historic scores remain in aggregates for cycles they participated in.
- **FR-025**: System MUST respect authorization layer: individual-level assessment data is readable only by the individual, their manager, and those above them in the management chain. Leaders above BU level see aggregates only.

**Module 2: Initiative Register**

- **FR-026**: System MUST support initiative entity with: Name, BU, sponsor (exec), owner (delivery lead), description, date registered.
- **FR-027**: System MUST support initiative classification: category aligned to the Harris AI Dashboard taxonomy (AI Product Feature / Pre-Built Agent deployed at customers / Custom Agent deployed at customers / Digital Worker internal to Harris / Other — internal-only, **not group-reported**), function(s), dimensions advanced (multi-select), AI-DLC level (1–3).
- **FR-028**: System MUST support initiative lifecycle: stage (Idea → Evaluation → Pilot → Production → Scaled → Retired) with stage history and dates. Stage history is **immutable and forward-only** — no retroactive edits; corrections are made by a new forward transition. Harris submissions read the stage held at submission time (and, for retired initiatives, the stage held when retired, per FR-064).
- **FR-029**: System MUST support value capture: value hypothesis, NR capture (Direct/Indirect, One-Time/Recurring, $USD with descriptions), measured value to date, effort/cost band.
- **FR-030**: System MUST support governance & risk: data sensitivity, regulatory relevance, risk tier, approval status, human-oversight model.
- **FR-031**: System MUST track # unique customers in production (for customer-deployed categories).
- **FR-032**: System MUST support technology tagging: models/providers (e.g., Claude, Azure OpenAI), vendors/tools, Cogito consumption flag.
- **FR-033**: System MUST support initiative status: RAG status, last update date, weekly owner status note (lightweight, <1 min entry).

**Module 2: Register Behaviors & Reporting**

- **FR-034**: System MUST allow Manager+ to create initiatives; BU Lead can edit/curate all entries in their BU. No approval workflow in v1.
- **FR-035**: System MUST support full-text search and faceted filtering (BU, stage, category, risk tier, model/vendor, dimension) on the initiative register.
- **FR-036** *(Phase 2 — deferred)*: System MUST provide a duplication/consolidation view: initiatives grouped by category/technology across BUs to identify independent builds of the same capability. (Data model support — category/technology tagging — ships in Phase 1 via FR-027/FR-032; the view is Phase 2.)
- **FR-037**: System MUST enforce weekly update discipline for active initiatives (Evaluation → Scaled): nag owners at 7 days; escalate to BU Lead at 14 days overdue.
- **FR-038**: System MUST provide data-quality scoring per BU: update timeliness (% current) + completeness of value/governance fields.
- **FR-039**: System MUST support CSV/Excel export of initiative register.
- **FR-040**: System MUST provide a read API for downstream reporting and integration.

**Module 2: Cross-Module Integration**

- **FR-041**: System MUST provide BU scorecards: maturity per dimension alongside initiative counts/stages per dimension.
- **FR-042** *(Phase 2 — deferred)*: System MUST provide group heatmap: 23 BUs × 7 dimensions, colored by mean maturity, with initiative counts overlaid. (Phase 1 ships tabular rollup views; the heatmap presentation is Phase 2.)

**Reporting & Harris Submissions**

- **FR-043**: System MUST generate Harris AI Dashboard pre-filled submission reports (weekly + monthly) per BU, matching the Harris form structure exactly.
- **FR-044**: Weekly submission MUST include: AI-DLC level (from weekly declaration) + evidence, per-category initiative counts (Ideation/Development/Production by initiative AI-DLC level 1/2/3), unique customers per category, and Ideas Tried but Stopped (retired initiatives by the stage held when retired, and level). Initiatives in category "Other (internal-only)" MUST be excluded from all group-reported counts.
- **FR-064**: Harris submission generation MUST apply the Harris stage mapping: Idea + Evaluation → *Ideation*; Pilot → *Development*; Production + Scaled → *Production*; Retired → *Ideas Tried but Stopped*, counted at the stage held when retired (taken from the immutable stage history). The mapping is configuration data, never hard-coded logic.
- **FR-065**: System MUST compute divergence between the declared BU AI-DLC level (weekly declaration) and the measured maturity distribution, and MUST make that divergence reportable to group leadership.
- **FR-045**: Monthly submission MUST include: per-category NR (Direct/Indirect, One-Time/Recurring, $USD, YTD), Support metrics (internal/customer), API/SOR usage.
- **FR-046**: System MUST pre-fill Harris submission fields from: initiative register + weekly declaration (weekly report); register financials + monthly BU metrics (monthly report). Submission counts MUST reconcile 100% to underlying records.
- **FR-047**: System MUST support EVP declaration of weekly BU AI-DLC level (0–3) with evidence panel showing measured maturity distribution and trend.
- **FR-048**: System MUST support monthly BU metrics form: Support metrics, SOR usage, with prior-month YTD auto-carry for editing.
- **FR-049**: System MUST generate PDF exports of Harris submission reports, mirroring the form layout.

**Data Management & Audit**

- **FR-050**: System MUST maintain immutable audit trail: score changes, role grants, org overrides, individual-data views (who viewed, when, which individual's data).
- **FR-051**: System MUST support GDPR right-of-access: export all personal data (assessments, scores, comments) for a given individual.
- **FR-052**: System MUST support GDPR retention policy: raw individual scores retained 3 years; aggregates retained indefinitely.
- **FR-053**: System MUST never allow audit rows to be updated or deleted.
- **FR-054**: System MUST track framework version and hold assessment data to the version used at scoring time (immutability once cycle closes).
- **FR-066**: The application MUST state its purpose limitation explicitly in-app — assessment data is collected for development purposes, not performance management — displayed where individuals complete assessments and where managers moderate, to drive honest self-assessment.

**Infrastructure & Notifications**

- **FR-055**: System MUST authenticate via the identity provider port (`IIdentityProvider`). The local build ships the dev provider (seeded synthetic users covering all seven roles, selectable at sign-in); the production adapter is Entra ID SSO (OIDC), a deferred decision-recorded story. No local password accounts exist in production.
- **FR-056**: System MUST derive role assignment from directory groups plus in-app grants for Group Viewer/Admin.
- **FR-057**: System MUST send email notifications (no Teams) for: cycle open, reminders, assessment completion, moderation completion, overdue initiatives, escalations, monthly digest.
- **FR-058**: System MUST support daily backup with tested restore.
- **FR-059**: System MUST achieve 99.5% availability during business hours.
- **FR-067**: v1 is English-only, but all user-facing strings MUST be externalised from the start (no hard-coded UI copy) — HIG BUs span multiple countries and later localisation must not require restructuring.
- **FR-072** *(added 2026-07-21, DR-0003)*: System MUST provide in-app contextual help for the mandated flows (self-assessment, manager moderation, initiative register, BU declarations/metrics, Harris submission review). Help content is versioned seeded data — never hard-coded copy — and each flow's help is authored alongside the story that ships the flow.
- **FR-073** *(added 2026-07-21, DR-0003)*: A printable end-user guide MUST be maintained at `docs/user-guide/`, covering the same flows and kept consistent with the in-app help; it is updated whenever user-facing behaviour changes.

### Key Entities

- **Framework**: Version immutable once cycle uses it; contains dimensions + level descriptors. Versioned so historic assessments remain tied to their scoring framework.
- **FrameworkVersion**: Immutable snapshot of dimensions and descriptors; linked to all assessments scored against it.
- **Dimension**: 7 per v1 SDLC framework (How AI is Leveraged, Examples/Usage, Work Unit, SDLC Process, Timing, Value Measured, Impact). Reusable across frameworks.
- **LevelDescriptor**: Per dimension, per level (0–3); shown inline during scoring.
- **Cycle**: Tied to a framework version; open/close dates; tracks participation and completion.
- **Assessment**: Individual's self-scores + evidence per cycle.
- **AssessmentScore**: Moderated score per dimension per assessment; source of truth for rollups. Retains self-score for calibration delta.
- **Initiative**: Register entry with identity, classification, lifecycle, value, governance, status, weekly notes.
- **InitiativeNRLine**: Per-initiative NR capture: category, Direct/Indirect, One-Time/Recurring, $USD, description.
- **Person**: Employee or contractor; synced from directory; links to manager and BU.
- **Team**: Group of people under one manager.
- **BusinessUnit**: One of ~23 BUs; registered with framework(s), group, portfolio, directory source.
- **Group**: Collection of BUs; part of a portfolio.
- **Portfolio**: Collection of groups; reporting entity.
- **OrganisationOverride**: Manual corrections to directory-synced relationships (BU assignment, dotted lines) with audit trail.
- **AuditLog**: Immutable log of score changes, role grants, org overrides, individual-data views. Never updated or deleted.
- **BUAIDLCDeclaration**: Weekly declaration of BU AI-DLC level (0–3) with RAG status and evidence (measured maturity distribution).
- **BUMonthlyMetrics**: Monthly entry for Support and SOR metrics; YTD carry-forward.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Phase 1 MVP (org sync, SSO, framework engine, cycle management, self+manager assessment, rollups, initiative register, Harris submission reports, weekly declaration + monthly metrics forms) is live and supporting HHA Cycle 1 within 2 weeks of final sign-off (calendar governed by directory audit, DPIA, and onboarding readiness, not code production).
- **SC-002**: All seven SDLC dimensions are assessable per individual; mean and floor-based level can be computed for any individual, team, BU, group, or portfolio. Trend lines available for rollups across ≥2 cycles.
- **SC-003**: HHA Cycle 1 baseline: ≥80% of eligible employees (non-contractors) complete both self-assessment and manager review within the cycle window (target: 1 month open).
- **SC-004**: Harris submission pre-filled data reconciles 100% to underlying register + declaration records. No line-item in the submission contains a discrepancy > 0% (exact match required).
- **SC-005**: All individual-level assessment data access is logged; zero reads occur outside the management chain (verified via access-control audit for all 7 seeded roles: Individual, Manager, BU Lead, Group Leader, Portfolio Leader, HIG Executive, Platform Admin).
- **SC-006**: Small-group suppression (N<4) is enforced: no aggregate or trend view reveals individual-level scores when N<4, **including by differencing** published aggregates against their parent/sibling aggregates. Spot-checks: (a) attempt to infer an individual's score from aggregates covering N=3 people; (b) attempt to derive a <4 complement by subtracting a team aggregate from its BU aggregate → both must be impossible.
- **SC-007**: Assessment flow is WCAG 2.2 AA compliant: every Individual and Manager user can complete their workflow using keyboard navigation, screen readers, and high-contrast modes without loss of functionality.
- **SC-008**: System supports ≥23 BUs, ≥2,000 teams (a team = a manager's direct reports, typically 5–10 people), ≥10,000 people (estimated 300–800 per BU), and can compute all rollups and generate Harris submissions in <30 seconds per BU.
- **SC-009**: Org sync (Entra ID / HRIS) completes nightly in <30 minutes; no sync failures cause data loss or orphaned records.
- **SC-010**: Email notifications (cycle open, reminders, completion, escalations) are delivered within 1 hour of the event; no >24 hour delays observed over a full cycle.
- **SC-011**: BU data-quality score (update timeliness + completeness) is visible to Group Leadership; Cycle 1 end: average BU completeness ≥80% (value + governance fields populated).
- **SC-012**: Harris submission weekly/monthly reports can be exported to PDF in <2 minutes; PDF renders correctly and matches on-screen form layout 100%.

---

## Assumptions

- **Org Structure**: Entra ID or HRIS export is available and includes BU, Group, Portfolio, employee type, and manager attributes. We assume inconsistency across BU tenants (directory hygiene audit required before rollout; question recorded in Open Questions).
- **Email Delivery**: SMTP relay or Azure Communication Services is available for notifications. Assume <5% delivery failures and retry logic is built-in.
- **Stack & Hosting**: .NET 8 Web API, React/TypeScript frontend, PostgreSQL database, docker-compose for local development, Azure for production (App Service / AKS). Identity provider is abstracted (IIdentityProvider interface); local dev provider ships with synthetic users covering all 7 roles; Entra ID OIDC adapter is a deferred decision-recorded story.
- **Synthetic Data Only**: All local-build data (people, orgs, assessments) comes from deterministic generators in `scripts/synth/`. No real employee data is ever present during development. Real-data unlock happens at deployment (post-Gate G1) outside the repo.
- **Framework Data**: v1 SDLC framework (7 dimensions, 4 levels) is seeded from the current spreadsheet. Non-engineering frameworks deferred to Phase 3.
- **Harris Form Stability**: Harris AI Dashboard form structure and stage mappings (Pilot → Development; Production + Scaled → Production) are assumed stable through Phase 1. Changes captured as decision records.
- **No Approval Workflow v1**: Initiative register is record-keeping only; no in-app approval gates. Actual approvals remain in external processes. This simplifies v1 scope but is revisited in Phase 2 if needed.
- **Single Assessment Per Person Per Cycle**: Each person has one assessment per cycle per framework. No support for parallel assessments or re-assessments within a cycle (late overrides exist but are admin-level).
- **Contractors Are Excluded**: Directory sync provides employee type; contractors (contractors, vendors) are identified and excluded from participation. This policy is configurable per cycle.
- **Leadership Visibility is Hierarchical**: Users see data for their direct reports and all people/BUs below them in the hierarchy. No per-request access control; roles derive scope automatically from org structure.
- **No Performance Management Use**: The application explicitly does NOT support performance management, pay decisions, or talent management. Purpose is development, not evaluation. This is stated in-app and in communications to drive honest self-assessment.

---

## Open Questions

Tracked in `docs/decisions/QUESTIONS.md` (Q-001..Q-003) per CLAUDE.md §6.3; summarised here. None block local build work.

[QA-001] **Directory Attributes**: Which AD/Entra attributes reliably carry the group/portfolio mapping and employee type across all 23 BUs? Directory hygiene audit needed before rollout; expect inconsistency between BU tenants/domains.

[QA-002] **Harris Form Semantics**: Confirm the Harris AI Dashboard stage mapping (Pilot → Development; Production + Scaled → Production) and the definition of an initiative's "AI-DLC level" (interpreted as the autonomy level the initiative embodies, 1–3; confirm if this aligns with Harris expectations).

[QA-003] **Harris API Roadmap**: Does the Harris dashboard team plan an ingestion API? If yes, direct submission replaces the transcribe step (Phase 3). If no, pre-filled PDF export remains the delivery mechanism.

---

## Design References

Binding UI references for the user stories in this spec: design system `docs/design/DESIGN.md` and screen mockups `docs/design/mockups/` (see its `index.md` for the screen ↔ spec-area map). Layout, information architecture, and the non-happy states shown (suppressed aggregates, divergence, overdue updates, incomplete assessments) are binding; exact pixels are not. Note: `heatmap-group.html` mocks the Phase 2 heatmap/league table (FR-042, out of scope here) — it stands as forward reference only.

## Dependencies & Constraints

- **Governance**: This feature is a signatory component of the HIG AI Adoption Platform and implements Article VI of the constitution (Privacy and Reporting are the Safeguarding Seam). All code touching authorization, visibility, and Harris submission generation is L3 (requiring code-reviewer + domain specialist + red-team review per CLAUDE.md §7).
- **Database**: Assessments and scores are UK GDPR personal data. DPIA required before group rollout. Retention policy (3 years raw, indefinite aggregates) must be documented and enforced at schema/migration level.
- **Compliance**: Accessibility (WCAG 2.2 AA on assessment flow), audit integrity (immutable logs), and data exfiltration prevention (no reads outside management chain) are acceptance criteria, not polish.
