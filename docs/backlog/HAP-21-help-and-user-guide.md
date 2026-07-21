---
id: HAP-21
title: In-app help surface + user guide baseline
epic: E1-foundations
wave: 2
fr: [FR-072, FR-073]
risk: L1                # trigger: React screens + copy
status: todo
estimate: {dev: M, qa: S}
worklog: []
closure: null
---
## Story
As any user of a mandated flow, I can open contextual help that explains what I'm being asked to do and why — and a printable user guide exists that says the same thing — so a 10,000-person monthly obligation doesn't run on tribal knowledge.

## Context
- Spec: FR-072 (in-app contextual help for self-assessment, moderation, register, BU forms, Harris review — content as versioned seeded data, never hard-coded), FR-073 (printable guide at `docs/user-guide/`, consistent with in-app help); DR-0003.
- Plan: story HAP-21 notes — earlier UI stories drafted per-flow guide pages (see `docs/user-guide/README.md` mapping); THIS story ships the in-app surface, fills any pages the UI stories left thin, and reconciles the two.
- Content storage: versioned JSON/markdown content files under `app/src/content/help/` (data-not-code satisfied by externalised versioned content; no DB table, no migration).
- **Component constraint: use existing components only (card, body type, buttons per A4/A8). If a new component (e.g. a HelpPanel/drawer) proves necessary, STOP — amend DESIGN.md A8 via reviewed commit first (CLAUDE.md §8.2) and note it here.**
- Files: `app/src/content/help/**`, a help entry-point per screen (existing screens touched minimally: one help trigger each), `docs/user-guide/**`.
- Blocked by: HAP-17
- Parallelisable: yes, with HAP-20 (disjoint files)

## Acceptance criteria
- [ ] Each mandated flow (assessment-self, assessment-moderation, register list/detail, bu-forms, harris-submission) has a help trigger opening that flow's contextual content; content rendered from `app/src/content/help/` files — a grep-guard test asserts no help copy string lives in component source (FR-072).
- [ ] Help content for each flow covers: what the user must do, the key rule of that flow (e.g. Δ≥2 comment, forward-only stages, purpose limitation), and where the data goes — reviewed against the matching user-guide page for consistency (checklist in PR notes, page-by-page).
- [ ] `docs/user-guide/` contains completed pages per its README mapping (all six), each consistent with the in-app content (FR-073); README index links resolve.
- [ ] Help surface uses existing components/tokens only (see Component constraint); vitest-axe passes on the help view; strings externalised.
- [ ] `./scripts/verify.sh` green.

## Attempts / notes
