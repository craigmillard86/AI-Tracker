# Specification Quality Checklist: HIG AI Maturity & Initiative Register

**Purpose**: Validate specification completeness and quality before proceeding to planning  
**Created**: 2026-07-21  
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs) — spec uses "System MUST" without naming .NET/React/PostgreSQL in requirements; infrastructure assumptions are clearly separated
- [x] Focused on user value and business needs — requirements center on maturity measurement, initiative tracking, and Harris reporting pain points
- [x] Written for non-technical stakeholders — user stories describe workflows, not system architecture; success criteria use business metrics
- [x] All mandatory sections completed — User Scenarios (9 stories + edge cases), Requirements (67 FR items + 18 key entities), Success Criteria (12 measurable outcomes), Assumptions (11 items), Open Questions (3 questions)

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain — 3 open questions recorded explicitly in "Open Questions" section; all requirements use informed defaults where specification was ambiguous
- [x] Requirements are testable and unambiguous — each FR-NNN states a specific capability; acceptance scenarios use Given-When-Then; edge cases are enumerable
- [x] Success criteria are measurable — SC-001 through SC-012 include metrics: percentages, timelines, exact reconciliation, user counts, accessibility standards
- [x] Success criteria are technology-agnostic — no mention of .NET, React, PostgreSQL, SQL dialects, or specific vendor services; architecture assumptions live in Assumptions section
- [x] All acceptance scenarios are defined — each user story includes 1–3 concrete Given-When-Then scenarios; edge cases list boundary conditions and error cases
- [x] Edge cases are identified — 5 edge cases documented covering leave scenarios, mid-cycle departures, retroactive stage changes, small-group suppression edge case, and framework version changes
- [x] Scope is clearly bounded — Phase 1 MVP scope stated explicitly (org sync, SSO, framework engine, cycle mgmt, self+manager assessment, rollups, register, Harris reports); Phase 2–3 features deferred
- [x] Dependencies and assumptions identified — Article VI (Privacy/Reporting seam) of constitution; UK GDPR personal data; L3 review requirement; DPIA pre-rollout; 3-year retention policy

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria — each FR links to one or more user stories with testable scenarios
- [x] User scenarios cover primary flows — 9 user stories cover: self-assessment (P1), manager review (P1), BU Lead dashboard (P1), Harris submission (P1), Group/Portfolio views (P2), weekly updates (P2), monthly metrics (P2), benchmarking (P2), org sync (P1)
- [x] Feature meets measurable outcomes defined in Success Criteria — assessment scoring, rollups, authorization, small-group suppression, accessibility, Harris reconciliation, all covered by SC-001 through SC-012
- [x] No implementation details leak into specification — all "System MUST" statements describe behavior, not internals; UI/UX, database schema, API versioning are absent

## Governance & Compliance

- [x] GDPR and privacy constraints documented — Article VI reference, UK GDPR personal data, retention policy (3 years raw / indefinite aggregates), right-of-access support, audit trail immutability
- [x] Accessibility requirements explicit — WCAG 2.2 AA on assessment flow (SC-007)
- [x] Risk classification addressed — L3 for authorization, visibility, and Harris submission generation (per constitution Article V); requires code-reviewer + domain specialist + red-team
- [x] Constitution compliance — specification aligns with Article VI (Privacy Seam), Article IX (stack, TDD, accessibility), and deferred decisions recorded (Entra ID, directory attributes, Harris API)

## Notes

✅ **READY FOR PLANNING** — Specification is complete, requirements are testable, and no clarifications are blocking progress. Three open questions (QA-001/002/003) are documented and do not block MVP scope; they are inputs to Phase 0 (drift sweep) and external coordination (directory audit, Harris confirmation) before Cycle 1 launch.

**Key readiness indicators**:
- 9 independently-testable user stories (P1 flows block on none of P2)
- 67 functional requirements with clear testable acceptance criteria
- 12 measurable success criteria, each with specific metrics
- L3 review path clearly defined; red-team brief prepared (attempt N<4 suppression bypass, infer individual scores, break Harris reconciliation)
- No architectural decisions deferred; stack/infrastructure assumptions are explicit
- Constitutional compliance verified; dependencies on Article VI privacy seam documented

**Next steps**: `/speckit-plan` to generate technical architecture and implementation plan.

## Review Amendments (2026-07-21)

Owner review of the generated spec found 8 fidelity gaps against the root specification; all fixed in the same day:

1. Harris stage mapping was absent → **FR-064** (Idea+Evaluation→Ideation; Pilot→Development; Production+Scaled→Production; Retired→Ideas Tried but Stopped at stage held when retired).
2. "Other" category exclusion from group-reported counts was absent → **FR-044** amended; **FR-027** category labels corrected to full Harris taxonomy.
3. Declared-vs-measured AI-DLC divergence reporting was absent → **FR-065**.
4. Monthly cadence and burden-proportionality defaults were absent → **FR-060** (monthly cycles), **FR-062** (self-assessment pre-population), **FR-063** (manager carry-forward default).
5. In-app GDPR purpose-limitation statement was an assumption, not a requirement → **FR-066**.
6. Appendix A framework content had been dropped → restored as data at `docs/frameworks/ai-maturity-sdlc.v1.json`; authoritative source is root spec Appendix A; FR-001 references it.
7. Localisation/string externalisation was absent → **FR-067**.
8. Cycle reminders and escalation summaries were under-specified → **FR-061**.

2026-07-21 (later, DR-0003): owner adopted documentation rules — **FR-072** (in-app contextual help as seeded data) and **FR-073** (printable user guide at `docs/user-guide/`) added; as-built wiki (`docs/wiki/`) mandated at constitution level; plan gains HAP-21. FR count now 73.

Layout decision (**DR-0002**): feature specs live under `specs/` per the Spec Kit process; the root specification is committed verbatim at `docs/spec/hig-ai-maturity-platform-specification.md`; this feature spec's FR-NNN identifiers are the citation scheme for commits and backlog stories. Known remaining nits (not blocking planning): FR-055 wording vs the identity-port decision, SC-008 team-count arithmetic.
