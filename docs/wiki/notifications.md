# Notifications subsystem (as-built)

*Owner story: HAP-18 (FR-061, FR-037, FR-057). Closed 2026-07-23, sha `1ef38bd`, risk L3. See also [visibility-seam.md](visibility-seam.md) and [cycles-and-assessment.md](cycles-and-assessment.md).*

The platform sends email — and only email — for three event families. All mail goes to the compose-network **mailpit** (SMTP 1025, UI/API 8025); there is no external SMTP configuration anywhere in the build. Delivery is deterministic in test/demo via an admin trigger.

## What fires, and when

| Family | FR | Trigger | Recipient(s) |
|---|---|---|---|
| **Cycle reminders** | FR-061 | Non-responder in an open cycle, at configured days-before-close (default **7, 3, 1**) | each non-responder (deep link) |
| **Manager escalation** | FR-061 | From configured threshold before close (default **3d**) | each non-responder's reviewer-of-record — *gated* (see below) |
| **BU-lead summary** | FR-061 | Same window | the BU's seam-entitled BU lead — *gated* (see below); **per-team COUNTS only, names no individual** |
| **Weekly-update owner nag** | FR-037 | Initiative in Evaluation→Scaled, no update **>7d** (Idea/Retired exempt) | initiative owner |
| **Weekly-update BU-lead escalation** | FR-037 | Same, no update **>14d** | the BU's eligible BU lead (register data) |
| **Moderation-complete** | FR-057 | Submitted→Moderated transition | the individual (notice only, **no score/moderated value in the body**) |

Cadence is **configuration, not schema** (`NotificationCadenceOptions`): intended close = `Cycle.OpensAt + CycleLengthDays` (default 30). No migration; idempotence is by computed thresholds plus a sent-mail ledger check (`ISentMailLedger` → `MailpitSentMailLedger` in prod, in-memory in test), so running the jobs twice sends no duplicates.

> **FR-057 is only partially delivered by this story** — of the email-only event list, only the **moderation-complete** notice ships here. The remaining events in the FR-057 list are a follow-up (domain advisory A2 on HAP-18).

## The privacy-critical part: who may receive a BU's individual data

The BU-lead summary (FR-061) discloses **assessment-participation data** — per-team counts of who has/hasn't submitted. That is individual-assessment-derived personal data (UK-GDPR; constitution Art. VI), so **who receives it is an L3 concern** and must match the visibility seam exactly. Two leaks were found and closed during HAP-18 (see the story file for the full arc):

1. **Depth-label mislabel (QA Finding 1/2).** The recipient was first chosen from `HierarchyRoleResolver`'s depth-from-root "BU Lead" label — which on a non-uniform tree names a structurally-unrelated person (the Q-014 hazard). Fixed by anchoring on an explicit BU-scoped `OrgRole.BuDelegate` grant.
2. **Subset-of-seam predicate (L3 re-review).** Anchoring on the BuDelegate grant *alone* was still weaker than the seam: `AssessmentReads.ClassifyReader` ranks HigExecutive/PlatformAdmin/GroupViewer **above** BuDelegate and *strips* individual-read capability when such a grant is co-held, and the seam also requires reader eligibility (active, non-contractor). A dual-granted or deactivated delegate would have received the summary while the seam denied them the read.

**As-built resolution.** `RoleGrantBuLeadResolver` (Infrastructure) is only a **candidate producer** — it lists the BuDelegate-anchored candidate per BU. The FR-061 recipient is then bound to the *one* seam authority in the Api-layer consumer:

- **`CycleReminderJob.BuLeadPassesSeamReadGate`** calls `AssessmentReads.AuthorizeIndividualRead` over the candidate's **full** grant set, against a real non-responder in the BU (chosen ≠ the candidate, so an own-data read can't vacuously pass; self only when the candidate is the sole non-responder). That single call enforces all three conjuncts at once — grant **precedence**, reader **eligibility**, and **specific-BU** reach. A candidate the seam would deny is skipped-silently-but-counted. This is the same gate the manager escalation already uses (`HasIndividualReadCapability`), so the notification surface and the seam cannot diverge.
- The manager escalation is likewise gated: a capability-stripped reviewer-of-record (Platform Admin / HIG Exec / Group Viewer) receives nothing — parity with `ManagerModerationService.GetReviewQueueAsync`.

**Layering.** `Hap.Infrastructure` must not depend on `Hap.Api.Authorization`. So the assessment-seam gate (FR-061) lives in the Api consumer; the FR-037 escalation (register data, **not** Art. VI) uses a plain-`Person` eligibility filter (`NotificationJobService.IsEligibleRecipient` = `IsActive && not Contractor`) in Infrastructure, which needs no seam import.

## Dormant on synthetic data

The synthetic build seeds **no `BuDelegate` grants** (DR-0005 / Q-014 — BU leads are depth-identified only). Because the recipient is now correctly gated on a real BuDelegate entitlement, **every BU-lead summary/escalation SKIPS on pure synthetic data** (fail-closed correct — sending to the depth-label lead was the leak). BU-lead notifications become demonstrable once a `BuDelegate` grant exists (the tests create one). Whether synth should seed such grants for G1/G2 demos is **Q-035** (open, owner decision).

## Determinism & operations

- **`POST /api/admin/notifications/run`** (Platform Admin) runs all jobs once, synchronously, and returns **counts only** (a flat `Dictionary<string,int>` — no names, no emails, no states in the payload). This is the test/demo path.
- In production a hosted service on a `PeriodicTimer` runs the same jobs.
- Email templates are externalised content (`Email/EmailTemplates/*.txt`), auto-embedded — never inline C# strings.

## Known follow-ups
- **Q-034** — owner-nag threshold day-7 (`>=7`) vs day-8 (`>7`, as shipped); one-line config change either way.
- **Q-035** — whether synth seeds BuDelegate grants so BU-lead features demo on synth.
- **Q-036** — the seam's `ClassifyReader.FirstOrDefault` under-grants a multi-BU delegate; the gate samples one subject and generalises. Non-blocking (the recipient always holds a genuine BuDelegate grant over the counted BU, so no count reaches an unentitled party), but an exactness follow-up (check every counted person, or pass the specific anchor BU) is noted.
