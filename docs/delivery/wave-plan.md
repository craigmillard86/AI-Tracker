# Delivery wave plan & epic taxonomy — feature 001 (Phase 1 MVP)

Source of the story slicing: `specs/001-maturity-initiative-register/plan.md`. Epics are the domain axis (frontmatter `epic:`); waves are the delivery sequence (frontmatter `wave:`). The two are independent.

## Epics

| Epic | Name | Stories |
|---|---|---|
| E1-foundations | Scaffold, synth data, org, identity, seam, framework engine | HAP-1..HAP-6 |
| E2-assessment | Cycles, scoring, moderation, close, rollups, audit/GDPR | HAP-7..HAP-12 |
| E3-register | Initiative register, stage history, notifications | HAP-13, HAP-14, HAP-18 |
| E4-harris | BU capture forms, submission engine + UI, reporting, G2 | HAP-15..HAP-17, HAP-19, HAP-20 |

## Waves

| Wave | Intent | Stories | Gate |
|---|---|---|---|
| 0 | Foundations — nothing reads assessment data until the seam stands | HAP-1..HAP-6 | — |
| 1 | Assessment core | HAP-7..HAP-12 | **HAP-12 flags G1 readiness** (privacy, human-witnessed) |
| 2 | Register & Harris | HAP-13..HAP-20 | **HAP-20 flags G2 readiness** (reconciliation, human-witnessed) |

Gates are witnessed by the owner per constitution Art. VII; stories flag readiness, never pass gates.
