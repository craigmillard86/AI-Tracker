---
id: HAP-7
title: Cycle management — global monthly state machine, invitations, contractor exclusion
epic: E2-assessment
wave: 1
fr: [FR-002, FR-003, FR-004, FR-005, FR-006, FR-060]
risk: L2                # trigger: cycle state machine
status: todo
estimate: {dev: M, qa: S}
worklog: []
closure: null
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
