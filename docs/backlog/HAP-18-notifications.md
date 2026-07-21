---
id: HAP-18
title: Notifications — cycle reminders/escalations and weekly-update nags via mailpit
epic: E3-register
wave: 2
fr: [FR-037, FR-057, FR-061]
risk: L2                # trigger: notification scheduling
status: todo
estimate: {dev: M, qa: S}
worklog: []
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
