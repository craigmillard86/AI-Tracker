---
id: HAP-18
title: Notifications — cycle reminders/escalations and weekly-update nags via mailpit
epic: E3-register
wave: 2
fr: [FR-037, FR-057, FR-061]
risk: L3                # trigger: reclassified 2026-07-23 (was L2) — see notes
status: in-progress    # bounced back 2026-07-23: L3 re-review panel found a new blocking leak on the fix — see "L3 RE-REVIEW PANEL"
estimate: {dev: M, qa: S}
worklog:
  - {phase: dev, start: 2026-07-23T15:00:46Z, end: 2026-07-23T15:41:38Z, mins: 40}
  - {phase: qa, start: 2026-07-23T17:59:43Z, end: 2026-07-23T18:15:03Z, mins: 15}
  - {phase: dev, start: 2026-07-23T18:34:24Z, end: 2026-07-23T19:00:58Z, mins: 26}
  - {phase: qa, start: 2026-07-23T19:11:46Z, end: 2026-07-23T19:24:55Z, mins: 13}
closure: null
---
## Story
As a non-responder, an overdue initiative owner, or an escalation recipient, I receive the right email at the right threshold — deterministic in test via an admin trigger, all captured in mailpit, none ever external.

## Context
- Spec: FR-037 (nag owner at 7d overdue, escalate BU Lead at 14d — active stages Evaluation→Scaled only; Idea/Retired exempt), FR-057 (email-only event list), FR-061 (cycle reminders to non-responders + escalation summaries to managers and BU Lead near close); root spec §4.2 "Weekly update discipline".
- Plan: research **D7** (MailKit → mailpit; hosted service on PeriodicTimer; `[PA] POST /api/admin/notifications/run` for determinism); contracts/api.md admin run endpoint; docker-compose mailpit from HAP-1 (SMTP 1025, UI/API 8025).
- Files: `backend/src/Hap.Infrastructure/Email/**` (MailKit sender + templates), notification job service (queries + sends), admin trigger endpoint. Email templates are content — externalised, not inline C# strings (FR-067 spirit; L1 trigger "email templates" is subsumed by this story's L2).
- No migration (idempotence via computed thresholds + a sent-log check against mailpit in tests; if a sent-record table proves necessary, STOP — that adds a migration and must serialise with the chain; note it here and coordinate).
- Blocked by: HAP-7, HAP-14
- Parallelisable: yes, with HAP-17 and HAP-19 (disjoint files)

## Acceptance criteria
- [ ] `POST /api/admin/notifications/run` executes all jobs once, synchronously, and reports counts per notification type (the deterministic test/demo path — research D7).
- [ ] Cycle reminders: non-responders in an open cycle receive a reminder with a deep link; submitted individuals receive nothing (FR-061 test asserting exact recipient set against mailpit API).
- [ ] Reminders and escalations fire at the configured thresholds (FR-061 as amended 2026-07-21 — defaults: non-responder reminders at 7, 3, and 1 days before close; escalation summaries from 3 days before close): each manager receives their team's incomplete list; BU Lead receives per-team summary (recipient + content assertions via mailpit API at each threshold).
- [ ] Weekly-update nags: initiative in Evaluation→Scaled with no update >7d → owner nag; >14d → BU Lead escalation listing all overdue initiatives; Idea/Retired initiatives trigger nothing (FR-037 tests per threshold using back-dated synth updates).
- [ ] Running the jobs twice in one day sends no duplicate emails (idempotence test via mailpit message count).
- [ ] All mail goes to the compose-network mailpit only; no external SMTP config exists (config assertion).
- [ ] Moderation-complete notification to the individual (FR-057 list) sends on transition (integration test).
- [ ] Wiki (DR-0003, at closure): create `docs/wiki/notifications.md`.
- [ ] `./scripts/verify.sh` green.

## Attempts / notes

**SPEC AUDIT 2026-07-21 (pre-start edit, story was todo):** reminder/escalation cadence made deterministic (configurable, defaults T-7/T-3/T-1 + escalations from T-3) — was "near close (within the configured window)" with no configured value anywhere.

**RISK RECLASSIFICATION 2026-07-23 (Dev, mid Phase 2, before writing the seam-adjacent code):** `risk: L2` → `L3`. Two independent §7 L3 triggers fire on this story's own diff, discovered while designing the cycle-reminder non-responder query:
- Cycle reminders (FR-061) need per-person submission state across the whole open cycle to find non-responders — the only sanctioned path is the seam's `IAssessmentStore.GetAssessmentsForPeopleAsync` (state only, no scores), but §7 lists "any read path over `Assessments`/`AssessmentScores` tables" as an L3 trigger independent of file location — a cycle-wide (not just own-team) caller of that method is a new read path over that table.
- Moderation-complete (FR-057) fires on the Submitted→Moderated transition inside `ManagerModerationService`, which lives under `backend/src/Hap.Api/Authorization/**` — an L3 trigger by file path alone, regardless of diff size.
- Team lead's ruling (2026-07-23): L3 confirmed, consistent with HAP-19 already being L3 for the same "who-hasn't-done-X participation read" shape — no QUESTIONS.md item needed, the §7 table + HAP-19 precedent settle it. Panel: hap-code-reviewer + hap-domain-specialist + hap-red-team; QA runs the mandatory §9.3 adversarial attempts.
- Guardrails agreed for the L3-touching parts: cycle-reminder state read goes ONLY through the sanctioned seam method (extend the seam interface under panel review if a cycle-wide read isn't already exposed cleanly — never a raw/parallel query); moderation-complete is a minimal side-effect hook fired after `ModerateAsync` commits, touching as few lines inside `Authorization/**` as possible, with no change to moderation decision/authorization logic; no individual data (scores, moderated values, another person's data) may ever reach an email body — only completion-state facts ("you have not submitted", "your assessment was moderated").
- FR-037 (weekly-update nags, Initiative register data, no seam) remains cleanly L2-shaped in isolation, but the story classification is per-story, not per-FR — the whole story now carries L3.

**SALVAGE + FR-061 BUILD 2026-07-23 (Dev, resumed after host-exhaustion reaped the builders mid-write).** On-disk salvage state at resume: FR-037 done+tested; FR-057 moderation-complete coded (`ModerationCompleteNotifier` + hook) but **untested**; FR-061 essentially **unbuilt** — only the sanctioned state-only seam read `IAssessmentStore.GetNonResponderPersonIdsAsync` existed (no consumer). Q-031 (the configured-intended-close decision) exists on `main` (edc92e9); the worktree branched before it, so the local QUESTIONS.md was stale — did NOT add a duplicate (it merges from main). Built FR-061 per the team-lead ruling (option A), OpensAt-offset:
- `backend/src/Hap.Api/Notifications/NotificationCadenceOptions.cs` — cadence as **config, no schema column** (Q-031): intended close = `Cycle.OpensAt + CycleLengthDays` (default 30); reminders {7,3,1}, escalations {3}. **No migration** (chain unchanged; verify step [4/9] idempotent-migration green).
- `backend/src/Hap.Api/Notifications/CycleReminderJob.cs` — Open cycle → invited/non-excluded (CycleInvitations) → **`GetNonResponderPersonIdsAsync` (sanctioned state-only seam read, person ids only, no scores)** → three notices. SeamBoundary stays green (verified: the new src files contain no standalone `Assessment` token / `Set<Assessment>` / display-read method, so no ErasureLedger dependency and no new query path over the score tables). Lives in `Hap.Api/Notifications/` (not the seam folder) — it is a *caller* of the sanctioned interface, not a new query path.
- 3 externalised templates (auto-embedded via the existing glob). **No score/individual data in any body**: reminders say only "you have not submitted"; manager escalation lists a reviewer's OWN reports' names + submission status (identical to the review-queue disclosure); BU-lead summary is **per-team COUNTS only, names no individual**.
- Wired into `POST /api/admin/notifications/run` + registered in Program.cs.

**RED-TEAM / PANEL — please scrutinise this added guardrail (beyond the brief):** the manager escalation is **gated on the reviewer holding individual-read capability**, reusing the seam's own `AssessmentReads.ClassifyReader` + `RoleScope.IndividualReadCapability`. So a capability-stripped explicit-grant reviewer-of-record (Platform Admin / HIG Exec / Group-Viewer) receives **no** escalation — exactly as `ManagerModerationService.GetReviewQueueAsync` excludes them — preventing a report's participation status reaching an aggregates-only role. Pinned by `CycleReminderJobTests.At_three_days_a_capability_stripped_reviewer_of_record_gets_no_manager_escalation`.

**FR-057 test added** (was coded, untested): `ModerationCompleteNotificationTests` — moderation emails the individual a notice with no score data (recipient + no-data property pinned) + a throwing-sender best-effort proof (mail failure never fails the committed moderation).

**Salvage-completeness fixes found at build time** (builder reaped mid-write): two never-reached `IAssessmentStore` test doubles (`CountingStore`, `NoStore`) and one direct `ManagerModerationService` construction (`The_member_read_is_fail_closed…`) had not been updated for the salvage's interface/ctor extensions — added the throwing member / the `notifier` arg. The reflection guard `Every_score_bearing_store_or_gateway_method_is_named_in_the_erasure_display_guard` correctly ignores the new method (returns `IReadOnlySet<Guid>`, not score-bearing) — re-confirms the state-only design.

**Verify (gate of record) — ALL GREEN** (`./scripts/verify.sh`, 2026-07-23): backend build warnings-as-errors; migrations idempotent (no new migration); Hap.Api.Tests **479 passed / 0 failed / 0 skipped** (0 skipped ⇒ the docker-mailpit idempotence test ran), Hap.Architecture.Tests 19 passed (SeamBoundary incl.), Domain 100, Synth 41; PrivacyReporting slice 268+14+13 passed; frontend lint/typecheck/test/build + no-external-font all green. Dev worklog 40 min. Status → qa. Panel next: hap-code-reviewer + hap-domain-specialist + hap-red-team (L3); then `hap-qa` §9.3. Wiki `docs/wiki/notifications.md` is a Phase-4 closure task (team lead) — not written here.

**L3 PANEL RESULT 2026-07-23:**
- `hap-code-reviewer`: **CHANGES-REQUIRED** (1 blocking) → **resolved** — the story's new privacy/disclosure-invariant tests carried no `[Trait("Category","PrivacyReporting")]`, so the always-run PrivacyReporting regression slice (the L3 merge gate + G1 evidence base) would not re-check HAP-18's disclosure invariants in future verifies. Fixed by tagging exactly the four disclosure-invariant tests (per-fact `[Fact]`/`[Trait(...)]` pair, matching the ~30-file convention): `CycleReminderJobTests.At_seven_days_reminders_go_to_non_responders_and_never_to_a_submitter`, `CycleReminderJobTests.At_three_days_a_capability_stripped_reviewer_of_record_gets_no_manager_escalation`, `CycleReminderJobTests.At_three_days_the_BU_lead_gets_a_per_team_count_summary_naming_no_individual`, `ModerationCompleteNotificationTests.Moderating_a_report_emails_the_individual_a_notice_with_no_score_data`. Deliberately NOT tagged: the plain threshold/cadence/idempotence/no-op tests in `CycleReminderJobTests`, the FR-037 register-data tests, and `ModerationCompleteNotificationTests.A_mail_send_failure_does_not_fail_the_moderation_write` (mailpit plumbing/best-effort-send, not a disclosure invariant).
- `hap-domain-specialist`: **SIGN-OFF**, three advisories (non-blocking): **A1** nag-threshold ambiguity → filed as Q-034 on `main`; **A2** FR-057 is only partially delivered by this story (see below); **A3** a minor C#/TS naming duplication between the notification-subject markers on either side of the stack, not correctness-affecting.
- `hap-red-team`: **NO PATH FOUND** — all three mandatory goals (read a score outside the chain; obtain an aggregate covering <4 people; desync a reported figure from underlying records) closed for this story's surface; the capability-gate guard (reviewer must hold individual-read capability to receive an escalation) holds at review-queue parity with `ManagerModerationService.GetReviewQueueAsync`. G1 not blocked by this story.
  - **G1-AWARENESS OBSERVATION (not a violation):** a BU-lead "Team led by X: 1 not yet submitted" line lets a BU lead infer a single-person team's non-submission. This is participation status the BU Lead is already entitled to under their existing seam reach (BusinessUnit-level), so it is not a leak — recorded here only so the owner has it in view at the G1 witness session, not as a defect.

**FR-057 SCOPE NOTE:** of the 7 events FR-057 lists, this story ships only **moderation-complete**. Cycle-open, assessment-completion, monthly-digest, and the remaining listed events are not implemented anywhere in the codebase yet. FR-057 is **partially** satisfied by HAP-18 — closure must not imply the full event list ships; a follow-up story is needed for the rest.

**Q-014 RECIPIENT-LABEL CLEARANCE:** FR-061's BU-lead summary uses `HierarchyRoleResolver`'s depth-derived labels to select email **recipients** (who gets the counts-only summary / whose team-manager names appear), not to gate individual-score **visibility scope**. This falls inside the Q-014 clearance carve-out for non-assessment uses of the resolver (the same basis HAP-13 used for register write-authority) — domain and red-team both confirmed the BU-lead summary discloses strictly less than what the BU Lead's existing entitlement already covers. Recorded so the resolver's "MUST NOT use for visibility scope until Q-014" caveat and this caller read as consistent, not contradictory.

**Q-034** (nag-threshold ambiguity, domain advisory A1) is filed on `main` — not duplicated here.

**PANEL-FIX ROUND 2026-07-23 (trait tags only):** applied the one blocking code-review fix above. No other panel-signed-off behaviour touched (nag threshold, FR-061 cadence, capability gate, BU-lead count-only summary all left exactly as reviewed). `[Trait("Category","PrivacyReporting")]` occurrence count in `backend/tests/Hap.Api.Tests/**` confirmed 114 → 118 (+4, matching the four tagged tests) by static grep before running verify; runtime PrivacyReporting pass-count delta to be confirmed against the pre-fix 268+14+13 baseline once verify.sh runs.

## QA (2026-07-23, `hap-qa`, fresh instance)

**VERDICT: FAIL — 2 blocking findings.** Both proven live against a disposable Postgres (targeted run, not the full `./scripts/verify.sh` — team-lead directed holding the full gate for known-buggy code; see "Verification method" below). Do not close this story until both are fixed and the two new regression-guard tests below go green.

### Finding 1 (BLOCKING, L3) — FR-061 BU-lead summary reaches a structurally unrelated person

`CycleReminderJob.SendBuLeadSummariesAsync` (`backend/src/Hap.Api/Notifications/CycleReminderJob.cs`) selects its recipient purely from `buLeadMap` — `IBuLeadResolver.ResolveBuLeadsByBusinessUnitAsync()` → `HierarchyRoleResolverBuLeadAdapter` → `HierarchyRoleResolver.ResolveAllAsync()`'s depth-from-root label. Unlike the sibling `SendManagerEscalationsAsync` (which correctly gates its recipient through `HasIndividualReadCapability`, reusing `AssessmentReads.ClassifyReader`/`RoleScope.IndividualReadCapability`), the BU-lead summary applies **no seam entitlement check at all** to the resolved recipient.

`HierarchyRoleResolver`'s own class doc names exactly this risk: "an interim/dual-hat layer, or a missing tier, breaks it… Callers MUST NOT use these labels for visibility scope until Q-014 is ratified or independently cleared for their use case." The story's own "Q-014 RECIPIENT-LABEL CLEARANCE" note (above) asserts the BU-lead summary "discloses strictly less than what the BU Lead's existing entitlement already covers" — that assertion is **only true when the depth label actually names the BU's real leader**. It does not always.

**Proof** (`CycleReminderQaAdversarialTests.BU_lead_summary_must_never_reach_a_hierarchy_mislabeled_person_with_no_structural_entitlement_to_the_BU`, `[Trait("Category","PrivacyReporting")]`): a fixture where a person (ROGUE) sits at hierarchy depth 3, is homed in BU_A, but manages a report in a completely different BU (BU_C) — nothing about ROGUE's real reporting line touches BU_A's team. ROGUE is nonetheless the unique person `HierarchyRoleResolver` labels "BU_A's BU Lead" (confirmed directly against `IBuLeadResolver`). Independently proven via `AssessmentReads.AuthorizeIndividualRead` that ROGUE is **denied** a direct read of the same subject (EMP_A1) the summary discloses a count about — i.e. ROGUE is demonstrably not already entitled, the opposite of what the clearance note assumes. Run live: ROGUE receives BU_A's real "Team led by MGR_A: 1 not yet submitted" summary — a stranger's inbox gets a report's aggregated assessment-participation status. This is a disclosure of individual-assessment-derived data (§9.3(b) attack) outside the management chain and outside any explicit grant — the exact class of leak Art. VI / G1 exists to prevent.

**Recommended fix shape** (for dev, not applied by QA): stop selecting the BU-lead-summary recipient from the unratified `HierarchyRoleResolver` label. Gate it the same way the seam gates every other BU-scope reader — on an explicit `OrgRole.BuDelegate` grant anchored to that BU (the same anchor `RoleScope.BuLead`/`AssessmentReads.ClassifyReader` already use everywhere else) — and fall back to "skip silently but count" (the existing vacant-lead pattern) when no such grant exists for a BU. Do **not** patch this by adding `HasIndividualReadCapability` alone: ROGUE classifies as a plain `Manager` (has a direct report), which **does** carry individual-read capability in the abstract (`IndividualReadReach.DirectReports`) — the missing check is structural connectedness to the BU, not role capability.

### Finding 2 (BLOCKING, same root cause, Register data not assessment data) — FR-037 BU-lead escalation shares the identical defect

`NotificationJobService.SendBuLeadEscalationsAsync` (`backend/src/Hap.Infrastructure/Notifications/NotificationJobService.cs`) resolves its recipient through the **same** `IBuLeadResolver.ResolveBuLeadsByBusinessUnitAsync()` call, with no entitlement check either. This discloses overdue-initiative business data (Register, not individual assessment scores) to the same mislabeled recipient, so it does not trip the constitution's Art. VI "individual assessment data" framing that made this story L3 — but it is a genuine recipient-targeting defect (§9.3(d)) sharing the exact code shape and the exact fix, so it should be fixed in the same pass.

**Proof** (`CycleReminderQaAdversarialTests.FR037_BU_lead_escalation_must_never_reach_a_hierarchy_mislabeled_person_with_no_relationship_to_the_BU`, not tagged PrivacyReporting — Register-data disclosure, not an assessment-privacy invariant): same ROGUE-shape fixture, an overdue initiative owned by someone in ROGUE's real chain (EXEC), 20 days stale. Run live: ROGUE receives the "Overdue weekly updates in BU_A" escalation despite zero relationship to BU_A's real initiative owner.

### Verification method

Per team-lead direction, did **not** run the full `./scripts/verify.sh` against known-buggy code. Instead: `dotnet build` (0 errors) to confirm the new test file compiles, then a **targeted run against my own disposable Postgres container** (migrations applied via `dotnet ef database update`, same shape as `verify.sh` steps 2–4, torn down after): ran the full `Hap.Api.Tests` project (485 tests) — **483 passed, 2 failed**, and the 2 failures are exactly the two new regression-guard tests above (proving both findings are real bugs, not test bugs, and that nothing else regressed from this QA pass's additions). Container removed after the run.

### §9.3 mandatory adversarial attempts — full record

**(a) Read a score outside the management chain via ANY notification surface, per seeded role; attack `admin/notifications/run` output for leakage.**
- Inspected all 6 email templates (`backend/src/Hap.Infrastructure/Email/EmailTemplates/*.txt`) verbatim: `cycle-reminder-nonresponder.txt`, `cycle-reminder-manager-escalation.txt`, `cycle-reminder-bu-lead-summary.txt`, `moderation-complete.txt`, `weekly-update-owner-nag.txt`, `weekly-update-bu-lead-escalation.txt`. None carries a score, a moderated value, evidence text, or another person's data — templates self-declare this and the tokens actually substituted (`EmailTemplateRenderer.Render`, a plain caller-supplied-token string replace, no auto-injection) match. Manager escalation names a manager's OWN reports' submission STATUS only (parity with the review queue); BU-lead summary names counts only.
- New test `Admin_notifications_run_response_carries_only_integer_counts_no_names_or_states` (PASS): the `POST /api/admin/notifications/run` response body is a flat `Dictionary<string,int>` — asserted directly (every value `JsonValueKind.Number`) and that no seeded name/email string appears in the raw response.
- **Outcome: no path found. CLOSED.**

**(b) Defeat the capability gate / get a report's participation status to a reviewer-of-record who lacks individual-read capability; attack the BU-lead count summary for a count reaching someone not already entitled.**
- Dev's own test proved this for Platform Admin as reviewer-of-record (`CycleReminderJobTests.At_three_days_a_capability_stripped_reviewer_of_record_gets_no_manager_escalation`). Extended the sweep: `Manager_escalation_gate_denies_a_HIG_Executive_reviewer_of_record` and `Manager_escalation_gate_denies_a_GroupViewer_reviewer_of_record` (both PASS) — the manager-escalation capability gate correctly denies all three capability-stripped explicit-grant shapes.
- Attacked the BU-lead count summary directly, as instructed: **FOUND Finding 1 above.** A count (of 1, the smallest non-suppressed unit) reached a recipient with zero structural entitlement to that BU's individual data.
- **Outcome: manager-escalation gate CLOSED (3/3 role shapes denied). BU-lead summary OPEN — Finding 1, BLOCKING.**

**(c) Seam bypass — confirm the ONLY assessment-state read is `GetNonResponderPersonIdsAsync`; look for any direct `Assessments`/`AssessmentScores` read in the new code.**
- Grepped `backend/src/Hap.Api/Notifications/**` for `IAssessmentStore` usage: the only call is `_store.GetNonResponderPersonIdsAsync(...)` in `CycleReminderJob.RunAsync` (person ids only, no scores, per its own interface doc).
- Grepped `backend/src/Hap.Infrastructure/Notifications/**` (FR-037) for `Set<Assessment`/`AssessmentScore`/`SelfScore`/`ManagerScore`: zero matches — `NotificationJobService` never touches assessment tables, only `Initiative`/`Person`/`BusinessUnit`.
- Confirmed `Hap.Architecture.Tests.SeamBoundaryTests.Assessment_types_and_query_surface_are_referenced_only_in_sanctioned_locations` (`Category=PrivacyReporting`, always-run) still structurally enforces this: a direct grep for `Set<Assessment` across `backend/src` found matches only in the three allowlisted seam files (`SeamAssessmentStore.cs`, `RollupPipeline.cs`, `CycleCloseProcessor.cs`), none in `Notifications/**`.
- **Outcome: no path found. CLOSED.**

**(d) Recipient targeting — a contractor-excluded person triggers/receives something; a reminder/escalation goes to someone outside the chain.**
- New test `A_contractor_excluded_person_receives_no_reminder_and_is_never_named_in_an_escalation` (PASS): a contractor's `CycleInvitation` row is confirmed `Excluded = true`; running the reminder job at both the T-7 and T-3 thresholds, the contractor receives no email and is never named in anyone else's escalation body.
- Extended search for "someone outside the chain": **FOUND Finding 2 above** (the FR-037 sibling of Finding 1) — the same unguarded `IBuLeadResolver` recipient-selection defect reaches Register-data escalations too.
- **Outcome: contractor exclusion CLOSED. FR-061 manager-escalation chain-targeting CLOSED (dev's own tests + capability sweep above). BU-lead-summary / FR-037 BU-lead-escalation targeting OPEN — Findings 1 and 2, BLOCKING.**

### AC clauses — literal verification

- [x] `POST /api/admin/notifications/run` runs all jobs once, reports counts per type — verified (dev test `Endpoint_reports_the_cycle_reminder_count_keys` + `Endpoint_runs_the_job_and_reports_counts_matching_the_dictionary_shape`; PlatformAdmin-gated, verified 403 for non-admin via `Endpoint_requires_Platform_Admin`).
- [x] Cycle reminders: non-responders reminded, submitters get nothing, exact recipient set asserted — verified (`At_seven_days_reminders_go_to_non_responders_and_never_to_a_submitter`).
- [~] Reminders/escalations fire at configured thresholds; each manager gets their team's list; **BU Lead receives per-team summary** — thresholds verified; manager-escalation recipient targeting verified CORRECT; **BU-lead summary recipient targeting is WRONG — Finding 1.**
- [x] Weekly-update nags at >7d/>14d, Idea/Retired exempt — verified (dev's `NotificationJobEndpointsTests`), **but the BU Lead escalation recipient targeting shares Finding 1's defect — Finding 2.**
- [x] Running jobs twice sends no duplicates — verified (`Running_twice_the_same_day_sends_no_duplicate_reminders`, `Running_the_job_twice_the_same_day_sends_no_duplicate_emails`, plus the real-mailpit idempotence tests).
- [x] All mail to mailpit only, no external SMTP — verified (`SmtpOptions` defaults, docker-compose `Smtp__Host=mailpit`, no other config surface anywhere in the codebase).
- [x] Moderation-complete notice sends on transition — verified (`Moderating_a_report_emails_the_individual_a_notice_with_no_score_data`).
- [ ] Wiki `docs/wiki/notifications.md` — not yet created (Phase-4 closure task per story notes; not due at QA).
- [ ] `./scripts/verify.sh` green — **not run this pass** (team-lead direction: hold the full gate for known-buggy code; targeted disposable-PG run above stands in as the live-DB proof for this QA pass).

### New tests added (QA work, honestly attributed)

`backend/tests/Hap.Api.Tests/CycleReminderQaAdversarialTests.cs` (new file):
- `BU_lead_summary_must_never_reach_a_hierarchy_mislabeled_person_with_no_structural_entitlement_to_the_BU` — `[Trait("Category","PrivacyReporting")]` — **FAILS today** (Finding 1 regression guard).
- `FR037_BU_lead_escalation_must_never_reach_a_hierarchy_mislabeled_person_with_no_relationship_to_the_BU` — **FAILS today** (Finding 2 regression guard).
- `Manager_escalation_gate_denies_a_HIG_Executive_reviewer_of_record` — `[Trait("Category","PrivacyReporting")]` — PASS.
- `Manager_escalation_gate_denies_a_GroupViewer_reviewer_of_record` — `[Trait("Category","PrivacyReporting")]` — PASS.
- `A_contractor_excluded_person_receives_no_reminder_and_is_never_named_in_an_escalation` — PASS.
- `Admin_notifications_run_response_carries_only_integer_counts_no_names_or_states` — PASS.

### Worklog

QA start wallclock timestamp was not captured at the start of this pass (work began immediately on assignment without writing `.wallclock-HAP-18-qa` first). Per CLAUDE.md §12 ("Lost the timestamp? Log nothing and say so. Never back-fill."), **no QA worklog entry is appended for this pass** — this is a gap in this pass's telemetry, recorded here rather than smoothed over. A `.wallclock-HAP-18-qa` will be started properly for the re-verification pass after the dev fix.

## L3 PRIVACY FIX 2026-07-23 (Dev, `dev-hap18d`) — resolves QA Findings 1 & 2

Both QA blocking findings share one root cause: the BU-lead notification recipient was selected from `HierarchyRoleResolver`'s depth-from-root label (via `IBuLeadResolver` → `HierarchyRoleResolverBuLeadAdapter`), which the seam itself never trusts for visibility scope (Q-014). On a non-uniform tree that label can name a structurally-unrelated person as a BU's lead, leaking that BU's data to a stranger.

**Fix — gate the recipient on the seam's own BU-anchor (an explicit BU-scoped `OrgRole.BuDelegate` grant), not the depth label.** Investigated the seam first: `AssessmentReads.ClassifyReader` promotes ONLY a `BuDelegate` grant to `SeamRole.BuLead` (→ `IndividualReadReach.BusinessUnit`); the depth label is consulted nowhere in the read path (HAP-13's `RegisterEndpoints.ResolveWritableBusinessUnitAsync` uses the hierarchy tiers only for *register-write* authority, not individual-data reads). So the only recipient the seam would permit to read a BU's members is a BU-anchored `BuDelegate` grant holder.

- New `backend/src/Hap.Infrastructure/Notifications/RoleGrantBuLeadResolver.cs` implements `IBuLeadResolver` by reading `BuDelegate` grants (re-read from DB, HAP-4 A3) → one recipient per BU (deterministic: earliest grant, then person id); a BU with no such grant is absent → caller's existing "skip silently but count" fallback fires. Wired in `Program.cs` in place of the old adapter.
- Deleted `backend/src/Hap.Api/Identity/HierarchyRoleResolverBuLeadAdapter.cs` (the depth-label impl) — now dead + unsafe for this use.
- Both consumers fixed identically by construction (they share the one port): `CycleReminderJob.SendBuLeadSummariesAsync` (FR-061, the L3 assessment-participation leak — Finding 1) and `NotificationJobService.SendBuLeadEscalationsAsync` (FR-037 register-data escalation — Finding 2). NOT a plain `HasIndividualReadCapability` check: per QA's clarification a rogue with a direct report classifies as `Manager` (has capability in the abstract) — the missing guard is structural connectedness to the SPECIFIC BU, which the `BuDelegate` anchor provides.
- Nothing else the L3 panel cleared was touched: the manager-escalation capability gate, the sanctioned state-only seam read, the cadence, and the "no individual/score data in email bodies" property are all unchanged.

**Two panel-cleared POSITIVE tests re-anchored (not weakened):** `CycleReminderJobTests.At_three_days_the_BU_lead_gets_a_per_team_count_summary_naming_no_individual` and `NotificationJobEndpointsTests.Fifteen_days_triggers_one_escalation_to_the_BU_Lead...` previously made their BU lead (`BULEAD_A`) a depth-label lead with no grant — which the fix (correctly) no longer treats as entitled (`BULEAD_A` is a skip-level manager the seam would also deny a direct read to). Each now grants `BULEAD_A` an explicit `BuDelegate` over `BU_A`, so the positive case establishes a genuinely seam-entitled BU lead. The negative-count tests (`A_BU_with_no_resolvable_BU_Lead...`, `Exactly_fourteen_days...`, endpoint counts) were unaffected and left as-is.

**Supersedes the earlier "Q-014 RECIPIENT-LABEL CLEARANCE" note above:** the assessment-participation BU-lead summary recipient is NOT within the Q-014 non-assessment carve-out (QA proved the mislabeled recipient is denied a direct `AuthorizeIndividualRead` of the same subject). It now uses the structural `BuDelegate` anchor, not the resolver's depth label.

qa-hap18's two regression tests (`BU_lead_summary_must_never_reach_a_hierarchy_mislabeled_person...` [PrivacyReporting], `FR037_BU_lead_escalation_must_never_reach_a_hierarchy_mislabeled_person...`) are satisfied unchanged — ROGUE holds no `BuDelegate` grant, so its BU is skipped and it receives nothing. Fresh `.wallclock-HAP-18-fix` started; verify.sh run + counts + SHA to follow on green.

## QA RE-VERIFICATION 2026-07-23 (`hap-qa`, fresh instance, post-L3-fix)

**VERDICT: PASS — the invariant holds under the new BU-anchored mechanism. `./scripts/verify.sh` → ALL GREEN (exit 0). No production code touched (test file only).**

### The invariant, independently re-confirmed (not taken on faith)

Guarantee under test: *a person who is NOT a BU-anchored `BuDelegate` for BU_X must NEVER receive BU_X's FR-061 non-responder summary or FR-037 overdue-initiative escalation, however the hierarchy would label them.* Re-derived from the seam, not from Dev's claim: `AssessmentReads.ClassifyReader` promotes ONLY a BU-scoped `OrgRole.BuDelegate` grant to `SeamRole.BuLead` → `IndividualReadReach.BusinessUnit`; the depth label is consulted nowhere in the read path. `RoleGrantBuLeadResolver` selects the recipient from exactly that anchor, so both notification consumers (`CycleReminderJob.SendBuLeadSummariesAsync` FR-061, `NotificationJobService.SendBuLeadEscalationsAsync` FR-037) now share the seam's own entitlement rule. Confirmed the old depth-label impl (`HierarchyRoleResolverBuLeadAdapter`) is deleted and `Program.cs` wires `RoleGrantBuLeadResolver`.

### (A) The two original blocking findings are closed — proven live, guards reached

Both `BU_lead_summary...` (line 220 `Assert.Null(leaked)`) and `FR037_BU_lead_escalation...` (line 453 `Assert.DoesNotContain(...rogueEmail)`) now REACH and PASS their real security guards: ROGUE (depth-3, home BU_A, no `BuDelegate` grant) receives nothing. These are no longer premise-blocked.

### (B) Obsolete premises reconciled WITHOUT weakening — the vector is still proven real

The old premise `Assert.True(map.TryGetValue(buAId, out leadId)); Assert.Equal(rogueId, leadId)` asserted the resolver would surface ROGUE — no longer true (correctly). Rewritten to a two-part premise that keeps full adversarial teeth:
- **Premise 1a (vector still latent):** `HierarchyRoleResolver.ResolveAllAsync()[rogueId].BuLeadOfBusinessUnitId == buAId` — the hierarchy STILL depth-mislabels ROGUE as BU_A's lead. The Q-014 vector was NOT fixed away; it lives on in the resolver. The guard therefore proves a live gap is closed, not a dead one.
- **Premise 1b (port refuses to surface it):** `IBuLeadResolver.ResolveBuLeadsByBusinessUnitAsync()` has no BU_A entry and ROGUE appears in no map value — the exact gap the fix closes.
- **Premise 2 kept unchanged:** `AssessmentReads.AuthorizeIndividualRead(Ungranted(ROGUE), EMP_A1)` is denied — ROGUE is genuinely unentitled, so surfacing the mislabel would be a real leak. No security assertion softened.

### (C) Teeth added — positive/negative pair rules out a "returns-nobody trivially passes" resolver

- **`FR061_a_BuDelegate_grant_over_the_wrong_bu_confers_no_entitlement_to_another_bu`** `[PrivacyReporting]` (NEW): ROGUE granted `BuDelegate` over the WRONG BU (BU_C) — a live grant (premise asserts `map[BU_C]==ROGUE`) — STILL receives nothing about BU_A. Proves the anchor is the SPECIFIC BU, not "any `BuDelegate` grant anywhere".
- **`FR061_a_BuDelegate_anchored_on_the_bu_receives_that_bus_summary`** `[PrivacyReporting]` (NEW): RA (homed in BU_A, real chain) granted `BuDelegate` over BU_A DOES receive BU_A's per-team count summary (`map[BU_A]==RA`; a "by team"/"BU_A BU" message arrives; body has "not yet submitted" counts, names no individual). Proves the recipient path is LIVE, not a resolver that safely returns nobody. Both directions now have teeth.

### (D) §9.3 mandatory adversarial attempts (re-run against the new mechanism)

- **(a) Read a score / individual data outside the chain via any notification surface, per role:** No path. Email bodies carry only completion-state facts and counts (templates unchanged, re-inspected); `Admin_notifications_run_response_carries_only_integer_counts_no_names_or_states` still passes (line 462 area — flat `Dictionary<string,int>`, no name/email in payload). **CLOSED.**
- **(b) Obtain an aggregate covering <4 people / defeat the recipient gate:** The BU-lead summary's smallest unit (a count of 1) can now reach ONLY a BU-anchored `BuDelegate` — someone `AuthorizeIndividualRead` already permits for that BU. Attempted the mislabel vector (ROGUE) directly and via a wrong-BU grant (BU_C): both receive nothing about BU_A. Manager-escalation capability gate still denies HIG-Exec/GroupViewer/PlatformAdmin reviewers-of-record (existing tests green). **CLOSED — no leaked number.**
- **(c) Desynchronise a figure from records:** N/A to new code beyond (a)/(b); no reported rollup/Harris figure in this surface. The BU-summary counts derive from the same sanctioned state-only non-responder read; no independent-recount disagreement possible. **No path.**
- **G1-awareness (unchanged from prior pass):** a legitimate BU-anchored `BuDelegate` seeing "1 not yet submitted" for a single-person team is participation status already within that lead's `BusinessUnit` reach — not a leak; noted for the owner's G1 witness session.

### Verify (gate of record) — ALL GREEN (exit 0), full `./scripts/verify.sh`

Backend build warnings-as-errors 0 errors; migrations idempotent; **Hap.Api.Tests 487 passed / 0 failed / 0 skipped** (+2 vs prior QA pass = the two new teeth tests; the two reconciled premises kept their count), Hap.Architecture 19, Hap.Domain 100, Hap.Synth 41; PrivacyReporting slice **Api 277 + Architecture 14 + Domain 13 passed**; frontend lint/typecheck/test/build + no-external-font all green. Only `backend/tests/Hap.Api.Tests/CycleReminderQaAdversarialTests.cs` changed — no production edit.

### AC clauses — literal re-verification (post-fix)

- [x] `POST /api/admin/notifications/run` runs all jobs once, reports counts per type — verified (endpoint tests + `Admin_notifications_run...` payload-shape guard).
- [x] Cycle reminders: non-responders reminded, submitters get nothing, exact recipient set — verified.
- [x] Reminders/escalations at configured thresholds; each manager gets their team's list; **BU Lead gets per-team summary** — now verified CORRECT for the BU-lead summary: recipient is a BU-anchored `BuDelegate` (positive test), and a mislabeled/wrong-BU person is refused (guard + negative test). **Finding 1 closed.**
- [x] Weekly-update nags >7d/>14d, Idea/Retired exempt; **BU-Lead escalation recipient** now BU-anchored — **Finding 2 closed** (guard reaches line 453 and passes).
- [x] Running jobs twice sends no duplicates — verified (idempotence tests green in full run).
- [x] All mail to mailpit only, no external SMTP — verified.
- [x] Moderation-complete notice on transition — verified.
- [ ] Wiki `docs/wiki/notifications.md` — Phase-4 closure task (not due at QA).
- [x] `./scripts/verify.sh` green — **verified this pass (exit 0, ALL GREEN).**

### New tests added this pass (QA work, honestly attributed)

In `backend/tests/Hap.Api.Tests/CycleReminderQaAdversarialTests.cs`:
- `FR061_a_BuDelegate_grant_over_the_wrong_bu_confers_no_entitlement_to_another_bu` — `[Trait("Category","PrivacyReporting")]` — NEW, PASS (negative mis-anchor teeth).
- `FR061_a_BuDelegate_anchored_on_the_bu_receives_that_bus_summary` — `[Trait("Category","PrivacyReporting")]` — NEW, PASS (positive live-path teeth).
- Reconciled premises (no security assertion weakened): `BU_lead_summary_must_never_reach_a_hierarchy_mislabeled_person...` [PrivacyReporting] and `FR037_BU_lead_escalation_must_never_reach_a_hierarchy_mislabeled_person...` — both PASS, guards reached.

### Worklog

QA start `.wallclock-HAP-18-qa` = 2026-07-23T17:59:43Z; close 2026-07-23T18:15:03Z; 15 min (under the S estimate). Entry appended to frontmatter.

## L3 RE-REVIEW PANEL 2026-07-23 (lead-convened, on the fix delta `e49f034..1853a4a`) — RESULT: BLOCKING

The fix (`7002462`) is NEW L3 code on the exact surface the original panel cleared, and the original `hap-red-team` "NO PATH FOUND" was falsified by QA — so the L3 panel was re-run against the fix delta (all fresh instances). Reviewers are read-only; the lead transcribes their verdicts here per their request.

- **`hap-domain-specialist`: SIGN-OFF.** BuDelegate is the spec-faithful "BU Lead" recipient for FR-061/FR-037 near-close summaries; fail-closed dormancy on synth (Q-035, no BuDelegate grants seeded) is spec-acceptable (privacy-correct delivery gap, not a swallowed error). No new QUESTIONS.md item beyond Q-035. FR-057 remains PARTIAL (advisory A2, unchanged).

- **`hap-code-reviewer`: CHANGES-REQUIRED — 2 blocking. Sign-off refused.** Risk class independently re-derived **L3** (match). Verified-good: deleted adapter truly dead (zero refs; `Program.cs:73` wires the new resolver), deterministic multi-delegate selection, grants re-read from DB (HAP-4 A3), no-individual-data-in-bodies untouched, conventions + green-verify record confirmed. BLOCKING:
  1. `RoleGrantBuLeadResolver.cs:43` — **grant-precedence not mirrored.** `AssessmentReads.ClassifyReader` (`AssessmentReads.cs:218-242`) ranks HigExecutive→PlatformAdmin→GroupViewer **above** BuDelegate and those roles strip individual-read capability (`RoleScope.cs:87-90`). A person holding a BU-anchored `BuDelegate` **plus** any of those three is denied `AuthorizeIndividualRead` for that BU — yet the resolver still selects them, so the FR-061 summary (smallest unit: a count of 1) reaches a seam-denied reader. Same leak class as QA Finding 1; dual-grant state is reachable (append-only grants; `LocalDevProvider` accumulates roles per person). Fix shape: exclude candidates also holding HigExec/PlatformAdmin/GroupViewer (mirror `ClassifyReader` precedence, comment binding the two), correct the now-false class-doc claim, add a `PrivacyReporting` teeth test (dual-granted delegate receives nothing).
  2. `RoleGrantBuLeadResolver.cs:42` (+ unfiltered lookups `CycleReminderJob.cs:180-184`, `NotificationJobService.cs:90`) — **`ReaderEligible` not replicated.** The seam's active/non-contractor conjunct (`AssessmentReads.cs:244-255`) is enforced nowhere on the notification path; since grants are append-only (no revocation — `RoleGrant.cs`), **deactivation is the only mechanism that ends a delegate's entitlement**, yet a deactivated delegate still gets mailed the summary/escalation. Fix shape: join `People`, require `IsActive` (and honour the contractor policy for the FR-061 assessment-participation consumer); add a deactivated-delegate regression test.
  - Advisory: "earliest grant wins" ordering will be wrong once revocation-by-superseding-record lands — add a coupling comment.

- **`hap-red-team`: PATH FOUND — CHANGES-REQUIRED (1 blocking). Supersedes the prior "NO PATH FOUND".** Independently found the identical co-grant precedence hole and provided a runnable failing test (`FR061_a_BuDelegate_who_also_holds_a_capability_stripping_grant_must_not_receive_the_summary`, `[Trait("Category","PrivacyReporting")]`) that trips `Assert.Null(leaked)` today: RA holding `BuDelegate(BU_A)` + `GroupViewer` receives BU_A's "Team led by MGR_A: 1 not yet submitted" line while `AuthorizeIndividualRead(RA, EMP_A1)` denies. Hits **two** sacred guarantees at once — read outside the chain (Goal 1) **and** defeat N<4 (Goal 2, the n=1 count to an aggregates-only role). Also confirmed the secondary `ReaderEligible` gap (contractor/inactive delegate), affecting **both** consumers (FR-061 + FR-037). Recommended fix: bind the recipient predicate to the one seam authority — after the resolver produces a candidate, re-classify via `ClassifyReader` over the candidate's **full** grant set (reuse `CycleReminderJob.HasIndividualReadCapability` / the `ManagerModerationService.GetCallerContextAsync` pattern) and keep only `SeamRole.BuLead` anchored on that BU + `ReaderEligible`. Data classification: **FR-061 path = assessment-participation (Art. VI / PrivacyReporting)**; **FR-037 sibling = Register data** (not PrivacyReporting-tagged).
  - Tertiary nuance (note, not blocking): a person holding two `BuDelegate` grants over different BUs — resolver lists them for both, but `ClassifyReader.FirstOrDefault` honours only the first.

**Convergence:** code-reviewer and red-team independently reached the SAME root cause — the fix replicated only the *anchor* conjunct of the seam's read predicate and dropped *precedence* and *eligibility*. Confirmed L3 blocking. Lead ADJUDICATION: both blocking notes accepted; bounce to a fresh Dev fix pass. Status → in-progress.

**Layering constraint for the fix (lead note):** `RoleGrantBuLeadResolver`/`NotificationJobService` live in `Hap.Infrastructure`, which must NOT depend on `Hap.Api.Authorization` (dependency direction is Api→Infrastructure). The FR-061 gate therefore belongs in the **consumer that can see the seam** — `CycleReminderJob` (Hap.Api), exactly where the manager-escalation path already gates via `HasIndividualReadCapability`. The FR-037 escalation recipient (register data, NOT Art. VI) needs the eligibility/active-contractor conjunct and a register-authority-consistent recipient rule, resolved without importing the assessment seam into Infrastructure. Design the seam-parity gate on the Api side; keep the Infrastructure resolver a candidate producer.

**G1 posture (surface transparency, for the owner):** DR-0009 (G1 PASSED 2026-07-23) was witnessed against the app score-read paths on synthetic data, where BU-lead notifications are dormant (Q-035, no BuDelegate grants seeded) — so this notification recipient vector did NOT fire at that witness and the witnessed zero-leak result stands for what it covered. This is a distinct latent path found post-witness; the fix closes it and strengthens the G1 posture. It does not, by itself, invalidate DR-0009, but the notification recipient surface should be considered G1-covered only once this fix is green. Flagged for the owner; no re-witness assumed without owner direction.

## L3 FIX ROUND 2 (2026-07-23, Dev `dev-hap18e`) — resolves the L3 re-review co-grant-precedence + reader-eligibility findings

Both re-review blocking notes shared one root cause: the earlier fix bound the BU-lead recipient to only the **anchor** conjunct of the seam's read predicate (holds a BU-scoped `BuDelegate` grant), dropping **precedence** and **eligibility**. Fixed by binding the recipient to the ONE seam authority rather than re-encoding a subset.

**Precedence + eligibility gate — FR-061 (`CycleReminderJob`, Hap.Api).** The resolver stays a candidate PRODUCER; `SendBuLeadSummariesAsync` now passes each resolved candidate through new `BuLeadPassesSeamReadGate`, which calls `AssessmentReads.AuthorizeIndividualRead(graph, candidateContext, subject)` over the candidate's **full** grant set (re-read from `RoleGrants`, HAP-4 A3). Because that one call is the seam's real predicate, it enforces all three conjuncts at once: (1) **precedence** — a co-held `HigExecutive`/`PlatformAdmin`/`GroupViewer` grant makes `ClassifyReader` classify the candidate as the higher, capability-stripped role → denied; (2) **eligibility** — the seam's private `ReaderEligible` (active, non-contractor under the Q-006 Restrictive default) runs inside `AuthorizeIndividualRead` → a deactivated/contractor delegate is denied; (3) **specific-BU anchor** — reach is checked against the subject's BU. A denied candidate falls into the existing "skip silently but count" vacant-lead path.

**Reader-eligibility gate — FR-037 (`NotificationJobService`, Hap.Infrastructure).** Register data, no assessment seam available in this layer. Added `IsEligibleRecipient(Person)` = `IsActive && EmployeeType != Contractor` — plain `Person` attributes (NOT the assessment seam), so the Api→Infrastructure layering holds. The recipient stays `BuDelegate`-anchored (consistent with HAP-13 register write-authority: a `BuDelegate` grant is the BU curator). Precedence stripping is intentionally NOT applied here — register data is broadly readable, so an above-BU co-grant is not a disclosure concern; the reviewers flagged only the eligibility gap for FR-037.

**Layering decision (2–3 sentences).** The FR-061 gate lives in the Api consumer (`CycleReminderJob`) because only Hap.Api can reference the assessment seam (`AssessmentReads`), and binding to the seam itself is strictly stronger than re-encoding its conjuncts — it can never drift from the read path it mirrors. The FR-037 gate lives in the Infrastructure consumer over plain `Person` attributes, since Hap.Infrastructure must not depend on Hap.Api.Authorization and `IsActive`/contractor are not seam concepts. The shared `RoleGrantBuLeadResolver` remains a pure candidate producer (class-doc corrected: the BuDelegate anchor is necessary but NOT sufficient); a coupling comment ties the "earliest grant wins" ordering to the future revocation-by-superseding-record story.

**Tertiary nuance (two `BuDelegate` grants over different BUs).** Handled for free by binding to `AuthorizeIndividualRead`: `ClassifyReader.FirstOrDefault` anchors the candidate to their first `BuDelegate` grant, and the reach check then compares the SUBJECT's BU against that anchor — so for the non-anchored BU the seam denies the read and the candidate receives no summary, exactly as the seam would refuse them a direct read of that BU. The gate is thus consistent with the specific BU being notified, no special-casing required.

**TDD — 6 new Dev-attributed tests in `CycleReminderQaAdversarialTests.cs`, all RED before the fix, GREEN after** (verified by stashing the production src and running the filtered set against a disposable Postgres: **Failed 6 / Passed 0** pre-fix; the fix flips all to green in the full run):
- `FR061_a_BuDelegate_who_also_holds_a_capability_stripping_grant_must_not_receive_the_summary` `[Theory]` `[PrivacyReporting]` — GroupViewer / HigExecutive / PlatformAdmin (3 cases; the red-team supplied the GroupViewer fixture). Premises assert the resolver still surfaces the candidate AND `AuthorizeIndividualRead` denies them.
- `FR061_a_deactivated_BuDelegate_must_not_receive_the_summary` `[PrivacyReporting]`.
- `FR061_a_contractor_BuDelegate_must_not_receive_the_summary` `[PrivacyReporting]`.
- `FR037_a_deactivated_BuDelegate_must_not_receive_the_escalation` (Register data — deliberately NOT `[PrivacyReporting]`).
- Positive path preserved (not weakened): `FR061_a_BuDelegate_anchored_on_the_bu_receives_that_bus_summary` and `NotificationJobEndpointsTests.Fifteen_days_...` (active employee `BuDelegate`) both stay green — the recipient path is proven live, not dead.

**No `backend/src/Hap.Api/Authorization/**` logic touched** — the fix consumes the seam only via its existing public/internal API (`AuthorizeIndividualRead`, `ClassifyReader`, `RoleScope`). Risk stays **L3** (BU-lead recipient over an assessment-participation read path). No new QUESTIONS.md item — the fix shape was settled by the re-review panel.

**Verify (gate of record) — ALL GREEN (exit 0), full `./scripts/verify.sh`:** backend build warnings-as-errors 0 errors; migrations idempotent (no new migration); **Hap.Api.Tests 493 passed / 0 failed / 0 skipped** (+6 vs the prior 487 = the 6 new tests), Hap.Architecture 19, Hap.Domain 100, Hap.Synth 41; PrivacyReporting slice **Api 282 + Architecture 14 + Domain 13 passed** (Api +5 vs prior 277 = the 5 PrivacyReporting-tagged new tests; the FR-037 test is correctly untagged); frontend lint/typecheck/test/build + no-external-font all green. Dev worklog 26 min (under the M estimate). Status kept **in-progress** — the lead re-runs the L3 panel + QA.

## QA FINAL PASS 2026-07-23 (`hap-qa`, fresh instance, independent verification on shipping fix `16f38af`)

**VERDICT: PASS.** Independent green-of-record reproduced; every AC clause literally satisfied (bar the Phase-4 wiki task, not due at QA); all §9.3 mandatory attempts closed with no path found; the 6 round-2 guard tests confirmed to have teeth. One **non-blocking observation** recorded below (multi-BU-delegate representativeness residual — does not leak to an unentitled party). No production code touched; no test change required. Status kept **in-progress** (lead does closure).

### Verify (gate of record) — INDEPENDENTLY re-run, ALL GREEN (exit 0)

Ran `./scripts/verify.sh` to completion myself (only the fix's author had run it on `16f38af` before this pass). All 9 stages executed (backend build warnings-as-errors 0 errors; disposable Postgres; dotnet-ef restore; **idempotent migrations — ran twice, second no-op**; backend tests; PrivacyReporting slice; frontend install/lint/typecheck/test; frontend prod build; no-external-font assertion). Counts reproduced EXACTLY against the story's Round-2 figures:
- **Hap.Api.Tests 493 passed / 0 failed / 0 skipped** (0 skipped ⇒ the docker-mailpit idempotence test ran).
- Hap.Architecture.Tests 19 / Hap.Domain.Tests 100 / Hap.Synth.Tests 41 — all 0 failed / 0 skipped.
- **PrivacyReporting slice: Api 282 + Architecture 14 + Domain 13 passed** (0 failed / 0 skipped).
- Frontend: eslint `--max-warnings 0` clean, `tsc --noEmit` clean, vitest all green (incl. `no-external-fonts`), prod build 83 modules, no external font in output.

### AC clauses — literal verification against `16f38af`

- [x] `POST /api/admin/notifications/run` runs all jobs once, reports counts per type — verified (endpoint tests; `Admin_notifications_run_response_carries_only_integer_counts_no_names_or_states` confirms a flat `Dictionary<string,int>` payload, no names/emails/states).
- [x] Cycle reminders: non-responders reminded, submitters get nothing, exact recipient set — verified (`At_seven_days_reminders_go_to_non_responders_and_never_to_a_submitter`).
- [x] Reminders/escalations at configured thresholds; each manager gets their team's list; **BU Lead gets per-team summary** — verified. BU-lead recipient is now bound to the ONE seam authority (`AssessmentReads.AuthorizeIndividualRead` over the candidate's full grant set, in `CycleReminderJob.BuLeadPassesSeamReadGate`); positive path proven live (`FR061_a_BuDelegate_anchored_on_the_bu_receives_that_bus_summary`), all denial shapes proven (mislabel, wrong-BU, co-grant precedence ×3, deactivated, contractor).
- [x] Weekly-update nags >7d/>14d, Idea/Retired exempt; **BU-Lead escalation** recipient BU-anchored + eligibility-gated — verified (`NotificationJobService.IsEligibleRecipient` = active ∧ non-contractor over plain `Person`; `FR037_a_deactivated_BuDelegate_must_not_receive_the_escalation` green).
- [x] Running jobs twice sends no duplicates — verified (dedup-token ledger; idempotence tests green, 0 skipped).
- [x] All mail to mailpit only, no external SMTP — verified (`SmtpOptions` defaults + compose `Smtp__Host=mailpit`; no other SMTP surface).
- [x] Moderation-complete notice on transition — verified (`moderation-complete.txt` carries no scores/comments; best-effort send does not fail the committed moderation).
- [ ] Wiki `docs/wiki/notifications.md` — **not created; Phase-4 closure task (lead), explicitly not due at QA** per story notes. The only unmet clause, and it is by design.
- [x] `./scripts/verify.sh` green — **independently verified this pass (exit 0).**

### §9.3 mandatory adversarial attempts — re-run against the FINAL shipping code

**(a) Read a score / another person's participation data outside the chain via any notification surface, per role.** No path.
- All 6 templates re-inspected verbatim: BU-lead summary = per-team COUNTS only ("names no individuals, contains no assessment scores"); manager escalation names only the reviewer's OWN reports' submission status (review-queue parity); moderation-complete carries no scores/comments; reminders say only "you have not submitted". `EmailTemplateRenderer.Render` is a plain caller-supplied-token replace — no auto-injection.
- The only assessment-state read on the notification path is `IAssessmentStore.GetNonResponderPersonIdsAsync` (`SeamAssessmentStore.cs:92`), confirmed STATE-ONLY: it selects `PersonId` where `State != NotStarted/InProgress` and returns the complement set — no score column ever read. **CLOSED.**

**(b) Defeat N<4 suppression / get a sub-4 (incl. n=1) count to a recipient the seam would deny — the exact class of the two prior leaks.** No path to an unentitled recipient.
- Re-attacked `BuLeadPassesSeamReadGate` directly. It binds each resolved candidate to `AuthorizeIndividualRead` over the candidate's FULL grant set, so it enforces all three seam conjuncts at once: precedence (co-held HigExec/PlatformAdmin/GroupViewer strips capability → denied), eligibility (inactive/contractor → denied), specific-BU anchor. Confirmed against the seam source (`AssessmentReads.ClassifyReader` ranks the three stripping roles above `BuDelegate`; `ReaderEligible` = active ∧ non-contractor under Q-006 Restrictive).
- **Highest-value probe (lead-directed): is single-subject authorization representative of the whole per-team summary given BU-level reach?** Traced it exhaustively. For the realistic case — a candidate holding a SINGLE `BuDelegate(buId)` grant — the answer is YES, fully representative: `SendBuLeadSummariesAsync` groups non-responders by `BusinessUnitId`, so every counted person in a group shares `buId`; the classified `BuLead` reach is `IndividualReadReach.BusinessUnit`, and `AuthorizeIndividualRead`'s BU branch allows any subject with `subject.BusinessUnitId == delegatedBusinessUnitId (== buId)`. So a pass on one sampled member implies a pass on every member. No leak.
- **Decisive containment fact:** the summary recipient is `buLeadMap[buId]`, and `RoleGrantBuLeadResolver` populates that map ONLY from `RoleGrant`s with `Role == BuDelegate && BusinessUnitId == buId`. Therefore the recipient of BU_X's summary ALWAYS holds a genuine `BuDelegate(BU_X)` grant — there is NO configuration in which a summary reaches a party lacking a grant-based entitlement to the counted BU. This is what makes the prior leaks (stranger / capability-stripped role) impossible here.
- Manager-escalation gate re-confirmed denying HIG-Exec / GroupViewer / PlatformAdmin reviewers-of-record (tests green). **CLOSED — no leaked number reaches an unentitled recipient.**

**(c) Desynchronise a reported count from underlying records.** No path.
- The BU-summary per-team counts derive solely from the single sanctioned state-only read (`GetNonResponderPersonIdsAsync`) = invited-set minus responded-set, grouped by reviewer-of-record and `.Count()`-ed in one pass — there is no independent rollup/snapshot/Harris figure in this surface to diverge from. No "Other"-initiative leak, no NR double-count, no stale snapshot vector exists here. **No path.**

### Guard-teeth confirmation (round-2 tests)

The 6 round-2 guards genuinely bite (not vacuous): each asserts a live PREMISE that the resolver STILL surfaces the candidate (`Assert.True(map[buId] == candidate)`) AND that the seam DENIES the candidate (`Assert.False(AuthorizeIndividualRead(...).Allowed)` over the candidate's full grant set), before asserting no mail. The precedence sweep is a `[Theory]` over all three capability-stripping roles (GroupViewer/HigExec/PlatformAdmin — the exhaustive non-BuDelegate `OrgRole` set); eligibility covers deactivated + contractor for FR-061 and deactivated for FR-037. Positive path (`FR061_a_BuDelegate_anchored_on_the_bu_receives_that_bus_summary`) rules out a trivially-passing "resolver returns nobody". I searched for an unguarded variant and found none in the leak-to-unentitled-party class (see (b)).

### NON-BLOCKING OBSERVATION — multi-BU-delegate representativeness residual (informational, for lead/dev + G1 file)

`BuLeadPassesSeamReadGate` samples ONE non-responder subject and generalises the decision to the whole per-team summary. This is sound for a single-`BuDelegate` holder (proven above). It is NOT exact in one narrow, benign shape: if the recipient co-holds a SECOND `BuDelegate` grant over a different BU, `ClassifyReader.FirstOrDefault` may anchor them on that OTHER BU, so `AuthorizeIndividualRead` for a non-directly-managed counted person in THIS BU would be denied — yet the gate can still pass if the one sampled subject happens to be the recipient's direct report (the `isDirectManager` disjunct, which is per-person, not BU-wide). The summary would then include counts about BU members the *seam* (via its FirstOrDefault limitation) would refuse that recipient a direct read of.
- **Why non-blocking:** the recipient in that shape STILL holds a genuine `BuDelegate` grant over the very BU whose counts they receive (the resolver guarantees it, see (b)). So no count ever reaches a party without a grant-based entitlement to that BU — this is materially different from Findings 1/2 (stranger) and the round-2 co-grant leak (capability-stripped role), which WERE blocking. The divergence is purely against the seam's own known `FirstOrDefault` under-grant of a multi-BU delegate (the "tertiary nuance" the L3 re-review already logged as not-blocking), it is non-deterministic (depends on the hashed non-responder iteration order), and it is dormant on synth (Q-035: no `BuDelegate` grants seeded).
- **Recommendation (optional, for exact seam-parity, not a merge blocker):** either evaluate `AuthorizeIndividualRead` for EVERY counted person rather than one sample, or have the resolver hand the gate the specific anchoring BU so a multi-BU delegate's reach for THIS BU is used. Filing this as an observation rather than a QUESTIONS.md item because it does not change required behaviour; leaving the disposition to the lead.

### New tests added this pass

None. The round-2 guards already cover the leak-to-unentitled-party class with teeth; the residual above is non-blocking and a flaky/non-deterministic assertion against a legitimate-delegate recipient would be poor regression hygiene, so I deliberately added no test. This QA record is the only file change.

### Worklog

QA start `.wallclock-HAP-18-qa2` = 2026-07-23T19:11:46Z; close 2026-07-23T19:24:55Z; **13 min** (under the S estimate). Entry appended to frontmatter.
