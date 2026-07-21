# Org hierarchy, directory import & append-only audit — as built

_Subsystem shipped by HAP-3 (FR-020..024, FR-050, FR-053). Describes shipped behaviour only; WHAT/WHY live in `docs/spec/` + `specs/`, status in `docs/backlog/`, decisions in `docs/decisions/`._

## What exists

The org hierarchy and the audit trail. Everything downstream (identity role derivation, the visibility seam, assessments) reads the people/hierarchy rows this subsystem imports, and writes to the audit trail this subsystem owns.

### Entities (`Hap.Domain/Org`, `Hap.Domain/Audit`)

Pure POCOs, no EF dependency — all mapping is Fluent API in `HapDbContext`.

- `Person` — natural key `ExternalRef`; `ManagerPersonId` self-reference is the org tree; `IsActive` (leaver flag), `OnLeave`, `EmployeeType` (Employee/Contractor), BU membership.
- `BusinessUnit` (key `Code`), `GroupOrg`, `Portfolio` — the fixed hierarchy Person → BU → Group → Portfolio.
- **There is no `Team` table.** A team is *derived* from manager links (a manager plus their direct reports). Any "team" concept is computed, never stored.
- `OrgOverride` — a manual correction to a Person's BU or Manager that survives directory re-sync (FR-023). Immutable (get-only, constructor-bound).
- `RoleGrant` — an explicit in-app role grant (entity exists; no write endpoint yet).
- `AuditLog` — append-only record of audited actions (`AuditAction` enum matches FR-050); immutable, non-FK actor/subject columns so no person delete can cascade into it.

### Directory import (`Hap.Infrastructure/Directory`)

- `IDirectorySource.FetchSnapshotAsync()` is the port; `SyntheticDirectoryAdapter` reads the HAP-2 generator's `directory.json` (the additive `metadata` envelope is ignored on import). The future Entra adapter implements the same port.
- `DirectoryImportService.SyncAsync` upserts by natural key inside one transaction: idempotent, never deletes (a person absent from the snapshot or flagged inactive is **deactivated and retained**, FR-024). After import it re-applies `OrgOverride` rows so directory data can never clobber a correction.
- **Import is atomic and validating, not best-effort:** an unresolvable or self-referential `manager_external_ref` (and an unknown `bu_code`) throws and rolls back the whole snapshot — no partial import, no silently-orphaned rows (the stricter correct reading of SC-009 "no orphaned records"). Multi-node cycles (A→B→A) across two existing rows are not caught at import and are the visibility seam's read-side responsibility (cycle-safe chain walk, HAP-5).

### Overrides & audit (`Hap.Infrastructure/Directory/OrgOverrideService`, `Hap.Infrastructure/Audit/AuditWriter`)

- Creating an override **validates the target resolves before writing anything** — unresolvable value, self-as-manager, or a cycle-introducing manager override is rejected (404/422) with zero override rows and zero audit rows written.
- Every accepted override write produces exactly one `AuditLog` row.
- **Audit fails closed** (research D1): the override write, its audit row, and the immediate re-apply share one `HapDbContext` and one `SaveChangesAsync` inside one transaction. If the audit row can't be written, nothing commits.

### Append-only enforcement — defence in depth

1. `AuditLog` has no setters (constructor-bound) — an UPDATE can't be expressed in C#.
2. An architecture test source-scans `backend/src` for mutation calls on the audit set (early signal; known-evadable, so not the enforcement).
3. **The enforcement is at the database.** Migration #1 installs two triggers on `audit_log`, both raising `append-only`: `audit_log_no_update_delete` (`BEFORE UPDATE OR DELETE ... FOR EACH ROW`) and `audit_log_no_truncate` (`BEFORE TRUNCATE ... FOR EACH STATEMENT`). Together they reject every UPDATE/DELETE/TRUNCATE route — EF or raw SQL — for any non-privileged role. INSERT (append) is allowed. `Category=PrivacyReporting` tests prove UPDATE, DELETE, and TRUNCATE are all rejected over the app's own connection.

## Admin surface (`Hap.Api/AdminEndpoints.cs`)

`POST /api/admin/sync`, `GET/POST /api/admin/overrides`. **These are unauthenticated in this build** — identity lands in HAP-4/5. Tolerable only because the build is synthetic-only, local, single-operator. A single marked extension point attaches `[PA]` gating; this MUST be closed before Gate G1 (tracked in HAP-4/HAP-5 acceptance criteria).

## Known pre-G1 conditions (not local defects)

- `[PA]` gating of the admin endpoints must be in place before real data.
- `AuditLog.ActorPersonId` is null until identity is wired.
- The append-only triggers are bypassable by a **superuser** (`session_replication_role='replica'`) or by the **table-owning** role (`ALTER TABLE ... DISABLE TRIGGER`). The production app's runtime DB role must be neither a superuser nor the audit_log owner — separate the migration/owner role from the query role before G1.
