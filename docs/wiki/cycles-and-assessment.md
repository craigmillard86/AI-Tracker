# Cycles & assessment — as built

_Subsystem shipped incrementally by HAP-7 (cycle state machine) and extended by HAP-8..HAP-10 (scoring, moderation, close). Describes shipped behaviour only; WHAT/WHY live in `docs/spec/` + `specs/`, decisions in `docs/decisions/`._

## Self-assessment (HAP-8 — FR-007/062/066)

The first real assessment data. `Assessment` (per cycle+person, states InProgress → Submitted, forward-only; carries the `unmoderated` flag for HAP-10) and `AssessmentScore` (one row per assessment per dimension, 0–3) live in `Hap.Domain/Assessments`. Migration #4 registers them **without a public DbSet** — the seam's `SeamAssessmentStore` (`db.Set<Assessment>()`) is the ONLY code that touches the tables, enforced by the boundary guard extended to the DbSet form plus a reflection guard pinning `SeamAssessmentStore` as the sole implementer of `IAssessmentStore`/`ISelfAssessmentStore`.

### Endpoints (`Hap.Api/AssessmentEndpoints.cs`, `SelfAssessmentService`)

`GET /api/me/assessment`, `PUT …/scores`, `POST …/submit` — **self-scope only: the subject is always the session caller** (`person_id` claim); no route/body/query/header carries a person id, so cross-person access is structurally impossible. Each call, in order: resolve the current cycle → **invitation gate** → submission lock → dimension validation → store.

- **GET** returns the 7 dimensions + descriptors from framework data (never hard-coded), prior-cycle scores pre-populated (FR-062), the FR-066 purpose-limitation copy key, and an `Editable` flag.
- **PUT** upserts partial progress (0–3 per dimension); reopening restores in-progress values.
- **Submit** transitions InProgress → Submitted; writes after submit → 409.
- Self views are **not audited** (per contract); the data path is still seam-only.

### Participation & integrity gates

- **Invitation gate (FR-002/004/005, US1 precondition):** every self operation requires a non-excluded `CycleInvitation` for the resolved cycle. A contractor (excluded) or a not-onboarded-BU person gets **404** on read and both write paths and lands **no row** — participation exclusion is held at the write, the cheapest place. *(Defense-in-depth carry-forward: rollup/Harris queries in HAP-10/11/19 must still inner-join `cycle_invitations WHERE Excluded=false`.)*
- **Dimension membership:** a `DimensionId` not in the resolved cycle's framework version (or `Guid.Empty`, or a duplicate in one payload) is rejected **422 before any write** — no phantom score, no FK-500.
- **Submission lock (Q-017a):** both the score-write and submit paths consult `Cycle.AllowsSubmission` — post-close write → **423** unless a late override exists. "Current cycle" resolves Open, else the most-recently-Closed (the override window); Draft is never current.

Status codes: 423 locked · 409 already-submitted / no-cycle-on-write · 422 incomplete / out-of-range / bad-dimension · 404 no-cycle-on-GET / not-invited.

### UI (`app/src/screens/assessment-self`, components `LevelSelectorCard`/`ProgressStepper`/`PurposeBanner`)

One dimension per section, four LevelSelectorCards (native radio group + arrow-key nav; selected = 2px teal border + check icon, never colour-alone), per-dimension evidence, PurposeBanner above the first section, ProgressStepper "x of 7" + projected floor (the 5-of-7 → floor L0 state is the binding mockup case). A pre-populated prior value shows a "last month" pill but doesn't count toward progress until re-confirmed. When the cycle is closed without an override the form renders **read-only** (A4 disabled treatment on cards/evidence/buttons + a notice). SC-007 WCAG 2.2 AA (keyboard-only completion, vitest-axe); strings externalised; tokens.css only.

## Cycle state machine (HAP-7 — FR-002/003/004/005/006/060)

One global monthly assessment cycle per framework. `Cycle` is a **forward-only** state machine: `Draft → Open → Closed`. `Open()` and `Close()` reject any other transition; there is no path back. At most one `Open` cycle exists per framework at a time (a second `open` returns 409).

- **Entities** (`Hap.Domain/Cycles`): `Cycle`, `CycleInvitation` (factories `Invited` / `ExcludedFor(reason)`), `CycleLateOverride`. Enums `CycleState`, `InvitationExclusionReason`.
- **Cadence:** `Cycle.Name` is free-text monthly (e.g. "2026-08"); open/close timestamps are set on transition. **No scheduler** — reminders/escalations are HAP-18.

### Invitations — derived once at open (`CycleService.OpenAsync`)

At `open`, one `CycleInvitation` row is written per **currently-active** person, inside the same transaction as the `Draft → Open` transition:
- active person in an onboarded BU, not excluded → **Invited**;
- contractor (when `contractor_exclusion_enabled`, per-cycle configurable) → **ExcludedFor(Contractor)**, no email row;
- person in a not-yet-onboarded BU → **ExcludedFor(NotOnboarded)**.

This runs **once** and is never recomputed, which is exactly why a BU onboarded *mid-Open* receives nothing until the next cycle opens (FR-002). No opt-out exists (FR-004). Contractor *participation* exclusion here is distinct from the contractor-manager *individual-score access* ruling (DR-0006) — different concern.

> **Q-016 (open):** there is no BU↔framework mapping in the data model, so `OpenAsync` invites every onboarded BU. Correct for this single-framework build; a **hard blocker for any second-framework story** (would over-invite every BU to every framework's cycle).

### Lock at close, and the late override

`Cycle.AllowsSubmission(hasLateOverride)` is the pure lock primitive: **Open** → always; **Closed** → only with a late override; **Draft** → never. HAP-7 ships only the primitive — the Assessment tables that a real submission writes to arrive in HAP-8, so **HAP-8/HAP-9 must consult this primitive** or post-close rejection won't exist (Q-017a).

`POST /api/cycles/{id}/late-override` — **Platform Admin** may grant for any person; a **Manager** may grant only for their **active direct reports** ("own team" = a manager and their active direct reports, per data-model.md — not the whole downward chain). Out-of-scope attempts return 404 (existence-leak convention). *(Known asymmetry: the admin path does not check `IsActive`, so admin can override for a departed person — intentional per AC5 "any person".)*

### Admin endpoints

`[PA] POST /api/cycles`, `/{id}/open`, `/{id}/close` under the same `PlatformAdmin` policy as the other admin surfaces (HAP-4). `POST /api/admin/business-units/{id}/onboard` toggles a BU's onboarded flag. *(This onboard mutation is currently unaudited — Q-017b requires an `AuditLog` row before the flag feeds any reconciled participation/Harris figure.)*

## FrameworkVersion lock enforcement (HAP-7, closing HAP-6 A6)

`Cycle.Open()` is the first caller of `FrameworkVersion.Lock()`. Migration #3 adds `hap_framework_version_locked_guard` — a Postgres trigger (mirroring the audit-log append-only pattern) that rejects raw-SQL **UPDATE, DELETE, and re-parent** (moving a `dimension`/`level_descriptor` between versions) of a **locked** version's content, checking both the OLD and NEW parent on UPDATE. This makes FR-054 immutability enforced at the DB layer, not just in the domain.

> **Backstop gap (carry-forward):** the row-level trigger does **not** fire on `TRUNCATE`, and the app role owns the tables, so `TRUNCATE` of a locked version's content is not rejected. Unreachable via any HTTP path today. A `BEFORE TRUNCATE` statement-level trigger (+ the `session_replication_role` reset coupling) must be added by the next story adding raw SQL near these tables, mirroring HAP-3's `audit_log_no_truncate`.

## Close-time work (deferred to HAP-10)

`CycleService.CloseAsync` is currently a bare `Open → Closed` transition. contracts/api.md specifies close also runs auto-adoption (FR-068), rollup snapshots, and suppression verdicts — **HAP-10 must hook that into this `CloseAsync`** (Q-017), not build a parallel close path.
