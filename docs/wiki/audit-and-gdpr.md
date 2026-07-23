# Audit trail & GDPR — as built

_Subsystem shipped by HAP-12 (FR-050, FR-051, FR-052, FR-053) — the G1-readiness capstone (constitution Art. VI.5 & Art. VII). Describes shipped behaviour only; WHAT/WHY live in `docs/spec/` + `specs/`, decisions in `docs/decisions/`. Admin-facing/API-only — no user-guide page (QUESTIONS.md Q-004)._

## What exists

Three surfaces close the audit-and-GDPR seam that the whole build has been writing rows into since HAP-3:

- a **read-only audit search** for a Platform Admin,
- a **right-of-access export** any person can pull of their own data,
- a **retention erasure job** that nulls raw scores older than 3 years.

All three live in the visibility seam (`backend/src/Hap.Api/Authorization/**`); the export and retention paths read/write individual assessment data only through the seam store, so no query path escapes it.

## The audit log (recap — written since HAP-3, verified here)

`AuditLog` (`Hap.Domain/Audit`) is append-only by three independent guards: the type has no setters (an UPDATE cannot be expressed in C#), an architecture test forbids any `Remove`/`Update`/`ExecuteDelete` on the set, and a database trigger (`hap_audit_log_append_only`, migration #1) raises on any `UPDATE`/`DELETE`/`TRUNCATE`. Rows are appended only through `IAuditWriter`, staged on the caller's `HapDbContext` so they commit atomically with the business write they accompany — **fail-closed**: if the audit write fails, the audited action rolls back.

The closed set of actions (`AuditAction`): `IndividualView`, `ScoreChange`, `RoleGrant`, `OrgOverride`, `Export`, `RetentionErasure`.

### Audit-completeness sweep (SC-005)

`AuditCompletenessSweepTests` walks every wired **[A]** individual-read endpoint and proves: an authorised call writes **exactly one** `IndividualView` row (correct actor/subject); an unauthorised call returns 404 and writes **zero** rows. Two route-table guards keep it honest: the individual-read surface in the running app must equal the swept set (a newly-wired person-addressed assessment read fails the test until it is covered), and **no audit-mutation endpoint may exist** in the route table. Today the [A] surface is a single endpoint, `GET /api/team/members/{personId}/assessment`; the contract's `GET /api/bus/{buId}/people/{personId}/assessment` is not yet wired by any story.

## Right-of-access export (FR-051)

`GET /api/me/export` → `PersonalDataExportService`. Self-scope: the subject is the session's person, never a route/body parameter, so a caller can only ever export **their own** data. The export contains everything held about the person — profile + org links, and every cycle's self scores, evidence, moderated scores, comments, and moderation metadata (state, submitted/moderated timestamps, moderator, unmoderated flag) — each figure labelled from framework data, not raw ids. Validated against a hand-assembled expected export for one synthetic user.

The export **writes an `Export` audit row** (actor == subject), fail-closed (staged and committed before the body is returned). This is the one self-scope read that is audited — a subject-access egress a DPO must be able to see — distinct from the "viewing your own assessment screen is not audited" rule.

## Retention erasure (FR-052)

`POST /api/admin/retention/run` (Platform Admin) → `RetentionService`. Policy: raw self/manager score **values** in cycles **closed more than 3 years ago** are erased in place; the **rows are retained** so the frozen `RollupSnapshot` aggregates stay reconcilable (FR-052: "aggregates retained indefinitely") and history is not perforated. Snapshots are never touched — closed-cycle reads come from the snapshot, never these rows, so erasing raw values after retention changes no published figure.

- **Age** is measured by `Cycle.ClosesAt` — an Open/Draft cycle, or one closed within the window, is never in scope.
- **What "erase" does** (`AssessmentScore.Erase()`): `SelfEvidence`, `ManagerScore`, `ManagerComment` → null; `SelfScore` → 0. `SelfScore` is a non-nullable `int` and the story shipped no migration (HAP-13 owns the next migration slot), so it is zeroed rather than SQL-nulled — see **QUESTIONS.md Q-027** (owner ratification at G1).
- **Idempotency**: the authoritative "this assessment was already erased" ledger is the per-assessment `RetentionErasure` audit row itself (one per affected assessment), **not** the row content. A second run resolves the same already-erased set from the audit log and erases zero further rows.
- **Atomic + fail-closed**: the erasure and its `RetentionErasure` audit rows commit in one transaction (mirrors moderation) — a failed audit rolls the whole erasure back.
- **Permanent against every write path**: erasure cannot be reversed by re-populating an erased row. Both write paths check the `RetentionErasure` ledger **before** any write and refuse an erased assessment with 409: `ManagerModerationService.ModerateAsync` (`ModerationErasedException`) for a Q-022 late-override re-moderation, and `SelfAssessmentService.UpsertScoresAsync`/`SubmitAsync` (`AssessmentErasedException`) for a dormant-platform late-override self-write — otherwise a real value would be written back into an erased row while every read (which keys off the same permanent ledger) kept reporting it erased, hiding real personal data from the subject (FR-051). The transient `AssessmentScore.Erased` flag (all three mutators — `SetSelf`/`SetManager`/`AdoptSelf` — throw) is the same-unit-of-work backstop. One **cross-request** edge remains (concurrent retention-vs-moderation TOCTOU) — parked for a durable interlock; see the ratification-residual row below.
- **Ledger fails CLOSED on read**: `ErasureLedger.ParseErasedAssessmentIds` throws `CorruptErasureLedgerException` on a `RetentionErasure` Detail with no parseable `assessmentId` (a writer-shape test guards the writer always emits one) — a corrupt privacy ledger halts rather than serving possibly-erased data as genuine.
- **Disclosed in the export**: the right-of-access export cross-references the same ledger and presents an erased datum as `Erased: true` / `DataErased: true` with `null` values — never a fabricated `0` (the cycle + moderation metadata still show; erased ≠ never-happened).
- **Disclosed/refused on EVERY raw-score read (structural)**: the erasure placeholder (`SelfScore→0`) must never be served as a genuine value on any read surface. Each raw-`AssessmentScore` read consults the shared **`ErasureLedger`** (the single source `IsErasedAsync`/`ErasedAssessmentIdsAsync`, which the export, the moderation interlock, and the retention idempotency check all use):
  - `GET /api/team/members/{id}/assessment` (manager cross-person read) → **refuses** an erased assessment (404, no audit row); the manager is not the data subject, so their route does not disclose — the subject's own export/result does.
  - `GET /api/me/assessment/result` (FR-012) and `GET /api/me/assessment` prefill (FR-062) → **disclose** to the data subject (`DataErased`/`Erased` flags; an erased prior never pre-fills a fabricated `0`).
  - A **`SeamBoundaryTests` guard** fails the build if any file reads assessment scores for display without referencing the `ErasureLedger` — making the disclosure structural, not convention (the RollupSnapshots-guard precedent). The guard covers **every** assessment-score read method (`GetAssessmentWithScoresAsync`, `GetByIdWithScoresAsync`, `GetSelfAsync`, `GetSelfScoresForCycleAsync`, `GetIndividualScoresAsync`, `ReadIndividualScoresAsync`, `GetAllForPersonAsync`). **Rollups are unaffected**: closed cycles read the frozen `RollupSnapshot` (never raw scores; retention never touches snapshots) and open cycles are never erased.
  - **Current-cycle-erased edge is handled by SILENT suppression, by design**: when the resolved "current cycle" is itself erased (a dormant platform), the self-form/result simply flag `DataErased` and show no values — there is no separate banner beyond the result-screen notice; this is intentional, not an oversight.
  - *(Hardening follow-up, optional)*: a raw `Set<AssessmentScore>()` query-surface guard for non-exempt seam files would add defense beyond the named-method guard — recorded, not required (the named-method guard covers all current display surfaces).

## Audit search (FR-050/FR-053)

`GET /api/admin/audit?subject=&action=&from=` (Platform Admin only) → `AuditQueryService`. AND-combined filters, newest first, capped at 500 rows. **Read-only by construction** — there is deliberately no write/update/delete audit route anywhere (asserted at the route table and in `AuditAppendOnlyTests`); an unknown `action` value is a 400.

## G1 privacy gate — readiness

Closing HAP-12 completes the evidence set for the **human-witnessed G1 privacy gate** (Art. VII). The rehearsal in `quickstart.md` "V3 — Privacy spot-checks" is automated end-to-end by `PrivacySpotChecksV3Tests` on a deterministic hierarchy carrying the DR-0005 canonical refs:

- zero individual reads outside the chain, exercised for all seven roles → 404 + no audit;
- DR-0005 one-hop direct read ALLOWED and audited across tiers (`HAP-PF-01 → HAP-GRP-01`, `HAP-GRP-01 → HAP-BUL-01`), a 2+-hop read DENIED (`HAP-PF-01 → HAP-SEED-IND`);
- DR-0006 contractor manager DENIED, the report escalating to the employee reviewer of record;
- HIG Executive sees aggregates only, never an individual score (FR-025 §2);
- a sub-4 team aggregate suppressed with no figures (SC-006);
- after an allowed view, the Platform-Admin audit search surfaces the `IndividualView` row.

The complement-differencing spot-check (V3 step 3) is exhaustively covered by `HierarchySuppressionTests` + `RollupDashboardTests` and remains a witnessed step in the doc. **M1 = zero leaks**; only the owner passes G1.

### G1 readiness package — the single-page witness checklist

The owner witnesses G1 against this one list — evidence to confirm, then the ratification decisions to make (all currently *provisional-in-effect* per CLAUDE.md §6.3, none blocking local work).

**Evidence the seam implements the ratified policy (confirm green):**

| What | Evidence (suite / doc) |
|---|---|
| Zero individual reads outside the chain, all 7 roles | `PrivacySpotChecksV3Tests` · SC-005 |
| Every [A] view audited exactly once; no audit-mutation route | `AuditCompletenessSweepTests` · SC-005 |
| DR-0005 one-hop direct read ALLOW; 2+-hop DENY | `PrivacySpotChecksV3Tests` (`DR0005_*`) |
| DR-0006 contractor manager DENY (escalates to employee RoR) | `PrivacySpotChecksV3Tests` (`DR0006_*`) + `TeamModerationEndpointsTests` |
| N<4 + complement suppression (incl. cross-level differencing) | `PrivacySpotChecksV3Tests`, `HierarchySuppressionTests`, `RollupDashboardTests` · SC-006 |
| Audit append-only (no update/delete/mutation surface) | `AuditAppendOnlyTests` + DB trigger `hap_audit_log_append_only` · FR-053 |
| Right-of-access export complete + discloses erasure (never a fabricated 0) | `AuditGdprEndpointsTests` (`Export_of_a_retention_erased_assessment_discloses_erasure…`) · FR-051 |
| Retention nulls raw values, retains rows, snapshots untouched, idempotent | `AuditGdprEndpointsTests` · FR-052 |
| **Erasure is PERMANENT** — no write path (incl. Q-022 late-override re-moderation) reverses it | `AuditGdprEndpointsTests` (`Late_override_re_moderation_of_a_retention_erased_assessment_is_refused…`) + `AssessmentEntityTests` (domain guard) · FR-052 |
| **Every raw-score READ discloses/refuses erasure** — no surface serves a fabricated 0 (member-read refuses; own result/prefill disclose) | `AuditGdprEndpointsTests` (`Member_read_…refused`, `Result_view_…discloses_erasure`, `Self_form_does_not_prefill_…`), `QaAdversarialHap12Tests` (`Dormant_platform_member_read_…`), structural `SeamBoundaryTests` (`Raw_score_display_reads_consult_the_erasure_ledger`) · FR-051/FR-052 |
| **Every WRITE path refuses an erased assessment** — moderation AND self-write | `AuditGdprEndpointsTests` (`Late_override_re_moderation_…refused`, `Self_write_to_a_retention_erased_assessment_is_refused…`) · FR-052 |
| **Erasure ledger fails CLOSED** on a corrupt Detail; writer-shape guarded | `AuditGdprEndpointsTests` (`Ledger_parse_fails_closed_…`, `Retention_erasure_audit_detail_round_trips_…`) · FR-052 |
| Audit Detail never carries a score value | `AuditGdprEndpointsTests` (`No_audit_row_detail_ever_carries_a_score_value…`) |

**Owner-ratification decisions the G1 witness must make (accumulated across the wave):**

| Item | Decision | Provisional-in-effect |
|---|---|---|
| **Q-020** | Senior-leader (no-moderator) auto-adopt behaviour | as shipped |
| **Q-024** | Hierarchy-derived AGGREGATE scope for Group/Portfolio/Exec (individual-read cap untouched) | ACCEPT-provisional |
| **Q-025** | FR-018 per-dimension level histogram vs. node-level floor distribution + per-dimension means (touches a binding mockup; a true per-dimension histogram reopens HAP-10 schema) | current shape |
| **HAP-11 residual** | Cross-cycle trend differencing assumption | as shipped |
| **HAP-11 residual** | The k=4 suppression floor | as shipped |
| **HAP-11 residual** | The laminar / fixed-hierarchy partition assumption | as shipped |
| **Q-027** | Persisted erasure marker — the non-nullable `SelfScore` int is zeroed (no migration); whether to add a nullable/tombstone `SelfScore` (or `Erased`) COLUMN is an L2 migration deferred to a new story. (Erasure *permanence* on the moderation path is NOT open — the ledger interlock closes it; this row is only the column design.) | zeroed |
| **Erasure cross-request TOCTOU** (durable-fix follow-up + G1 residual) | **(a) retention-vs-write TOCTOU** — `AssessmentScore` has no `xmin` token and retention never bumps `Assessment.xmin`, so a retention run committing in the sub-second window between a write path's ledger read and its store `SaveChanges` could write over just-erased rows → an EXPORT DESYNC (`DataErased:true` while a genuine value exists). Never a leak (no erased-data recovery; snapshots untouched); unreachable in the G1 witnessed sequential single-admin model. Durable fix (pick at follow-up): an `xmin` token on `AssessmentScore`, an in-transaction ledger re-check in the store write, or retention bumping the parent `Assessment.xmin`. *(The earlier dormant-platform `SetSelf` edge is now fully CLOSED — the self-write ledger interlock refuses a write to an erased assessment, and the read surfaces disclose/refuse; only this concurrency TOCTOU remains.)* | parked (concurrency-only) |

DR-0005 (one-hop above-BU direct read) and DR-0006 (contractor manager restrictive) are already owner-ratified decision records; G1 only confirms the seam implements them (rows above). **M1 = zero leaks**; only the owner passes G1. (This table is mirrored in the story closure notes at Phase 4.)
