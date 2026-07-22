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

## Manager moderation (HAP-9 — FR-008/009/010/011/012/063/069)

The manager's review turns a `Submitted` self-assessment into the **moderated score of record**. All cross-person reads live in the seam (`Hap.Api/Authorization`): `ManagerModerationService` over `TeamEndpoints` (`GET /api/team/reviews`, `GET /api/team/members/{id}/assessment` **[audited]**, `PUT /api/team/reviews/{id}`), with the individual's own result at `GET /api/me/assessment/result`.

### Who may moderate — `moderation ⊆ read`

Authorisation is a **conjunction**: `AuthorizeModeration = AuthorizeIndividualRead.Allowed AND ReviewerOfRecord(subject) == caller`. A caller who cannot *see* a subject's scores can never *moderate* them — this closed a real leak where an above-BU leader (e.g. HIG Executive, denied individual reads by FR-025 cl.2) was the chain manager of a Portfolio Leader and could extract self-scores through the moderation error path. The **reviewer of record** is `ChainResolver.ReviewerOfRecord` — the first *active, non-contractor* ancestor, so DR-0006 contractor managers and FR-070 departed managers are skipped to the real reviewer, while a valid direct manager short-circuits the walk (no skip-level over-grant; DR-0005 one-hop preserved). Every endpoint builds the caller's **real grants** via `CallerContextAsync` (a fresh `RoleGrant` DB read per HAP-4 A3 — never a stripped/`Ungranted` context, which was the root cause of the read leak). A capability-less caller gets an **empty queue** and **404** on read/moderate, with **zero audit rows** on denial.

> **Q-020 (open, owner ruling):** a senior leader whose reviewer-of-record is aggregates-only (a Portfolio Leader under the HIG Executive) has **no eligible moderator** — correct per `moderation ⊆ read`, but it means such assessments go unmoderated and **auto-adopt at close** (FR-068/HAP-10). Provisional (fail-closed): auto-adopt. Two addenda in QUESTIONS.md record why this *extends* rather than contradicts DR-0005, and a cousin BU-delegate cross-BU edge.

### Moderation write — divergence, carry-forward, audit, lock

- **Score of record:** both `SelfScore` and `ManagerScore` persist per dimension (FR-010/011); divergence is **computed live**, never stored. `SetManager` is the sole mutator and **unconditionally** enforces FR-009: `|self − manager| ≥ threshold` requires a comment or the write is rejected **422** (no carry-forward exemption). The server-driven `commentThreshold` / `defaultCommentRequired` flags mean the client never holds a threshold literal, and a carried-forward Δ≥2 default is flagged comment-required so GET and PUT agree.
- **Carry-forward (FR-063):** default adopts the self-score; where a prior-cycle moderated score exists and the self-score is unchanged, the default is carry-forward (the prior *score*, not its comment — Q-019).
- **Audit:** each successful individual view writes **exactly one** `IndividualView` row and each moderation write **one** `ScoreChange` row (Q-018 keep — FR-050 names score changes as MUST-audit), both staged+committed **before** data returns; an audit-write failure **fails the request** (fail-closed).
- **Submission lock (Q-017a):** a moderation write is a submission-class write — it consults `Cycle.AllowsSubmission` + the subject-keyed late override, so post-close moderation is **423** unless an override exists. The queue's `CanModerate` honours the same override, so queue and write agree.
- **Concurrency:** `Assessment` carries an **xmin** optimistic-concurrency token; racing moderations resolve to a clean **409** with the whole unit of work (scores + state + audit) rolled back — exactly one `ScoreChange` row survives, never a 500.

### UI (`app/src/screens/assessment-moderation`, components `DivergenceFlag`/`ComparisonRow`)

Review queue → per-dimension `ComparisonRow` printing **both** self and manager values, `DivergenceFlag` at Δ≥1, the forced-comment error state at Δ≥2 (amber row + red-bordered required field, colour never the sole signal), carry-forward defaults pre-filled, a calibration-delta line, and read-only-on-submit (A4 disabled treatment, mirroring HAP-8's close state). After moderation the individual's result view (FR-012) shows manager scores, comments, and divergence; 404 before. WCAG 2.2 AA (vitest-axe), strings externalised, `tokens.css` only.

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

## Cycle close & rollups (HAP-10 — FR-068/069/070/015/016)

`CycleService.CloseAsync` runs the whole close in ONE transaction, delegating to `ICycleCloseProcessor` (seam impl `CycleCloseProcessor` — reads scores only where the boundary guard permits): `Open → Closed` transition → auto-adopt → rollup snapshots → frozen suppression, one `SaveChanges`+commit. No parallel close path (Q-017 honoured).

- **Auto-adopt (FR-068):** every `Submitted`-but-unmoderated assessment becomes `AutoAdopted` (self→manager scores via `AssessmentScore.AdoptSelf()`, `unmoderated=true`); `Moderated` untouched. AutoAdopted rows stay in means/floor but are **excluded from the calibration delta**. A senior leader with no eligible moderator (Q-020) falls through this same generic path — no special-casing.
- **Departure escalation (FR-070):** a departed manager's pending reviews escalate to `ChainResolver.ReviewerOfRecord` (reused from HAP-9); still-unmoderated at close → auto-adopt.
- **Scoring (FR-015/016, `Hap.Domain/Scoring`):** mean = arithmetic mean of the 7 dimensions to 2dp; floor = min dimension score; distribution by floor level. Pure domain maths, property-tested.
- **Scored population vs completion denominator (§3.5/FR-024):** tracked as **separate** fields. A submitted/moderated mid-cycle leaver stays in per-dimension means + floor distribution (scored population) but leaves the completion denominator; both reconcile independently.

### RollupSnapshot — immutable, frozen at close (research D4/D2)

One `RollupSnapshot` per org node (Team / BU / Group / Portfolio / AllHig) per cycle, holding n, per-dimension means, floor distribution, completion %, unmoderated %, calibration delta, and the **suppression verdict** — computed once at close by REUSING the seam `SuppressionEvaluator` (never reimplemented) and **frozen**: shrinking a node below 4 after close never changes the stored verdict (FR-071 historical rule). Migration #5 makes the table **append-only** (row-level UPDATE/DELETE + `BEFORE TRUNCATE` statement-level triggers, mirroring `audit_log`; live-DB tested) with a **unique `(cycle, nodeType, nodeRef)` index** (`NULLS NOT DISTINCT`) so a racing second close collides and rolls back rather than duplicating immutable rows.

> **Team partitioning (Q-023):** a Team node contains only reports **homed in the same BU as their manager**. Anyone without a same-BU manager — manager-less (BU heads) or **cross-BU-managed** — is **teamless**: counted at BU/Group/Portfolio/AllHig via their home BU, in no Team node. This keeps every Team nested in one BU, so `Σ(team-homed scored n) = BU's team-homed scored n` and the suppression `Σchild ≤ parent` precondition both hold. Their manager still reviews them normally (review authority unchanged — this only shapes the aggregate rollup node). Teamless people are absorbed into the BU-vs-teams complement, so N<4 differencing stays closed by suppression rule 2.

> **Reconcile "as of close" (Q-022):** a post-close late override may **re-moderate an `AutoAdopted` assessment** (`Assessment.Moderate` accepts `Submitted OR AutoAdopted`; `Moderated` stays terminal), gated by the unchanged `moderation ⊆ read` + reviewer-of-record + submission-lock. This mutates the live rows, but the **frozen snapshot is authoritative** — any reconciliation reads it as-of-close, never against post-override live rows.

> **Carry-forward to HAP-11 (G1 flag):** `RollupSnapshot` persists the true N/mean/distribution even for `Suppressed` rows, via a **public DbSet readable outside the seam**. Inert today (no snapshot read path exists), but a **hard precondition on HAP-11**: any snapshot read MUST project through the suppression verdict — no external projection may expose N/mean/distribution for a Suppressed row. A G1 witness item.

> **HAP-11 hardening — hierarchy-global differencing (Q-026) + F2 closed.** HAP-11 strengthened the suppression the close writes: after the per-parent `SuppressionEvaluator` pass, the shared `RollupPipeline` runs `HierarchySuppression.Close`, which closes the differencing attack **across the whole tree**, not just one parent-child level. It (a) collapses equal-membership single-child chains (if any node on a chain is suppressed they all are) and (b) iterates to a fixpoint that suppresses additional published nodes until no suppressed node's count is recoverable by summing the published nodes a reader can reach (the tree identity `parent = Σchildren + teamless`). Protecting the count-system protects every mean identically. The same pipeline serves the live open-cycle dashboard, so live and frozen verdicts agree. This is a **strengthening** of research D2's stated goal (close the subtraction attack), so HAP-10's own suppression verdicts are unchanged (its fixture has no cross-level leak) — but any story reading these snapshots inherits the stronger guarantee. **F2 is now closed structurally:** every snapshot read (HAP-11 `RollupReads`) projects through the verdict via `AggregateReadResult.Project` (a suppressed node carries no number), and `SeamBoundaryTests` fails the build if the `RollupSnapshots` query surface is used outside the seam.
