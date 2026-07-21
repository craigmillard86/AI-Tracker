---
id: HAP-7
title: Cycle management — global monthly state machine, invitations, contractor exclusion
epic: E2-assessment
wave: 1
fr: [FR-002, FR-003, FR-004, FR-005, FR-006, FR-060]
risk: L2                # trigger: cycle state machine
status: done
estimate: {dev: M, qa: S}
worklog:
  - {phase: dev, start: 2026-07-21T21:37:36Z, end: 2026-07-21T22:34:28Z, mins: 56}
  - {phase: qa, start: 2026-07-21T22:36:47Z, end: 2026-07-21T22:50:52Z, mins: 14}
closure:
  sha: 983dddc
  files: [backend/src/Hap.Domain/Cycles/**, backend/src/Hap.Infrastructure/Cycles/**, backend/src/Hap.Infrastructure/Persistence/Migrations/20260721214132_AddCycleManagement*, backend/src/Hap.Api/CycleEndpoints.cs, backend/src/Hap.Api/AdminEndpoints.cs, backend/tests/**, docs/wiki/cycles-and-assessment.md]
  tests: "backend 298 (Api 209 incl. cycle + QA adversarial); PrivacyReporting 122; migration #3 idempotent + chains behind #2; framework-lock DB trigger rejects UPDATE/DELETE/re-parent of a locked version's content (closes HAP-6 A6); verify.sh ALL GREEN"
  risk: L2
  panel: [hap-code-reviewer, hap-domain-specialist]
  rounds: 2
  date: 2026-07-21
  note: "L2 panel over 2 rounds. Round-1 code-reviewer caught (and empirically proved) a re-parent bypass in the framework-lock trigger (UPDATE checked only NEW parent) — fixed to check OLD+NEW via TG_OP + a BEFORE DELETE guard, re-proven by both the reviewer and QA. Closes HAP-6's deferred A6 carry-forward (Cycle.Open() is Lock()'s first caller)."
  carry_forward:
    - "FR-054 framework-lock DB backstop is NOT complete for TRUNCATE (QA finding): the row-level BEFORE trigger rejects UPDATE/DELETE/re-parent but does NOT fire on TRUNCATE, and the app's `hap` role owns the framework tables (postgres bootstrap/owner), so it has TRUNCATE rights. Unreachable via any current HTTP path (nothing issues TRUNCATE outside test scaffolding). The next story that adds raw SQL near framework_versions/dimensions/level_descriptors — or a dedicated hardening pass — must add a BEFORE TRUNCATE statement-level trigger to all three (mirror HAP-3's audit_log_no_truncate) AND bracket TestSupport.ResetAsync's TRUNCATE of those tables in `SET session_replication_role='replica'` (the same coupling HAP-3 handled). Do NOT overstate the backstop as complete until this lands."
    - "PlatformAdmin late-override does NOT check target.IsActive (QA finding): a Platform Admin can grant a late-override for a DEPARTED person, while a Manager cannot (round-1 IsActive check). Examined and consistent with AC5's literal 'Platform Admin (any person)' — recorded as an intentional asymmetry, not a defect. Candidate one-line hardening (deny override for an inactive subject even for admin) if the owner prefers; a departed person cannot submit anyway. Owner's call."
    - "One-Open-per-framework is check-then-act with no DB constraint — a concurrency window under two concurrent admins (single-admin local build makes it low-risk); a future DB-level guard is complicated by the version→framework join (a simple partial unique index won't do). Noted in CycleService.OpenAsync."
    - "Q-016 (BU↔framework mapping): data-model.md models no BU↔framework junction, so OpenAsync invites every onboarded BU — a HARD BLOCKER for any second-framework story (would over-invite every BU to every framework's cycle). See QUESTIONS.md Q-016."
    - "Q-017a (submission lock) + Q-017 CloseAsync handoff: HAP-8/HAP-9 must consult Cycle.AllowsSubmission for post-close submission rejection; HAP-10 must hook auto-adoption/snapshots/suppression into CycleService.CloseAsync (currently a bare transition). See QUESTIONS.md Q-017. Q-017b: the BU-onboard mutation must get an AuditLog row before its flag feeds any reconciled participation/Harris figure (L3 obligation on the Wave-2 consumer)."
---
## Story
As a Platform Admin, I can open one global monthly cycle per framework whose invitations are derived automatically from the onboarded org (contractors excluded), locked at close with a manager-or-admin late override, so participation is mandatory, mechanical, and auditable.

## Context
- Spec: "Module 1: Assessment Framework & Cycles" FR-002 (global per framework; mid-cycle onboarding joins next cycle; lock at close; manager-or-admin override), FR-003 (invitations derived at open), FR-004 (no opt-out), FR-005/006 (contractor exclusion + override layer), FR-060 (monthly cadence); "Clarifications" bullet 4.
- Plan: data-model.md "Cycles & assessment" (Cycle, CycleInvitation — states Draft→Open→Closed forward-only, one Open per framework); contracts/api.md "[PA] POST /api/cycles…" and late-override endpoint. **Admin surface is API-only — no mockup exists (QUESTIONS.md Q-004); build no UI in this story.**
- Files: `backend/src/Hap.Domain/**` (Cycle state machine), `backend/src/Hap.Infrastructure/Persistence/**` (**EF migration #3**: Cycle, CycleInvitation), endpoints in `Hap.Api`.
- **Serialise with: HAP-6 (migration chain — this migration lands after HAP-6's).**
- Blocked by: HAP-3, HAP-6
- Parallelisable: no (migration chain)

## Acceptance criteria
- [ ] `POST /api/cycles` (Draft) then `/open`: invitations generated for every active, non-contractor person in onboarded BUs mapped to the framework — counts asserted against synth data; contractors get `excluded=true, reason=Contractor` and no invitation email row (FR-003/005).
- [ ] Opening a second cycle for the same framework while one is Open → 409 (FR-002 "one Open per framework" test).
- [ ] A BU onboarded while a cycle is Open gets no invitations until the next cycle open (FR-002 test: onboard mid-cycle, assert zero invites; open next cycle, assert invites).
- [ ] State machine is forward-only: Closed → Open rejected; Draft → Closed rejected (tests).
- [ ] After close, score submission is rejected (423 or 409) unless a late override exists; `POST /api/cycles/{id}/late-override` works for Platform Admin (any person) and for a Manager (own directs only — scope test).
- [ ] Contractor exclusion is per-cycle configurable (FR-005): opening with `contractor_exclusion_enabled=false` invites contractors (test).
- [ ] Cadence fields support monthly naming ("2026-08") and open/close dates; no scheduler in this story (notifications are HAP-18).
- [ ] `./scripts/verify.sh` green (migration idempotent).

## Attempts / notes

**Phase 1 setup (2026-07-21).** No prior attempt exists (`git log --all --grep "HAP-7"` empty before this
branch). Read spec.md FR-002/003/004/005/006/060 + Clarifications bullet 4, data-model.md "Cycles &
assessment", contracts/api.md [PA] cycle rows, QUESTIONS.md Q-004/Q-006/DR-0006. Confirmed Q-006/DR-0006
(contractor-manager individual-score access, restrictive) is unrelated to this story's contractor
*participation* exclusion (FR-005/006) — not conflated.

Two provisional assumptions recorded to QUESTIONS.md (next free numbers, per session lead: Q-016, Q-017) and
applied here rather than blocking:
- **Q-016** — "onboarded BUs mapped to the framework" (FR-002/003) is read as *every onboarded BU*, since
  data-model.md (the cited plan artifact) models no BU↔Framework junction table and this build seeds exactly
  one framework. No junction table added.
- **Q-017(a)** — AC 5's "score submission is rejected... unless a late override exists" is built as the LOCK
  PRIMITIVE this story owns (`Cycle.AllowsSubmission(bool hasLateOverride)` + a late-override existence query),
  tested directly against that primitive — not through an actual assessment-submission HTTP call, since
  Assessment/AssessmentScore are HAP-8's migration #4 (not in this story's file scope). **HAP-8/HAP-9 must
  consult this primitive when they build the real submission/moderation writes**, or post-close rejection will
  silently not exist; flagged in QUESTIONS.md for the owner to add to those stories' Context.
- **Q-017(b)** — `BusinessUnit.IsOnboarded` (HAP-3) has never had a write path. Since this story's own AC 3
  requires onboarding a BU as test setup and no other story owns it, HAP-7 adds a minimal
  `BusinessUnit.SetOnboarded(bool)` + `[PA] POST /api/admin/business-units/{id}/onboard` (unaudited — no
  `AuditAction` case fits and no cited FR calls for one). Fits the story's stated file scope
  (`Hap.Domain/**` + "endpoints in `Hap.Api`").

**Late-override scope-check design (per lead's instruction to reuse the HAP-5 seam, not reinvent it):** "own
directs only" is a DIRECT-report check (`OrgGraph.Find(targetId)?.ManagerPersonId == callerId`), deliberately
NOT `ChainResolver.GrantsIndividualRead` (which grants the whole upward ANCESTOR chain — too permissive here;
AC explicitly says "own directs only", not "anywhere in caller's chain"). Uses `OrgGraphLoader` (HAP-5 seam
infra) to load the structural graph. Out-of-scope manager attempts return 404 (matches contracts/api.md's
documented "out-of-scope person-addressed requests return 404, not 403" convention) rather than a new 403
path.

**FrameworkVersion.Lock() DB-layer guard (HAP-6 panel carry-forward, advisory A6):** HAP-6's panel flagged that
`Lock()` was in-memory/EF-only with "a DB-layer guard... should land with HAP-7, when Lock() gets its first
real caller." Cycle.Open() is that first real caller (locks the adopted FrameworkVersion) — migration #3 adds
a Postgres trigger backstop (mirrors the audit_log append-only trigger from migration #1) rejecting raw-SQL
writes to a locked framework_versions row or its dimensions/level_descriptors. Verified end-to-end against a
disposable Postgres before committing (lock/insert/update paths, forward/back/forward migration idempotence).
Closing this gap flipped two existing HAP-6 QA tests (`FrameworkLockBypassQaTests`) that had pinned the
*absence* of a DB guard as a documented finding — both renamed and their assertions inverted to prove the
bypass is now rejected; see their updated doc comments for the full history.

**Dev complete — verify.sh ALL GREEN (2026-07-21).** Built: Cycle/CycleInvitation/CycleLateOverride domain
entities (forward-only Draft→Open→Closed state machine; `Cycle.AllowsSubmission` pure lock primitive);
`CycleService` (Hap.Infrastructure.Cycles) for create/open/close/late-override, owning the one-Open-per-framework
check and invitation-generation snapshot at open; `[PA] POST /api/cycles`, `/open`, `/close`, and
`POST /api/cycles/{id}/late-override` (PlatformAdmin-or-own-manager, not admin-gated); `BusinessUnit.SetOnboarded`
+ `[PA] POST /api/admin/business-units/{id}/onboard`; EF migration #3 chaining behind HAP-6's, with the
FrameworkVersion-lock DB trigger. 39 domain + 9 architecture + 41 synth + 187 api = 276 backend tests green,
frontend unaffected and green. Full `./scripts/verify.sh` run: ALL GREEN, including idempotent migration
application (second `dotnet ef database update` = no-op) and the always-on PrivacyReporting suite (98 + 5 tests).
Did NOT clock out or change status — session lead runs the L2 panel first.

**L2 panel round 1 (2026-07-21).** `hap-domain-specialist`: **SIGN-OFF** (with advisories — Q-016 escalation,
Q-017 CloseAsync/HAP-10 handoff addendum, Q-017b audit-trail elevation, all applied below). `hap-code-reviewer`:
**CHANGES REQUIRED** — 2 blocking:
1. Real bug, empirically proven against a live Postgres: migration #3's `hap_framework_version_locked_guard`
   checked only the NEW parent on an UPDATE (NEW is never null there), so a raw SQL re-parent —
   `UPDATE dimensions SET "FrameworkVersionId" = <unlocked> WHERE "Id" = <dim-under-locked>` — moved a row OUT
   of a locked version undetected, after which it was freely mutable/deletable under its new, unlocked parent.
2. QUESTIONS.md record-integrity: Q-016/Q-017 were inserted before Q-014/Q-015's closing "RESOLVED... DR-0005"
   paragraph, making that paragraph read as a false second status for Q-017.

Risk confirmed **L2 by both** panel members (the manager-scope late-override predicate and the
framework-lock trigger both stay L2 — no red-team needed).

**Round-1 fixes applied (2026-07-21).**
- **Blocking #1 — migration #3 amended** (unshipped, so amending is legitimate rather than a new migration):
  the trigger now checks BOTH the OLD and NEW parent's lock state, gated on `TG_OP` (`IN ('UPDATE','DELETE')` /
  `IN ('INSERT','UPDATE')`) rather than the `COALESCE(NEW, OLD)` pattern that silently preferred NEW and was
  exactly the gap. Also adds the `framework_versions` `BEFORE DELETE` guard (advisory — FK Restrict already
  covers it in practice, but the trigger is now complete on its own terms). Verified by hand against a fresh
  disposable Postgres before committing: the reviewer's exact exploit (rejected), the reverse direction —
  re-parenting INTO a locked version (rejected), the new DELETE guard (rejected), and three legitimate
  unlocked-version operations (re-parent between two unlocked versions, delete an unlocked version, insert/
  update under a still-unlocked version — all still succeed, no regression). Pinned by a new test,
  `Raw_UPDATE_reparenting_a_dimension_out_of_a_locked_version_is_rejected_HAP7_L2_round1`
  (`FrameworkQaAdversarialTests.cs`), reproducing the exact bypass via raw SQL (`Dimension` has no setter, so
  reflection couldn't reproduce it — raw SQL is also the more faithful "bypass everything" repro).
- **Blocking #2 — QUESTIONS.md reordered:** Q-016/Q-017 moved to after Q-014/Q-015's RESOLVED paragraph, so
  each entry's status now sits with its own question.
- **Q-016 escalated:** no longer generic "OPEN, provisional" — now explicitly a **hard blocker for any
  second-framework story** (silent over-invite risk: `CycleService.OpenAsync` would invite every onboarded BU
  to every framework's cycle with no scoping mechanism).
- **Q-017 gained an addendum:** `CycleService.CloseAsync`'s doc comment and the QUESTIONS.md entry now
  obligate HAP-10 to hook its auto-adoption/snapshot/suppression work directly into `CloseAsync` (mirroring how
  Q-017a's late-override handoff was recorded), rather than building a parallel close path.
- **Q-017b elevated, not fixed here:** the BU-onboarding audit-trail gap is deliberately NOT closed in HAP-7
  (would pull an L2 story into the L3 audit-write path for an advisory disconnected from any current
  reconciliation consumer). Recorded as an obligation on whichever future Wave-2 story first makes
  `IsOnboarded` feed a reconciled participation/Harris figure — that addition is L3 regardless of how small the
  mutation looks.
- **Advisories applied:** `CycleEndpoints` late-override handler now uses `Guid.TryParse` + an explicit 500
  Problem for a missing `person_id` claim (matches `IdentityEndpoints.cs`'s convention exactly, replacing the
  `Guid.Parse(...!)` NRE risk); target-existence is now checked on the PlatformAdmin path too (previously a
  nonexistent `PersonId` fell through to an unhandled `DbUpdateException` → 500, now a clean 404); the manager
  path now also checks `target.IsActive` (mirrors `ChainResolver`'s own convention — a manager cannot grant a
  late override for a report who has since departed). `Manager_cannot_grant_a_late_override_outside_their_own_
  direct_reports` tagged `Category=PrivacyReporting`. Two new regression tests added for the target-existence
  and IsActive fixes. The one-Open-per-framework check-then-act concurrency window is noted (not built) in
  `CycleService.OpenAsync`'s doc comment, per the panel's explicit "note, don't build" instruction.
- **Verification:** 39 domain + 9 architecture + 41 synth + 190 api = 279 backend tests, 0 failures (+3 new
  tests vs. round 1's 276). `./scripts/verify.sh` ALL GREEN again: migration idempotent (second `dotnet ef
  database update` = no-op), PrivacyReporting suite 5 + 102 = 107/107 (+4 vs. round 1's 103, from the new
  re-parent test, two new late-override regression tests, and the newly-tagged existing test).

Still did NOT clock out or change status — awaiting both L2 members' re-review (code reviewer re-running its
Postgres re-parent probe; domain confirming sign-off stands over the migration-fix delta).

**L2 panel round 2 (2026-07-21) — COMPLETE at tip `7fbc331`, both sign-offs, zero blocking.**
- `hap-code-reviewer`: **SIGN-OFF** — re-ran its Postgres probe against the amended migration; all six illegal
  operations rejected (both re-parent directions, an in-place update on a locked row, the level_descriptor
  variants, and delete-on-locked), no false positives on legitimate unlocked-version operations; verify green;
  risk confirmed to stay L2.
- `hap-domain-specialist`: **SIGN-OFF** confirmed at `7fbc331` — the trigger fix strengthens FR-054; the
  late-override `IsActive` check is spec-consistent (a team is a manager and their *active* direct reports);
  all three domain advisories (Q-016 escalation, Q-017 CloseAsync/HAP-10 addendum, Q-017b elevation) recorded
  as applied.

Dev clocked out below; status → `qa`. Session lead owns two follow-ups at their own closure, not Dev's: (a)
rebasing HAP-7 onto current main to pick up the DR-0007 governance commit (`144f64a`) — trivial, disjoint from
this story's files; (b) carrying the Q-017a submission-lock handoff into HAP-8/HAP-9 story Contexts.

---

## QA (fresh instance, CLAUDE.md §9) — 2026-07-21

Fresh instance, no Dev/panel context. Re-derived correctness from the spec/data-model/contracts and the diff
itself (tip `e2246e3`); did not trust Dev's own narrative of what the tests prove. Worktree
`C:\git\hap-worktrees\HAP-7`, branch `HAP-7-fr-002-cycle-management`.

**Literal acceptance-criterion verification** (one check per clause, against the running system via
`CycleEndpointsTests.cs` — re-read line-by-line, not assumed — plus this QA pass's own tests):

| # | Clause | Verdict | Evidence |
|---|---|---|---|
| 1 | Invitations generated for every active non-contractor person in onboarded BUs; counts asserted vs synth data; contractor excluded=true/reason=Contractor/no email row | **PASS** | `Open_generates_invitations_with_the_expected_counts_and_per_row_shape` — 5 active people, 3 invited, 1 excluded-Contractor (`InvitedAt` null), 1 excluded-NotOnboarded; re-verified the counts arithmetic against the fixture by hand |
| 2 | Second Open for same framework while one Open → 409 | **PASS** | `Opening_a_second_cycle_for_the_same_framework_while_one_is_open_returns_409` |
| 3 | BU onboarded mid-Open gets no invites until next open | **PASS** | `Bu_onboarded_mid_open_cycle_gets_no_invitations_until_the_next_open` — snapshot row count and `Excluded` verified unchanged across the onboarding event, then flips at the *next* open |
| 4 | Forward-only: Closed→Open and Draft→Closed rejected | **PASS** | `State_machine_rejects_closed_to_open_and_draft_to_closed` (API) + full `CycleEntityTests.cs` domain coverage (both directions, repeat-state, both exception fields asserted) |
| 5a | Lock primitive: `AllowsSubmission` Open→true, Closed→only-with-override, Draft→never | **PASS** | `CycleEntityTests.cs` theory-covers all three states × override true/false (6 cases) |
| 5b | Actual submission HTTP call rejected 423/409 post-close without override | **NOT VERIFIABLE BY A COMMAND IN THIS STORY'S SCOPE** — no submission endpoint exists yet (Assessment/AssessmentScore are HAP-8's migration #4). Already correctly routed to QUESTIONS.md Q-017a by Dev at Phase 1 setup, with an explicit obligation on HAP-8/HAP-9 to consult `Cycle.AllowsSubmission`/`CycleService.HasLateOverrideAsync`. Re-confirmed this is the right disposition, not a gap to paper over: I did not (and could not) hit a real 423/409 here, and say so rather than treating the primitive's own domain-test pass as equivalent | — |
| 5c | Late-override: PlatformAdmin (any person) | **PASS** | `PlatformAdmin_can_grant_a_late_override_for_any_person` + this QA's own `PlatformAdmin_late_override_for_a_nonexistent_person_is_a_clean_404_not_500` (re-derived against a deeper fixture) |
| 5d | Late-override: Manager (own directs only) | **PASS** | Dev's `Manager_can_grant_a_late_override_for_their_own_direct_report` / `Manager_cannot_grant_a_late_override_outside_their_own_direct_reports` / `Manager_cannot_grant_a_late_override_for_a_departed_direct_report`, PLUS this QA's four deep-hierarchy escape attempts below (§ mandatory adversarial (a)) |
| 6 | `contractor_exclusion_enabled=false` invites contractors | **PASS** | `Contractor_exclusion_disabled_invites_the_contractor` — 4 invited incl. CONTRACTOR1, 0 excluded-Contractor |
| 7 | Cadence: monthly naming + open/close dates; no scheduler | **PASS** | `Cadence_name_and_open_close_timestamps_round_trip`; confirmed no notification/scheduling code exists anywhere under `Hap.Domain/Cycles` or `Hap.Infrastructure/Cycles` (grep clean) |
| 8 | `./scripts/verify.sh` green, migration idempotent | **PASS** | Full run below — ALL GREEN, `dotnet ef database update` run twice, second is a no-op |

**§9.3 note (mandatory for stories touching assessment data/rollups) — explicitly out of reach by
construction, not skipped silently:** no `Assessments`/`AssessmentScores`/`RollupSnapshot` tables exist in this
build yet (HAP-8/HAP-9/HAP-10 territory). The three mandatory attempts —(a) read a score outside the
management chain, (b) obtain an aggregate covering <4 people, (c) desynchronise a rollup from its records —
have no object to attack: there is no individual score, no aggregate, and no Harris figure anywhere in the
schema this story touches. Confirmed by inspecting the full migration set (`AddCycleManagement` adds only
`cycles`/`cycle_invitations`/`cycle_late_overrides` plus the framework-lock trigger) — nothing here is
assessment data. Re-scoped the adversarial effort to this story's own actual attack surface instead: the
late-override scope predicate and the framework-lock DB trigger, per the story brief.

**Mandatory adversarial attempts (a)–(d), each with attempt + outcome:**

**(a) Late-override scope, all seeded roles.** Existing dev suite only exercised one cross-BU escape
(flat MGR1/EMP1/EMP2 fixture). Built an independent deep hierarchy (ADMIN·MIDMGR→{MGR1,MGR2}·MGR1→EMP1·
EMP1→GRANDCHILD·MGR2→EMP3·EMP_OTHER_BU in BU02) and attempted, as MGR1:
  - Skip-level grandchild (GRANDCHILD, reports to EMP1 who reports to MGR1) → **404, correctly rejected**
    (`Manager_cannot_grant_a_late_override_for_a_skip_level_grandchild`)
  - Sibling-team report (EMP3, reports to MGR2, MGR1's sibling under MIDMGR) → **404, correctly rejected**
    (`Manager_cannot_grant_a_late_override_for_a_sibling_teams_report`)
  - Upward, own manager (MIDMGR) → **404, correctly rejected**
    (`Manager_cannot_grant_a_late_override_for_their_own_manager_upward`)
  - Cross-BU unrelated person (EMP_OTHER_BU, BU02) → **404, correctly rejected**
    (`Manager_cannot_grant_a_late_override_cross_BU_for_an_unrelated_person`)
  - Negative control: MGR1 granting for their OWN direct report (EMP1) → 200 OK, proving the fixture's
    check is discriminating, not failing closed universally (`Manager_CAN_grant_..._sanity_check`)
  - PlatformAdmin for a departed (inactive) person (GRANDCHILD, deactivated) → **200 OK — the PlatformAdmin
    path does NOT check `target.IsActive`** the way the Manager path does (round-1 advisory only touched the
    Manager path). Examined this deliberately rather than treating it as an oversight: AC5's literal text is
    "Platform Admin (any person)" — unqualified — vs. the Manager's explicit "own directs only" scope, so an
    admin overriding for a departed employee (e.g. correcting a late-discovered submission before their
    leaver's data ages out of the retention window) reads as in-scope, not a bug. Documented as a **confirmed,
    examined design asymmetry**, not a defect — but flagging it explicitly rather than leaving it implicit is
    itself the QA finding: if this asymmetry is NOT intended, it is a one-line fix in `CycleEndpoints.cs`'s
    PlatformAdmin branch (`Manager_cannot_grant_a_late_override_for_a_departed_direct_report`'s IsActive check,
    mirrored) and the session lead/owner should say so before closure. No violation found — outcome recorded,
    not glossed over.
  - PlatformAdmin for a nonexistent person → **404, clean, not 500** (re-confirmed against the deep fixture,
    independent of Dev's own equivalent test on the flat fixture)

  **Result: zero scope escapes found. Manager's "own directs only" holds against skip-level, sibling, upward,
  and cross-BU attempts; PlatformAdmin's "any person" holds and 404s cleanly for absent targets.**

**(b) Framework-lock trigger (migration #3, round-1 OLD/NEW-parent fix) — independent raw-SQL bypass
attempts via the app's own DB connection**, beyond the existing round-1 regression test (which only covers
dimension re-parent OUT):
  - Re-parent dimension INTO a locked version (from unlocked) → **rejected** (`FR-054` exception)
  - In-place UPDATE of a dimension's Name under locked, no re-parent → **rejected**
  - In-place UPDATE of a level_descriptor's DescriptorText under locked, no re-parent → **rejected**
  - Re-parent level_descriptor OUT of a locked dimension (DimensionId → unlocked) → **rejected**
  - Re-parent level_descriptor INTO a locked dimension (DimensionId → locked) → **rejected**
  - DELETE a dimension under locked, isolated from FK-Restrict noise with a descriptor-free fixture (so only
    the trigger, not the FK, could be stopping it) → **rejected by the trigger itself**
  - DELETE a level_descriptor under locked → **rejected**
  - DELETE a locked `framework_versions` row, isolated from FK-Restrict noise with a dimension-free fixture →
    **rejected by the trigger itself**
  - Legitimate unlocked-version ops (insert, in-place update, re-parent between two unlocked versions, delete)
    → **all still succeed — no false positives**, independently re-derived rather than trusting the panel's
    own round-2 confirmation
  - **Bypass FOUND, the round-1 fix could not have addressed it:** `TRUNCATE TABLE level_descriptors` against
    a locked version's seeded content **succeeds and empties the table** — Postgres row-level `BEFORE` triggers
    (what migration #3 installs) do not fire on `TRUNCATE`, only statement-level triggers do, and none is
    defined here. This is not a test-only artifact: docker-compose's `POSTGRES_USER=hap` is the
    `postgres:16-alpine` bootstrap role — the actual table owner — and it is the SAME role used identically
    for EF migrations and the running API (`ConnectionStrings__Hap`), so the app's own production-shaped local
    connection genuinely holds `TRUNCATE` rights on every guarded table. **No code path in this build issues
    `TRUNCATE` today outside `TestSupport.ResetAsync`** (explicit test-only scaffolding, which itself uses a
    different mechanism — `session_replication_role`, not privilege — for the same underlying reason: the
    guard is a per-row trigger, not a privilege restriction). Not exploitable through today's HTTP surface, so
    **not treated as a blocking defect for this L2 story** — but it is a real, proven residual gap in "the
    trigger is the backstop" framing, recorded here for whoever next adds raw SQL near these tables (an admin
    reset tool, a bulk-import job): the trigger stops row-level writes; it is not a substitute for withholding
    `TRUNCATE`/DDL privilege from the application's DB role. Test:
    `TRUNCATE_bypasses_the_row_level_lock_trigger_a_genuine_residual_finding` (deliberately asserts the bypass
    succeeds, not that it's rejected, so the finding is provable by running the suite, not just asserted in
    prose).

  **Result: every DELETE/UPDATE/INSERT bypass attempted is correctly rejected by the round-1-fixed trigger,
  including four directions the round-1 regression test itself didn't cover (into-locked re-parent, in-place
  update, and both level_descriptor re-parent directions). One genuine, structurally-unfixable-by-a-trigger
  bypass found (TRUNCATE) — documented as a residual finding, not blocking, per the reasoning above.**

**(c) One-Open-per-framework.** Re-read (not re-built, per the story brief's "needn't build a race test"
instruction) `Opening_a_second_cycle_for_the_same_framework_while_one_is_open_returns_409` and
`CycleService.OpenAsync`'s own doc comment recording the check-then-act concurrency window. Confirmed: the
sequential case is correctly 409'd; the documented (not built) race window is honestly disclosed in the code
comment rather than silently absent. No further action — the brief explicitly did not ask for a concurrency
test here.

**(d) Invitation generation integrity.** Attempted to make the invitation set disagree with active/onboarded/
contractor membership:
  - Duplicate invitation row for the same (cycle, person) via raw SQL INSERT → **rejected** by the unique
    index `IX_cycle_invitations_CycleId_PersonId`
    (`Raw_SQL_duplicate_invitation_row_for_the_same_cycle_and_person_is_rejected_by_the_unique_index`)
  - Invitation for an inactive person → confirmed the actual behaviour rather than assuming it:
    `CycleService.OpenAsync` filters to `IsActive` people before generating the snapshot, so an inactive
    person gets **zero rows at all** — neither `Invited` nor `ExcludedFor(...)` — since
    `InvitationExclusionReason` has no `Inactive` case to record it under. Matches the spec's leaver edge case
    ("no further invitations") but was previously unasserted; now covered
    (`Inactive_person_at_cycle_open_gets_no_invitation_row_at_all_not_even_excluded`).

  **Result: no disagreement between the invitation set and org membership found; the unique constraint holds
  against a direct raw-SQL duplicate-insert attempt.**

**QA verdict: PASS.** All eight acceptance-criterion clauses hold (5b is correctly out of reach by
construction, already routed to QUESTIONS.md by Dev, re-confirmed rather than re-litigated). No blocking
defects; one residual finding (TRUNCATE bypasses the row-level lock trigger — not exploitable via any current
HTTP path, not blocking) and one confirmed-intentional asymmetry worth an explicit owner nod
(PlatformAdmin late-override does not check target activity) are both recorded above for the session
lead/owner's attention at closure, not silently absorbed.

**Tests added (QA work, honestly attributed — none existed during the Dev window):** 19 new tests in
`backend/tests/Hap.Api.Tests/CycleQaAdversarialTests.cs` (15 tagged `Category=PrivacyReporting`: the four
late-override scope escapes, the PlatformAdmin-inactive and PlatformAdmin-nonexistent tests, all eight
trigger-bypass tests, the TRUNCATE finding, and the duplicate-invitation test; 4 untagged: the direct-report
sanity control, the legitimate-unlocked-ops confirmation, and the inactive-person-invitation test).

**Verification:** `./scripts/verify.sh` — **ALL GREEN**. Backend build clean (Release, warnings-as-errors).
Migrations applied twice; second run "No migrations were applied. The database is already up to date." (both
prior migrations plus this story's #3, including the trigger DDL, idempotent). Backend tests: 39 domain + 9
architecture + 41 synth + **209 api** (190 pre-QA + 19 new) = 298 total, 0 failures. PrivacyReporting suite:
5 architecture + **117 api** (102 pre-QA + 15 newly tagged) = 122, 0 failures. Frontend lint/typecheck/test/
build green, no external font request in the built output. Full log retained in the QA agent's scratchpad for
this session.
