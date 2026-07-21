# Wiki — how the system works, as built

One page per subsystem, describing **shipped behaviour** (constitution Art. I, DR-0003). Updated in the closure commit of any story that changes the subsystem — a stale page is drift.

Scope rule: this surface explains the system that exists. It never restates the spec (WHAT/WHY → `docs/spec/`, `specs/`), the backlog (status → `docs/backlog/`), or decision records (why decided → `docs/decisions/`).

Pages are created by the first story that ships each subsystem:

| Page (created by) | Subsystem |
|---|---|
| `org-and-directory.md` (HAP-3) | Org hierarchy, synthetic directory import, overrides, leavers |
| `identity.md` (HAP-4) | Identity port, dev provider, sessions, role derivation |
| `visibility-seam.md` (HAP-5) | Chain resolver, role scopes, N<4 + complement suppression |
| `frameworks.md` (HAP-6) | Framework engine, versioning, seeding |
| `cycles-and-assessment.md` (HAP-7..10) | Cycle state machine, scoring, moderation, close behaviour |
| `rollups.md` (HAP-11) | Rollup computation, snapshots, dashboards |
| `audit-and-gdpr.md` (HAP-12) | Audit trail, right-of-access, retention |
| `register.md` (HAP-13/14) | Initiative register, stage history, weekly updates |
| `harris-submissions.md` (HAP-16/17) | Submission generation, stage mapping, reconciliation |
| `notifications.md` (HAP-18) | Reminder/escalation jobs, mailpit |
