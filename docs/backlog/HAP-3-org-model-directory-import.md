---
id: HAP-3
title: Org model, directory import port, overrides, and append-only audit foundation
epic: E1-foundations
wave: 0
fr: [FR-020, FR-021, FR-022, FR-023, FR-024, FR-050, FR-053]
risk: L3                # trigger: directory-import writes to people/hierarchy + audit-log write paths
status: qa
estimate: {dev: L, qa: M}
worklog:
  - {phase: dev, start: 2026-07-21T15:54:15Z, end: 2026-07-21T17:04:29Z, mins: 70}
  - {phase: qa, start: 2026-07-21T17:06:52Z, end: 2026-07-21T17:15:58Z, mins: 9}
closure: null
---
## Story
As the platform team, we need the org hierarchy (person → team → BU → group → portfolio) imported from the directory port with a manual-override layer and an append-only audit log, so role scopes and every later feature stand on trustworthy org data.

## Context
- Spec: "Functional Requirements — Module 1: Organization Structure" (FR-020..024), "Data Management & Audit" (FR-050, FR-053); "Key Entities" Person/BusinessUnit/Group/Portfolio/OrganisationOverride/AuditLog.
- Plan: data-model.md "Org & identity" + "Audit & GDPR" (Team is DERIVED from manager links — no Team table); contracts/api.md "Ports — IDirectorySource" and "[PA] POST /api/admin/sync, GET/POST /api/admin/overrides"; research D1 (audit fails closed).
- Files: `backend/src/Hap.Infrastructure/Directory/**` (IDirectorySource + SyntheticDirectoryAdapter reading Hap.Synth output), `backend/src/Hap.Infrastructure/Persistence/**` (DbContext + **EF migration #1**: Person, BusinessUnit, GroupOrg, Portfolio, OrgOverride, RoleGrant, AuditLog), `backend/src/Hap.Infrastructure/Audit/**` (append-only writer), `backend/src/Hap.Domain/**` (entities), sync endpoint in `Hap.Api`.
- **Migration chain: this is migration #1 — HAP-6 serialises behind it.**
- Blocked by: HAP-2
- Parallelisable: no

## Acceptance criteria
- [ ] `POST /api/admin/sync` imports the full synthetic snapshot: person count, manager links, BU/group/portfolio mappings match `directory.json` exactly (integration test asserts counts and spot rows).
- [ ] Re-running sync is idempotent (no duplicates, updated fields overwrite) and never deletes: leavers become `is_active=false`, rows retained (FR-024).
- [ ] A person changing manager or BU in a modified snapshot is updated on next sync; OrgOverride rows survive re-sync and re-apply after import (FR-023 test with an override + re-sync).
- [ ] Every override write produces exactly one AuditLog row (`OrgOverride`); test tagged `Category=PrivacyReporting`.
- [ ] AuditLog has no UPDATE/DELETE path: EF model maps no setters for mutation, no API mutates it, and `Hap.Architecture.Tests` asserts no code path calls Update/Remove on the AuditLog DbSet (tagged `Category=PrivacyReporting`).
- [ ] Audit write failure fails the audited operation (fails closed — research D1): test forces audit failure and asserts the override write rolls back.
- [ ] Migration applies idempotently under `./scripts/verify.sh` (run twice = no-op); verify green.
- [ ] QA (adversarial, fresh agent): attempt to create/modify a person via any non-sync endpoint (must not exist); attempt to delete an audit row via SQL through any exposed path (none exists); document both in this file.

## Attempts / notes

### Attempt 1 — Dev (2026-07-21), branch `HAP-3-fr-021-org-import-audit`

No prior attempts (`git log --all --grep HAP-3` empty; no HAP-* branches/worktrees stranded).

**Risk trigger (L3, confirmed):** directory-import writes to people/hierarchy + audit-log write paths (CLAUDE.md §7 L3 rows). No assessment tables exist yet, so no visibility seam is created in this story; the seam (`Hap.Api/Authorization`) lands with the assessment stories.

**Key design choices**
- **Domain entities are pure POCOs** (Hap.Domain has zero EF dependency — LayeringTests stays green). All EF mapping (keys, enum-as-string conversions, jsonb, FKs) is Fluent API in Hap.Infrastructure.
- **Append-only audit is enforced at the type level:** `AuditLog` has get-only properties set only through its constructor (EF constructor binding) — there is no setter to mutate, so an UPDATE cannot be expressed in C#. No `DbSet<AuditLog>` mutation call exists. `Hap.Architecture.Tests` asserts (a) reflection: AuditLog exposes no property setters; (b) source scan: no `AuditLogs.Update/Remove/RemoveRange/ExecuteUpdate/ExecuteDelete` anywhere under `backend/src`.
- **Audit fails closed via a shared unit of work:** `AuditWriter` stages the audit row on the *same* `HapDbContext`; the override service wraps the domain write + audit write in one transaction and one `SaveChangesAsync`. If the audit write fails, nothing commits. Tested by injecting a faulting `IAuditWriter` and asserting the OrgOverride row is absent after the attempt.
- **Sync is idempotent, never deletes:** upsert persons by `external_ref`, BUs by `code`, groups/portfolios by name; leavers (snapshot `is_active=false` or dropped from snapshot) become `is_active=false`, rows retained (FR-024). Two-pass manager resolution via `external_ref` map.
- **OrgOverrides survive re-sync and re-apply after import** (FR-023): overrides are never touched by sync; after each import the `BusinessUnit`/`Manager` overrides are re-applied so directory data cannot clobber a correction. `DottedLine` overrides are recorded + audited but not yet structurally consumed (no manager-chain effect in v1 — advisory; a later visibility story consumes them).
- **Adapter decoupling:** the `IDirectorySource` port + DTOs live in `Hap.Infrastructure/Directory`; `SyntheticDirectoryAdapter` deserialises the `Hap.Synth` JSON output directly (System.Text.Json, no new dependency, no production dependency on the generator project). A test-only contract test references `Hap.Synth` and asserts the adapter parses generator output.

**Assumption flagged to session lead (NOT a QUESTIONS blocker — known sequencing, not ambiguity):** the `[PA]` (Platform Admin only) authorization guard on `/api/admin/sync` and `/api/admin/overrides` cannot be enforced until `IIdentityProvider` lands (HAP-4/5); this story is wave-0 foundation *before* identity. Endpoints are built with the gate as a single clearly-marked extension point and are exercised API-only via integration tests (consistent with QUESTIONS.md Q-004 provisional: admin surfaces ship API-only in v1). **These endpoints MUST be gated `[PA]` before Gate G1.** No real data, no network, synthetic-only — acceptable for wave 0, flagged so a later story does not forget.

### Round 0 — Dev verify evidence (B1 record)

`./scripts/verify.sh` — **ALL GREEN** on 2026-07-21 at tip `3ee18a4` (end of the initial Dev pass): backend build warnings-as-errors clean; [4/9] migrations applied idempotently (2nd run "No migrations were applied. The database is already up to date."); [5/9] Domain 6 / Architecture 3 / Synth 41 / Api 13 all passed; [6/9] PrivacyReporting suite Architecture 2 + Api 2 passed; [7-9] frontend lint/typecheck/test/build + no-external-font passed.

### Round 1 — L3 panel verdicts (2026-07-21)

- **hap-domain-specialist: SIGN-OFF** (3 advisories: D1 metadata envelope, D2 ActorPersonId follow-up, D3 DottedLine → QUESTIONS).
- **hap-code-reviewer: BLOCKED** — 4 blocking, 8 advisory. Refused to sign off pending the four fixes below.
- **hap-red-team:** converged with the code reviewer on a unified remediation scope; pulled the override self-reference / manager-cycle checks forward to this wave-0 write seam (HAP-5's chain-walk remains read-side defence-in-depth).
- Revision remains **L3**; full-panel re-review follows.

### Round 1 — remediation (all four blocking notes landed)

- **B2 — import manager resolution** (`DirectoryImportService`): a non-null `manager_external_ref` that does not resolve, or that points at the person itself, now **throws** and fails the whole import (was silently nulled), consistent with the unknown-BU guard. Tests: `Sync_throws_when_a_manager_reference_does_not_resolve`, `Sync_throws_when_a_person_is_their_own_manager` (assert nothing committed).
- **B3 — override write seam** (`OrgOverrideService.CreateAsync`): the target is now resolved + validated **before anything is written** — BU/manager must resolve, must not be the subject itself, and a manager override must not create a management-chain cycle (upward walk from the proposed manager). On failure it throws before staging any row, so a rejected override leaves **no override row and no audit row** (fail closed). Typed exceptions: `PersonNotFoundException` → 404, `OverrideValidationException` → 422; basic request validation → 400 (advisory A7). `DottedLine` now records a **null** original value (advisory A5). Tests: unresolvable-BU / self-manager / management-cycle rejections (all assert zero override + zero audit rows), endpoint 422, plus the existing one-audit-row and audit-fail-closed tests.
- **B4 — DB-layer append-only backstop** (migration #1, edited in place — HAP-6 chains behind): a `BEFORE UPDATE OR DELETE ON audit_log` trigger (`hap_audit_log_append_only`) raises, so the database itself rejects any UPDATE/DELETE of audit rows regardless of route (EF `Remove`/`EntityState.Deleted`/`RemoveRange`/`ExecuteUpdate`/`ExecuteDelete`/property-bag, or raw SQL). New `Category=PrivacyReporting` test `Database_rejects_raw_update_and_delete_on_audit_log` asserts raw SQL UPDATE and DELETE are both rejected and the row is untouched. The source-scan guard is **broadened** (context `Remove`/`RemoveRange` calls and `EntityState.Deleted/Modified` on audit-referencing lines) but is now explicitly the early signal, not the enforcement.
- **B1 — record:** round-0 verify evidence (above) and the round-1 panel verdicts (above) recorded per §8.6.

**Round 1 — verify evidence:** `./scripts/verify.sh` **ALL GREEN** on 2026-07-21 at tip `5a6e878` (this notes commit adds only the record and no code): backend build clean; migrations idempotent (2nd run no-op); [5/9] Domain 6 / Architecture 3 / Synth 41 / **Api 20** passed; [6/9] PrivacyReporting Architecture 2 + **Api 3** passed (append-only reflection + source-scan, one-audit-row, audit-fail-closed rollback, **DB-level UPDATE/DELETE rejection**); [7-9] frontend green.

### Advisories — applied / recorded

- Applied: D1 (contracts/api.md metadata envelope + corrupt-snapshot rejection), D3 (QUESTIONS.md Q-010 DottedLine advisory-only), A1 ([PA] gating criteria added to HAP-4 and HAP-5 story files), A5 (DottedLine null original), A7 (400 request validation).
- **D2 (closure follow-up):** the identity story (HAP-4/5) must **wire `ActorPersonId`** on override/role-grant audit rows (currently null pending identity) — list alongside the `[PA]` route-gating flag in the HAP-3 closure notes.
- **A2 (noted, not actioned — needs lead approval):** MSB3277 EFCore Relational 8.0.4-vs-8.0.8 unification warning (Npgsql 8.0.4 pulls 8.0.4; direct ref is 8.0.8). It is a benign MSBuild warning (not promoted by `TreatWarningsAsErrors`) and pre-exists this story. A dependency-version bump is an explicit L2 trigger — **not** changed here; flagged for the session lead to schedule.
- **A4 (noted):** the re-apply loop reports `OverridesReapplied` but not skipped overrides. With B3 rejecting unresolvable overrides at creation, a re-apply skip should not occur for a resolvable target; a `DirectoryImportResult.OverridesSkipped` counter is a cheap future addition if observability is wanted — recorded, not actioned this round.
- Red-team interceptor / EF concurrency-token suggestions: noted; a concurrency token on an immutable, never-updated audit row is moot given the DB trigger now forbids UPDATE outright.

### hap-red-team round 1 (2026-07-21), formal deliverable — verdict BLOCKED pending B2/B3/B4 (§9.4 record)

Per-surface outcomes (attack paths examined and their result):

- **Surface 1 — audit evasion (write-side):** core fail-closed has **NO PATH** — one scoped `HapDbContext`, one `SaveChanges`, one transaction; a DB-time audit failure rolls the whole operation back. Sync-without-audit is **by design** (FR-050 enumerates the audited actions; the `AuditAction` enum matches, and directory sync is not one). **VIOLATION FOUND → B3:** an unresolvable `OverrideValue` committed the override + audit row while apply silently no-op'd — an immutable audit row asserting a correction that never took effect. *Fixed:* CreateAsync validates + resolves before any write; unresolvable ⇒ reject, zero rows. Advisory (future, before any RoleGrant endpoint): a `SaveChanges` interceptor asserting every Added `OrgOverride`/`RoleGrant` has a matching Added `AuditLog`.
- **Surface 2 — append-only bypass:** **NO PATH through shipped code** (grep-confirmed: no `ExecuteSql*`/`Remove`/`Update` on the audit set), but the guarantee was not defended in depth → **B4:** five enumerated evasions (raw SQL, `context.Remove`, aliased DbSet, EF property-bag UPDATE, a future interceptor) all pass the source-scan. *Fixed:* `BEFORE UPDATE OR DELETE` trigger in migration #1 + a `Category=PrivacyReporting` raw UPDATE/DELETE test executed over the **app's own connection/role**. **Trigger, not REVOKE** — REVOKE is inert for the owner role verify's disposable Postgres uses (owner/superuser bypasses privileges; the trigger fires for all roles). Confirmed on the revised tip: migration uses `CREATE TRIGGER … RAISE EXCEPTION`, no REVOKE; the test issues raw `UPDATE`/`DELETE` through the app `HapDbContext`.
- **Surface 3 — unauthenticated admin:** **VIOLATION FOUND** (graph poisoning, cycle injection, `createdBy` spoof, unbounded audit append), **ADJUDICATED TOLERABLE for wave-0** (synthetic-only, local, single operator, Q-004). **HARD CONDITION for the record:** the `[PA]` guard MUST attach before any of — real directory data, a real `IIdentityProvider`/multi-user, or Gate G1 — and specifically before HAP-5's chain resolver is trusted. Gating requirement written into HAP-4 and HAP-5 acceptance criteria now (advisory A1).
- **Surface 4 — hostile-snapshot integrity:** synthetic path **NO PATH** (generator is top-down acyclic). Untrusted input **VIOLATION FOUND → B2:** A→B→A and self-manager imported cleanly; a dangling manager ref was silently nulled (fabricating an org root); duplicate `external_ref`s collapsed last-wins; asymmetric with the unknown-BU throw. *Fixed:* import throws on unresolvable/self manager ref; HAP-5 chain-walk stays as read-side defence-in-depth. (Duplicate-`external_ref` last-wins within a single snapshot is inherent to keyed upsert; the generator guarantees uniqueness — noted, not a wave-0 blocker.)
- **Surface 5 — sync-vs-override ordering:** persistent state **NO PATH** — re-apply runs after import (FR-023 holds), `CreatedAt`-ordered and deterministic. A transient last-writer window exists under concurrent admin writes (no concurrency token on `Person`); self-heals on the next sync. Advisory: a row-version token when multi-user lands.
- **G1 readiness: NOT READY** — blocked on the `[PA]` guard and the trigger; this story is a **precondition** for G1, not a gate-completer.

Red-team's re-verify list for the revised tip (all satisfied at `0814d7b`; independent re-run is the panel's): (1) trigger-not-REVOKE in migration #1 + raw-SQL test over the app role, five evasion routes re-run; (2) importer throws on unresolvable/self manager, cycle probe rolls back; (3) CreateAsync rejects unresolvable/self/cyclic pre-write with zero rows; (4) all five surfaces re-run, verify green incl. PrivacyReporting, risk class re-derived (stays **L3**).

### Round 2 — panel verdicts + hap-red-team round-2 formal deliverable (2026-07-21, §9.4 record)

- **hap-domain-specialist: SIGN-OFF** (stands at `0814d7b`). **hap-code-reviewer: SIGN-OFF** (all four round-1 blocking notes verified first-hand). **hap-red-team: CONDITIONAL SIGN-OFF.**
- Red-team round-2: B2/B3 fully closed; B4 closes all five original append-only evasion routes (raw SQL, `context.Remove`, aliased DbSet, EF property-bag UPDATE, future interceptor) — verified trigger-not-REVOKE and that the raw UPDATE/DELETE test runs over the app's own connection/role. **Sixth route FOUND:** a `BEFORE UPDATE OR DELETE` **row** trigger does **not** fire on `TRUNCATE`, so `TRUNCATE audit_log` still defeated FR-053's "never deleted". Unreachable today (only the test-only `ResetAsync` truncates), so not a hard block from red-team — but the **session lead ruled FIX NOW** (a sacred append-only backstop with a mass-delete hole, in the still-mutable migration #1, cheap now and expensive once HAP-6 chains behind it).
- Discharged this story: S1 (fail-closed, B3), S2 (append-only, B4 UPDATE/DELETE + now TRUNCATE), S4 (hostile snapshot, B2). Carry-forward: S3 `[PA]` guard (HAP-4/5 ACs), S5 concurrency token (multi-user), cycle-safe read-side chain walk (new HAP-5 AC).

### Round 3 — remediation (both blocking items landed)

- **B4-TRUNCATE** (migration #1, edited in place): added a second trigger `audit_log_no_truncate` — `BEFORE TRUNCATE ON audit_log FOR EACH STATEMENT` — reusing `hap_audit_log_append_only()` (it raises for any `TG_OP`, incl. `TRUNCATE`); matching `DROP TRIGGER` in `Down`. Both triggers verified present after apply; migration #1 still applies idempotently (verify runs it twice — 2nd is a no-op). New `Category=PrivacyReporting` test `Database_rejects_raw_truncate_of_audit_log` issues raw `TRUNCATE audit_log` over the app's own `HapDbContext` connection/role and asserts rejection + row survival. **Coupling handled:** the trigger blocks the test-only `TestSupport.ResetAsync` truncate, so that reset now brackets its `TRUNCATE ... CASCADE` with `SET session_replication_role='replica' … 'origin'` to bypass the trigger for the wipe — this works only because the disposable TEST DB role is a superuser; **the application never runs with that role and never calls that path** (test scaffolding only).
- **HAP-5 AC — cycle-safe chain walk** (`docs/backlog/HAP-5-visibility-seam.md`): added a load-bearing criterion that the management-chain resolver must not assume acyclicity — visited-set + depth-cap, proven against a synthetic hostile 2-cycle fixture (`Category=PrivacyReporting`). HAP-3 guards single-node/2-cycle *overrides* at the write seam and rejects self/unresolvable manager refs on import, but a multi-node cycle importable via a future non-synthetic directory (Entra) is the read-side backstop's job.

**RoleGrant audit invariant (carry-forward, noted):** "every RoleGrant write carries exactly one AuditLog row" is currently **convention-only** — there is no RoleGrant write path in HAP-3 (the entity/table ships as foundation; the grant endpoint lands with the identity/roles story). Carry the invariant + the red-team's SaveChanges-interceptor advisory (assert every Added `OrgOverride`/`RoleGrant` has a matching Added `AuditLog`) forward to the RoleGrant-endpoint story.

**Round 3 — verify evidence:** `./scripts/verify.sh` **ALL GREEN** on 2026-07-21 at code tip `459a1f9` (this notes commit adds only the record, no code): backend build clean; [4/9] migrations idempotent with **both** triggers ("No migrations were applied. The database is already up to date."); [5/9] Domain 6 / Architecture 3 / Synth 41 / **Api 21**; [6/9] PrivacyReporting Architecture 2 + **Api 4** (append-only reflection + source-scan; one-audit-row; fail-closed rollback; DB-level UPDATE/DELETE **and TRUNCATE** rejection); [7-9] frontend green.

### L3 panel — COMPLETE (2026-07-21, tip `ce0c97b`, code `459a1f9`) — three sign-offs, zero blocking

- **hap-domain-specialist: SIGN-OFF** (confirmed stands at `0814d7b`; the round-3 delta is mechanism-only, no domain change).
- **hap-code-reviewer: SIGN-OFF** re-confirmed round 3 — TRUNCATE closure + migration idempotence verified first-hand, verify ALL GREEN at `ce0c97b`.
- **hap-red-team: UNCONDITIONAL SIGN-OFF** — all three sacred-guarantee attack classes (B2 hostile-snapshot integrity, B3 fail-closed override, B4 append-only incl. TRUNCATE) closed with proving tests.

### Dev clock-out (Phase 2 complete)

Dev worklog logged to frontmatter: `{phase: dev, start: 2026-07-21T15:54:15Z, end: 2026-07-21T17:04:29Z, mins: 70}` (measured AI wall-clock across dev + three review rounds on the one `.wallclock-HAP-3-dev`; well under 4× the `dev: L` = 3d human-equivalent estimate — no overage). `.wallclock-HAP-3-dev` deleted. `status: qa`. QA is a fresh `hap-qa` instance (no Dev context).

### Carry-forward for Phase-4 closure (lead to lift into the `closure:` block)

- **G1 preconditions (must all hold before Gate G1 / any real data):**
  - `[PA]` route-gating of `/api/admin/*` — added to HAP-4 (auth sweep) and HAP-5 (role gating) acceptance criteria.
  - **`ActorPersonId` wiring** — override/role-grant audit rows carry `actorPersonId: null` in wave-0 (no identity); the identity story must populate the acting admin's person id. **Named follow-up (domain-requested for the durable closure block, not just prose).**
  - **NEW (red-team G1 item):** the **production** DB application role MUST NOT be a superuser and MUST NOT hold trigger-disable privilege (`session_replication_role`), otherwise the append-only trigger is bypassable and FR-053 reopens. (The test-only `ResetAsync` bypass relies on the disposable test DB's superuser role and is scaffolding only.)
- **Cycle-safe read-side chain walk** — HAP-5 AC added (visited-set + depth-cap vs a hostile 2-cycle fixture).
- **RoleGrant audit invariant + SaveChanges-interceptor advisory** — carry to the RoleGrant-endpoint story (no RoleGrant write path exists yet).
- **A2 MSB3277** EFCore 8.0.4-vs-8.0.8 unification warning — dependency bump is an L2 trigger; lead to schedule.

## QA — fresh agent, adversarial (2026-07-21)

Fresh `hap-qa` instance, no shared context with Dev. Re-derived correctness from the acceptance
criteria and the governing spec sections (spec.md FR-020..024/050/053, data-model.md "Org &
identity"/"Audit & GDPR", contracts/api.md IDirectorySource/[PA] admin routes, research.md D1),
not from the Dev/red-team notes above (read only after independent exploration, to keep the
adversarial posture honest).

**Baseline:** ran `./scripts/verify.sh` unmodified at tip `370832f` before writing anything —
**ALL GREEN**, independently reproducing the exact counts Dev's notes claim: Domain 6 /
Architecture 3 / Synth 41 / Api 21 tests; PrivacyReporting Architecture 2 + Api 4; migrations
idempotent (2nd apply "already up to date"). This confirms Dev's evidence is real, not asserted.

### Acceptance-criterion verdicts (one check per clause)

1. **`POST /api/admin/sync` imports the full snapshot exactly — PASS.** Re-verified Dev's
   canonical-snapshot test, and added an **independent** reconciliation
   (`Sync_counts_independently_reconcile_against_the_raw_snapshot_json_on_disk`) that parses the
   raw `directory.json` bytes fed to the endpoint with `JsonDocument` — not a second call to
   `DirectoryGenerator.Generate` — and cross-checks person count, BU/group/portfolio counts, BU
   code set, and every `manager_external_ref` resolution against the post-sync DB. Dev's own test
   compares two `Generate(CanonicalSeed)` calls against each other, which proves the generator is
   deterministic but not that the *importer* reconciles to the *actual bytes on disk*; this closes
   that gap.
2. **Idempotent, no duplicates, never deletes — PASS.** Dev's tests re-verified. Added
   `Snapshot_with_duplicate_external_ref_does_not_crash_and_creates_exactly_one_person_row` — an
   untested edge Dev's notes call "inherent to keyed upsert, not a wave-0 blocker" but never
   proved: confirmed the importer does not throw and produces exactly one row (last-value-wins),
   not two rows or a `DbUpdateException` masking the real cause.
3. **Manager/BU change applied on next sync; overrides survive re-sync — PASS.** Dev's test
   re-verified. Extended to the one `OverrideField` Dev never exercised: `DottedLine`. Confirmed
   it is recorded/audited but — correctly, per Q-010's restrictive provisional answer — does
   **not** alter `ManagerPersonId` (the structural chain), unlike `BusinessUnit`/`Manager`.
4. **Every override write → exactly one AuditLog row — PASS, gap closed.** Dev's test only
   exercises `BusinessUnit`. AC4 says "every override write" literally, which includes
   `DottedLine`; added `DottedLine_override_write_produces_exactly_one_audit_row` (passes: 1
   override row, 1 audit row). Also added a concurrency probe
   (`Concurrent_override_writes_for_the_same_subject_never_desynchronise_override_and_audit_counts`)
   — 5 concurrent `CreateAsync` calls against the same subject (different target BUs, separate
   `DbContext` scopes) — `override count == audit count == 5` every run; the shared-transaction
   design holds under concurrency, not just sequentially.
5. **AuditLog has no UPDATE/DELETE path — PASS, with one new documented finding (non-blocking).**
   Reflection (no setters), architecture source-scan, and "no API mutates it" all re-verified and
   substantially broadened (see AC8 below — 13 total mutation-route probes across people,
   admin/people, overrides, admin/overrides, admin/audit). **New finding:** the DB-layer
   append-only trigger can itself be disabled with a single `ALTER TABLE audit_log DISABLE
   TRIGGER audit_log_no_update_delete;` by the exact role the app connects as — table
   **ownership** grants this, not superuser, and the `hap` role owns `audit_log` because it ran
   the migration that created it. Verified two ways: (a) manual `psql -U hap`: disable → `INSERT`
   a row → `DELETE FROM audit_log` → succeeds → 0 rows survive; (b) an automated regression test
   (`Disabling_the_append_only_trigger_then_deleting_is_examined_over_the_app_role`, tagged
   `PrivacyReporting`) reproduces this over the app's own `HapDbContext` connection and always
   re-enables the trigger in a `finally` so it cannot poison later tests in the shared,
   non-parallelised `hap-db` collection. **Why this does not block AC5 or the story:** no
   HTTP-reachable endpoint executes arbitrary or admin-supplied SQL anywhere in this diff (see
   AC8), so this route requires the same trust level as direct database/code access — it is not
   attacker-reachable through the shipped API today. **Why it still matters:** it is a materially
   different bypass from the `session_replication_role` route the story's own G1-precondition
   carry-forward already calls out (that one requires *superuser*, confirmed confined to
   `TestSupport.ResetAsync` — the only call site in the repo, test scaffolding only). Trigger
   ownership-disable requires only being the table's owner, which is unavoidable for whatever
   role runs migrations. **Recommendation (added to carry-forward below):** before any real data
   or Gate G1, separate the migration-owning role from the application's runtime-query role, or
   explicitly accept and document the residual risk — "must not be superuser" alone is
   insufficient to close this route.
6. **Audit write failure fails the operation closed — PASS.** Dev's `ThrowingAuditWriter` rollback
   test re-verified. Examined the reverse direction (audit stages successfully, then the override
   insert itself fails) for an independent forcing route: no unique/check constraint exists on
   `org_overrides` beyond the FK to an already-resolved `Person` (validated before either row is
   staged), so this direction is not independently constructible against the current schema —
   examined, no route found, not fabricated.
7. **Migration applies idempotently under verify.sh (run twice) — PASS.** Confirmed in both my
   baseline run and the final run with QA tests added: `dotnet ef database update` run twice,
   second run reports "No migrations were applied. The database is already up to date." both
   times.
8. **QA adversarial clause — PASS, broadened.** "Create/modify a person via any non-sync
   endpoint": Dev probed 4 routes; added 4 more (`DELETE`/`PATCH /api/people/{id}`,
   `PUT`/`DELETE /api/admin/people/{id}`) plus a distinct new surface — override/audit
   mutation-or-deletion routes (`DELETE`/`PUT`/`PATCH /api/admin/overrides/{id}`,
   `DELETE`/`PUT /api/admin/audit/{id}`) — 9 new probes, all correctly `404`/`405`. "Delete an
   audit row via SQL through any exposed path": none exists at the HTTP layer (confirmed above);
   at the DB layer, raw UPDATE/DELETE/TRUNCATE are all rejected (Dev's B4 rounds, re-verified),
   and the one route that *does* work (trigger-disable, finding 5 above) requires DB/code access,
   not an exposed HTTP path.

### §9.3 mandatory adversarial attempts (privacy/reporting seam)

This story touches audit write paths and org data the future chain resolver will trust, so §9.3
is mandatory. **Verified myself, not taken on trust, that the individual-score/rollup attacks are
out of reach by construction:** `HapDbContext.cs` exposes exactly seven `DbSet`s —
`Portfolios`, `Groups`, `BusinessUnits`, `People`, `OrgOverrides`, `RoleGrants`, `AuditLogs` — no
`Assessment`/`AssessmentScore`/`RollupSnapshot`/`HarrisSubmission` table exists in migration #1;
`Program.cs` + `AdminEndpoints.cs` together expose exactly `/healthz`, `POST /api/admin/sync`,
`GET/POST /api/admin/overrides` — no individual-score read or aggregate-rollup endpoint exists
anywhere in this branch. So:

- **(a) Read a score outside the management chain, as each seeded role** — **OUT OF REACH BY
  CONSTRUCTION.** No `IIdentityProvider`/seeded roles exist yet (HAP-4/5); no assessment data or
  score-read endpoint exists at all. Confirmed by source inspection above, not skipped silently.
- **(b) Obtain an aggregate covering <4 people** — **OUT OF REACH BY CONSTRUCTION.** No
  rollup/aggregate table or endpoint exists yet (`RollupSnapshot` is a later story).
- **(c) Desynchronise a rollup/Harris figure from its records** — **OUT OF REACH BY
  CONSTRUCTION.** No `HarrisSubmission`/`HarrisSubmissionLine` table or generation code exists yet.
- **Applicable target — create/modify a Person or hierarchy row via any non-sync endpoint:**
  attempted (13 routes total across Dev+QA); **none exists.**
- **Applicable target — UPDATE/DELETE/TRUNCATE `audit_log` via every reachable route:** EF
  (`Remove`/`RemoveRange`/`EntityState.Deleted`/`ExecuteUpdate`/`ExecuteDelete`/property-bag) is
  covered by the architecture source-scan (re-verified green); raw SQL over the app's own
  `HapDbContext` connection/role — UPDATE, DELETE, TRUNCATE all **rejected** (Dev's B4 rounds,
  re-verified); the DDL-level **trigger-disable** route **succeeded** (finding 5 above) but is
  not HTTP-reachable. `session_replication_role` confirmed by grep to have exactly one call site
  in the whole repo — `TestSupport.ResetAsync` — and is test scaffolding only; the disposable test
  DB role is a superuser, the application never runs as that role and never calls that path.
- **Applicable target — force an audit-write failure, confirm rollback (fail-closed):** re-verified
  Dev's test; examined the reverse direction, no independent forcing route exists against the
  current schema (documented above under AC6).
- **Applicable target — override committing while resolving to nothing / self-reference / cycle /
  hostile snapshot:** all of Dev's B2/B3 tests re-verified green; extended to the untested
  `DottedLine` field (self-reference and unresolvable-target rejection, both zero-row fail-closed)
  and to a duplicate-`external_ref` hostile-ish snapshot (no crash, single row).

### Red-team brief (§9.4)

**A concrete violation path was constructed** (the append-only trigger-disable route, finding 5
above) — documented with exact commands, reproduced by an automated regression test, and analysed
for reachability: it requires DB/code-level access at the same trust level as `psql` with the
app's own credentials; no HTTP-reachable endpoint in this diff executes arbitrary or
admin-supplied SQL, so it is **not attacker-reachable through the shipped API today**. It is,
however, a real gap in the story's own stated G1 precondition ("role must not be superuser") —
table ownership alone is sufficient, and ownership is unavoidable for the role that runs
migrations. Every other §9.3 target examined above returned **NO PATH**, each with the specific
route(s) attempted stated rather than "looks fine."

### QA outcome — **PASS**

All 8 acceptance-criterion clauses verified literally and hold. Zero HTTP-reachable violations of
any privacy/audit/hierarchy guarantee found. One non-blocking, evidence-backed infrastructure
finding (DB-role trigger-disable) is added to the carry-forward list below, distinct from and in
addition to the existing `session_replication_role` item.

**Tests added this QA window** (honestly attributed as QA work, not Dev):
`backend/tests/Hap.Api.Tests/QaAdversarialTests.cs` — 18 new test cases (12 `[Fact]` + one
`[Theory]` with 9 `InlineData` cases), 3 tagged `Category=PrivacyReporting`
(`DottedLine_override_write_produces_exactly_one_audit_row`,
`Concurrent_override_writes_for_the_same_subject_never_desynchronise_override_and_audit_counts`,
`Disabling_the_append_only_trigger_then_deleting_is_examined_over_the_app_role`).

**Verify evidence:** `./scripts/verify.sh` **ALL GREEN** at branch tip `370832f` with the QA test
file added (uncommitted at time of run, committed immediately after): backend build clean;
migrations idempotent (2nd run no-op, confirmed twice across both my verify runs); [5/9] Domain 6
/ Architecture 3 / Synth 41 / **Api 39** (was 21, +18 QA); [6/9] PrivacyReporting Architecture 2 +
**Api 7** (was 4, +3 QA); [7-9] frontend green (unchanged, no frontend touched this window).

**Carry-forward for Phase-4 closure (QA additions to the existing list above):**
- **NEW — DB role trigger-disable (finding 5):** before Gate G1 / any real data, either separate
  the migration-owning Postgres role from the application's runtime-query role (so the app's role
  does not own `audit_log` and has no `ALTER`/trigger-disable privilege on it), or explicitly
  ratify the residual risk in a decision record. This is **in addition to**, not a duplicate of,
  the existing "production role must not be superuser / must not hold
  `session_replication_role`" item — ownership-based trigger-disable requires neither superuser
  nor `session_replication_role`.

### QA clock-out

QA worklog logged to frontmatter: `{phase: qa, start: 2026-07-21T17:06:52Z, end:
2026-07-21T17:15:58Z, mins: 9}` (measured AI wall-clock; well under 4× the `qa: M` estimate — no
overage). `.wallclock-HAP-3-qa` deleted. Status remains `qa` — closure (merge, `status: done`,
change-log row) is the session lead's job, not QA's.
