---
id: HAP-14
title: Initiative detail — forward-only stage history, NR lines, weekly updates (register-detail.html)
epic: E3-register
wave: 2
fr: [FR-028, FR-029, FR-030, FR-031, FR-032, FR-033]
risk: L2                # trigger: EF migrations/schema (NR aggregation itself is HAP-16)
status: todo
estimate: {dev: L, qa: M}
worklog: []
closure: null
---
## Story
As an initiative owner or BU Lead, I manage one initiative in full — immutable forward-only stage history, NR lines, governance fields, customer counts, and a sub-minute weekly RAG update — so Harris reporting has trustworthy raw material.

## Context
- Spec: FR-028 (stage machine Idea→…→Retired, immutable forward-only history, corrections = new transitions), FR-029 (value + NR capture), FR-030 (governance/risk — informational only, §4.2 register is not an approval gate), FR-031 (customers), FR-032 (technology/Cogito), FR-033 (weekly RAG + note); "Edge Cases" stage-change bullet.
- Plan: data-model.md InitiativeStageHistory (**append-only; Retired rows capture prior_stage** — feeds FR-064), InitiativeWeeklyUpdate, InitiativeNRLine (**EF migration #7**); contracts/api.md stage/updates/nr-lines endpoints incl. **409 on backward transition** and **409 deleting an NR line referenced by a persisted monthly submission** (guard ships here; submissions exist from HAP-16).
- Mockup: `docs/design/mockups/register-detail.html` — binding incl. **11-day overdue banner** and **red-RAG initiative** states. Components (A8): **StageTimeline**, **NRLineEditor**; A4 cards/forms/badges.
- Files: domain + endpoints, `backend/src/Hap.Infrastructure/Persistence/**` (migration #7), `app/src/screens/register-detail/**`, `app/src/components/{StageTimeline,NRLineEditor}/**`.
- **Serialise with: HAP-13 (migration chain).**
- Blocked by: HAP-13
- Parallelisable: no

## Acceptance criteria
- [ ] `POST /api/initiatives/{id}/stage`: forward transitions append history rows with entered_at/by; backward → 409; Retired is terminal (further transitions 409) and the history row records prior_stage (FR-028 tests per transition).
- [ ] Stage history has no UPDATE/DELETE path (EF mapping + architecture-test assertion, same pattern as AuditLog).
- [ ] `POST /api/initiatives/{id}/updates` records RAG + note; `last_update_at` refreshes; update trail returned newest-first on the detail endpoint (FR-033).
- [ ] NR lines: add with year/direction/recurrence/amount/description; delete allowed until referenced by a persisted monthly submission → then 409 (guard test stubs a submission row shape; full flow retested in HAP-16).
- [ ] Governance fields persist and render read/write per FR-030 with the §4.2 informational-only copy shown (no approval semantics anywhere — route/permission test that no approval state blocks any operation).
- [ ] Customers count editable for customer-deployed categories only (HarrisCategory flag drives it — test both category kinds).
- [ ] UI implements the mockup: identity/tech card, dimensions-advanced chips, NRLineEditor (Direct/Indirect × One-Time/Recurring rows, right-aligned $), governance card, StageTimeline (ordered, dates printed, no interactive states), weekly update composer (RAG select + one-line note + submit), 11-day overdue banner state, red-RAG state.
- [ ] vitest-axe passes; strings externalised; tokens only.
- [ ] Wiki/guide (DR-0003, at closure): extend `docs/wiki/register.md` + `docs/user-guide/initiative-register.md` (detail/update portions).
- [ ] `./scripts/verify.sh` green (migration idempotent).

## Attempts / notes
