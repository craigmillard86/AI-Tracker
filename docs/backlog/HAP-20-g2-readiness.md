---
id: HAP-20
title: GATE G2 readiness — end-to-end reconciliation evidence and gate walkthrough
epic: E4-harris
wave: 2
fr: [FR-046]
risk: L0                # trigger: docs + test-only additions
status: todo
estimate: {dev: S, qa: S}
worklog: []
closure: null
---
## Story
As the platform owner, I have a scripted, repeatable walkthrough proving a full weekly + monthly Harris submission reconciles line-by-line against register and metrics records — the complete evidence set for the human-witnessed G2 reporting gate.

**GATE: closing this story completes G2 readiness (constitution Art. VII). Closure notes MUST say so prominently. The story flags the gate; only the owner passes it, witnessing quickstart.md "V5 — Harris submission reconciliation" on a full weekly + monthly submission.**

## Context
- Spec: SC-004 (100% reconciliation), FR-046; constitution Art. VI.4 + Art. VII G2 definition (stage mapping, level meaning, YTD-vs-current-month rules witnessed line-by-line).
- Plan: story table HAP-20; quickstart.md V5 + "Gate readiness"; research D5 (persisted submissions are the fixed artifact G2 reconciles).
- Files: `backend/tests/Hap.Api.Tests/**` (end-to-end evidence test: seed → cycle → register activity → declarations/metrics → generate both submissions → reconcile every line), `docs/wiki/harris-submissions.md` (extend with the reconciliation method), a `docs/delivery/g2-walkthrough.md` script for the witnessed run, PrivacyReporting coverage report (list of every suite tagged, generated into the story notes at closure).
- Test-only + docs — no product code. If any product defect surfaces, STOP: it belongs to a new story, not this one (record in Attempts).
- Blocked by: HAP-17, HAP-19
- Parallelisable: yes, with HAP-21 (disjoint files)

## Acceptance criteria
- [ ] One end-to-end evidence test (`Category=PrivacyReporting`) runs the full journey on synth data and asserts every weekly + monthly submission line reconciles exactly to independent recomputation — green under `./scripts/verify.sh`.
- [ ] `docs/delivery/g2-walkthrough.md` gives the owner a step-by-step witnessed-run script mirroring quickstart V5, incl. the YTD boundary check and one hand-verified category count. The walkthrough generates the weekly + monthly submissions **fresh at the witnessed run** — never reconciling a stale persisted document (FR-046 as amended; DR-0004; panel A2).
- [ ] PrivacyReporting coverage report produced: every [S]/[A] endpoint and every submission line type maps to at least one named tagged test; gaps = failures of this story.
- [ ] Closure notes flag G2 readiness prominently.
- [ ] `./scripts/verify.sh` green.

## Attempts / notes

**SPEC AUDIT 2026-07-21 / L2 PANEL A2:** fresh-generation requirement made explicit for the G2 witnessed run (DR-0004).
