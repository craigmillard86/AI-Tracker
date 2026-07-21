# Cycles & assessment — as built

_Subsystem shipped incrementally by HAP-7 (cycle state machine) and extended by HAP-8..HAP-10 (scoring, moderation, close). Describes shipped behaviour only; WHAT/WHY live in `docs/spec/` + `specs/`, decisions in `docs/decisions/`._

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
