# Data Model — Phase 1

Entities mapped from spec Key Entities + FRs. PostgreSQL via EF Core, forward-only migrations. Conventions: surrogate `id` PKs, `created_at` UTC on all tables; soft temporal fields explicit below. **Bold** = enforced invariant.

## Org & identity

### Person
- id, external_ref (directory key), display_name, email, job_title
- manager_person_id (nullable — manager gaps exist in synth data)
- business_unit_id, employee_type (`Employee` | `Contractor`), is_active (leavers: **deactivated, never deleted**; FR-024)
- on_leave (bool; FR-069)
- *Team is derived*: a team = a manager and their active direct reports (no Team table; FR-021's "Team" materialises in rollup snapshots via manager_person_id)

### BusinessUnit
- id, name, code, group_id, is_onboarded (drives cycle scope; FR-002), directory_source (informational)

### GroupOrg
- id, name, portfolio_id

### Portfolio
- id, name

### OrgOverride (FR-023)
- id, person_id, field (`BusinessUnit` | `Manager` | `DottedLine`), original_value, override_value, reason, created_by, created_at
- **Every write also writes an AuditLog row**; overrides survive re-sync (applied after import)

### RoleGrant (FR-056)
- id, person_id, role (`PlatformAdmin` | `HigExecutive` | `BuDelegate(bu_id)` | explicit viewer grants), granted_by, created_at
- `BuDelegate` authorises declaration/monthly-metrics submission for one BU (FR-047, audit 2026-07-21); every grant writes a `RoleGrant` audit row (FR-050)
- Hierarchy-derived roles (Manager, BU Lead, Group Leader, Portfolio Leader) are **computed from org structure, never stored**

## Framework (data, not code — FR-001)

### Framework
- id, key, name, description, owner

### FrameworkVersion
- id, framework_id, version_number, status (`Draft` | `Active` | `Retired`), source_ref (e.g. `docs/frameworks/ai-maturity-sdlc.v1.json`)
- **Immutable once any Cycle references it** (FR-054; guarded in domain + DB trigger-free check on write path)

### Dimension
- id, framework_version_id, key, name, display_order

### LevelDescriptor
- id, dimension_id, level (0–3), level_name, descriptor_text
- `level_name` (HAP-6, panel round-1 advisory) is the framework-wide label for `level` (e.g. the
  top autonomy level's name), denormalised onto every descriptor row rather than modelled as a
  separate table — the value only ever repeats across the (typically 7) dimensions of one
  version, and the source JSON's `levels` array has no other home in this schema.

## Cycles & assessment

### Cycle
- id, framework_version_id, name (e.g. "2026-08"), opens_at, closes_at
- state: `Draft → Open → Closed` (**forward-only; one Open cycle per framework** — FR-002/FR-060)
- contractor_exclusion_enabled (default true; FR-005)

### CycleInvitation
- id, cycle_id, person_id, invited_at, excluded (bool) + excluded_reason (`Contractor` | `NotOnboarded`)
- **Set derived at cycle open** from org + BU onboarding + framework mapping (FR-003)

### Assessment (one per person per cycle — spec assumption)
- id, cycle_id, person_id, state: `NotStarted → InProgress → Submitted → Moderated | AutoAdopted`
- submitted_at, moderated_at, moderated_by_person_id (may differ from line manager after FR-070 escalation)
- unmoderated (bool — set true by cycle close auto-adoption; FR-068)
- **Access exclusively via the seam** (`Hap.Api/Authorization`) — architecture-test enforced

### AssessmentScore (one per assessment per dimension)
- id, assessment_id, dimension_id
- self_score (0–3), self_evidence (text, optional)
- manager_score (0–3, nullable until moderated), manager_comment
- **manager_comment required when |self − manager| ≥ 2** (FR-009; domain-enforced)
- Moderated score of record = manager_score (or self_score copied on auto-adoption, FR-068)

### RollupSnapshot (written at cycle close — research D4)
- id, cycle_id, org_node_type (`Team` | `BU` | `Group` | `Portfolio` | `AllHig`), org_node_ref
- n, per-dimension mean (jsonb keyed by dimension key), floor_level_distribution (jsonb), completion_pct, unmoderated_pct, calibration_delta (jsonb; **excludes auto-adopted**, FR-068)
- suppressed (bool) + suppression_reason (`N<4` | `Complement`) — **verdict frozen at close** (FR-071, research D2)

## Initiative register

### Initiative
- id, business_unit_id, name, description, sponsor_person_id, owner_person_id, registered_at
- category_id → HarrisCategory, ai_dlc_level (1–3; FR-027), functions_affected (text[])
- current_stage (denormalised from history), rag_status, last_update_at
- customers_in_production (int, customer-deployed categories; FR-031)
- models_providers (text[]), vendors_tools (text[]), uses_cogito (bool) (FR-032)
- value_hypothesis, measured_value, measurement_method, effort_cost_band (FR-029)
- data_sensitivity (`None|Internal|PII|PHI|Clinical`), regulatory_relevance (text[]), risk_tier (`Low|Med|High`), approval_status, approver, oversight_model, governance_notes (FR-030 — informational only)

### InitiativeStageHistory (FR-028)
- id, initiative_id, stage (`Idea|Evaluation|Pilot|Production|Scaled|Retired`), entered_at, entered_by
- **Append-only, forward-only; no UPDATE/DELETE mapped.** Retired rows capture prior_stage (stage held when retired — feeds FR-064)

### InitiativeWeeklyUpdate (FR-033, FR-037)
- id, initiative_id, rag_status, note, created_by, created_at
- Overdue = active stage (Evaluation→Scaled) and no update in 7 days (computed, not stored)

### InitiativeNRLine (FR-029/FR-045)
- id, initiative_id, year, direction (`Direct` | `Indirect`), recurrence (`OneTime` | `Recurring`), amount_usd, description

### HarrisCategory + HarrisStageMap (seeded data — FR-027/FR-064)
- HarrisCategory: id, key, name, group_reported (bool — **false for "Other"**, drives FR-044 exclusion), customer_deployed (bool)
- HarrisStageMap: id, internal_stage, harris_stage (`Ideation|Development|Production|IdeasTriedButStopped`) — **configuration rows, never code**

## Harris reporting

### BUAIDLCDeclaration (FR-047; weekly)
- id, business_unit_id, week_of, declared_level (0–3), next_level_expected_date, rag_status, note, declared_by, created_at

### BUMonthlyMetrics (FR-048)
- id, business_unit_id, month, support_internal (jsonb: time_savings_pct, fewer_people, support_ratio_impact), support_customer (jsonb: customers_ytd, tickets_ytd, resolved_by_ai_ytd, ai_assisted_ytd), sor_called_by_other_apps (current-month only), submitted_by, created_at

### HarrisSubmission (+ HarrisSubmissionLine) (FR-043–FR-046; research D5)
- HarrisSubmission: id, business_unit_id, kind (`Weekly` | `Monthly`), as_of, generated_at, generated_by, declared_vs_measured_divergence (jsonb; FR-065)
- HarrisSubmissionLine: id, submission_id, section, line_key, value, source_query_ref
- **Persisted at generation; each line reconciles to an independent query** (PrivacyReporting suite)

## Audit & GDPR

### AuditLog (FR-050, FR-053)
- id, at, actor_person_id, action (`IndividualView` | `ScoreChange` | `RoleGrant` | `OrgOverride` | `Export` | `RetentionErasure`), subject_person_id (nullable), detail (jsonb)
- **Append-only: no UPDATE/DELETE mapped in EF; architecture test asserts no mutation path; audited actions fail closed** (if the audit write fails, the read fails)

### Retention (FR-052)
- No table — a retention job erases raw AssessmentScore self/manager values > 3 years old (aggregates in RollupSnapshot persist indefinitely), writing `RetentionErasure` audit rows. Right-of-access export (FR-051) reads via the seam.

## Key state machines

- **Cycle**: Draft → Open → Closed. Close triggers: auto-adoption (FR-068), rollup snapshotting + suppression verdicts, calibration deltas.
- **Assessment**: NotStarted → InProgress → Submitted → (Moderated | AutoAdopted). A late override (manager or admin) reopens moderation post-close: a **Submitted** report → Moderated, and — since close has by then auto-adopted (FR-068) — an **AutoAdopted** report may also be re-moderated → Moderated (clearing the unmoderated flag). See Q-022. (Moderated is terminal; the frozen close-time RollupSnapshot is unaffected — reconciliation is "as of close", Q-022 addendum.)
- **Initiative stage**: Idea → Evaluation → Pilot → Production → Scaled → Retired, forward-only; Retired terminal.

## Scale assumptions
23 BUs / 6 groups / 3 portfolios; ≥10,000 people; ≥2,000 derived teams; monthly cycles → ~120k AssessmentScore rows/cycle-year at full rollout. Comfortably Postgres-trivial; indexes on (cycle_id, person_id), (assessment_id, dimension_id), (business_unit_id, current_stage), audit (subject_person_id, at).
