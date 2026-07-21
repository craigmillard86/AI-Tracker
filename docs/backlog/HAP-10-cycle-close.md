---
id: HAP-10
title: Cycle close — auto-adopt unmoderated, snapshots with frozen suppression, departure escalation
epic: E2-assessment
wave: 1
fr: [FR-068, FR-069, FR-070, FR-015, FR-016]
risk: L3                # trigger: writes moderated scores + rollup/suppression computation over AssessmentScores
status: todo
estimate: {dev: M, qa: S}
worklog: []
closure: null
---
## Story
As the platform, closing a cycle auto-adopts unmoderated self-scores (flagged), computes mean and floor-level rollups into immutable snapshots with suppression verdicts frozen at close, and escalates a departed manager's pending reviews — so history is complete, honest, and can never retro-expose anyone.

## Context
- Spec: FR-068 (auto-adopt + unmoderated %, excluded from calibration delta), FR-069 (leave status), FR-070 (manager departure escalation), FR-015/016 (mean + floor computations); "Clarifications" bullets 1 and 5.
- Plan: research **D4** (RollupSnapshot per org node at close; trend reads snapshots) and **D2** (suppression verdict FROZEN in the snapshot); data-model.md RollupSnapshot (**EF migration #5**); scoring maths in `Hap.Domain/Scoring` (mean of 7; floor = min; distribution).
- Files: `backend/src/Hap.Domain/Scoring/**`, close orchestration in cycle service, snapshot writer via seam, `backend/src/Hap.Infrastructure/Persistence/**` (migration #5).
- **Serialise with: HAP-8 (migration chain).**
- Blocked by: HAP-9
- Parallelisable: no

## Acceptance criteria
- [ ] `POST /api/cycles/{id}/close`: every Submitted-but-unmoderated assessment becomes AutoAdopted with self-scores copied to manager scores and `unmoderated=true` (FR-068); Moderated assessments untouched (test with mixed states).
- [ ] Calibration delta computation excludes AutoAdopted rows (FR-068 test: delta identical with/without an auto-adopted member).
- [ ] Manager departed before close (is_active=false with pending reviews): pending reviews escalate to the manager's manager, who can moderate until close; at close, still-unmoderated → auto-adopt (FR-070 test using the synth leaver).
- [ ] Scoring maths (Hap.Domain.Tests, pure, `Category=PrivacyReporting`): mean = arithmetic mean of 7 dimension scores to 2dp; floor level = min dimension score; distribution counts by floor level — property test over random score sets: floor ≤ every dimension score and floor ≤ mean.
- [ ] Snapshots written for every Team/BU/Group/Portfolio/AllHig node: n, per-dimension means, floor distribution, completion %, unmoderated %, calibration delta, suppression verdict per research D2 — spot-assertions against hand-computed values for 3 synth nodes.
- [ ] Suppression verdicts frozen: shrinking a team below 4 AFTER close does not change the stored verdict (FR-071 historical rule test); snapshots have no update path (append-only assertion).
- [ ] Snapshot totals reconcile: sum of team n's = BU n (per BU); recompute-from-raw equals snapshot values (desync guard, `Category=PrivacyReporting`).
- [ ] QA (adversarial, fresh agent — mandatory L3 attempts): attempt to make a snapshot disagree with underlying rows (recompute independently); attempt to read an aggregate covering <4 via any close output; document here.
- [ ] `./scripts/verify.sh` green (migration idempotent).

## Attempts / notes
