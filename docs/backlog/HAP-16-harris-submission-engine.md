---
id: HAP-16
title: Harris submission engine — weekly + monthly generation with reconciliation suite
epic: E4-harris
wave: 2
fr: [FR-043, FR-044, FR-045, FR-046, FR-064, FR-065]
risk: L3                # trigger: Harris submission generation + its aggregation queries (Hap.Domain/Submissions)
status: todo
estimate: {dev: L, qa: M}
worklog: []
closure: null
---
## Story
As an EVP, the system generates my weekly and monthly Harris submissions — category × mapped-stage × level counts, customers, stopped initiatives, NR YTD, support/SOR metrics, declared level with measured divergence — every figure persisted and reproducible by an independent query, because a number that doesn't reconcile is worse than no number.

## Context
- Spec: FR-043..FR-046 (submission content + **100% reconciliation**), FR-064 (**stage mapping as configuration data**: Idea+Evaluation→Ideation, Pilot→Development, Production+Scaled→Production, Retired→IdeasTriedButStopped at prior_stage), FR-065 (declared-vs-measured divergence reportable); root spec §6.1 tables (exact form sections); SC-004; "Sacred" reporting rule (CLAUDE.md §1).
- Plan: research **D5** (persist submission + lines with as-of; reconciliation via independent hand-written SQL, not EF, not the production query); data-model.md HarrisSubmission/HarrisSubmissionLine + **HarrisStageMap seeded table** (**EF migration #9**).
- Files: `backend/src/Hap.Domain/Submissions/**` (generation logic — pure, unit-testable), migration #9 + seeds, generation endpoints (`GET /api/bus/{buId}/submissions/{weekly|monthly}`), reconciliation test suite in `Hap.Api.Tests`.
- **Serialise with: HAP-15 (migration chain).**
- Blocked by: HAP-14, HAP-15
- Parallelisable: no

## Acceptance criteria
- [ ] Weekly generation persists a HarrisSubmission + lines: per group-reported category — counts in Ideation/Development/Production broken by AI-DLC level 1/2/3 (mapped via HarrisStageMap rows, never code — grep-guard test on stage-name strings), # unique customers per category, Ideas Tried but Stopped counted at prior_stage × level, declared level + RAG + next-level date from the latest declaration, and the declared-vs-measured divergence (FR-044/064/065).
- [ ] **"Other" category initiatives appear in NO group-reported line** (FR-044 test: an Other initiative in Production changes nothing).
- [ ] Monthly generation persists: per-category Direct/Indirect × One-Time/Recurring NR in $USD aggregated YTD "up to and including the submission month" (FR-045 boundary test: line dated month+1 excluded), plus Support internal/customer and SOR from the latest monthly metrics (FR-045/046).
- [ ] Reconciliation suite (`Category=PrivacyReporting`): EVERY submission line equals an independent raw-SQL recomputation (research D5) — parameterised over all lines of a generated weekly + monthly for 2 synth BUs; exact equality, no tolerance.
- [ ] NR-line 409 guard from HAP-14 verified end-to-end: after a monthly submission persists, deleting a referenced NR line → 409.
- [ ] Submissions are persisted immutably (no update endpoint; regeneration creates a new submission row with new as-of — test).
- [ ] Generation completes < 30s per BU against full synth data (SC-008 timing assertion).
- [ ] QA (adversarial, fresh agent — mandatory L3 attempts): attempt to make any submission figure disagree with underlying rows (mutate register post-generation and verify the persisted submission still reconciles to its as-of snapshot semantics; regenerate and verify new figures); attempt to smuggle an "Other" initiative into a reported count; document here.
- [ ] Wiki (DR-0003, at closure): create `docs/wiki/harris-submissions.md`.
- [ ] `./scripts/verify.sh` green (migration idempotent).

## Attempts / notes
