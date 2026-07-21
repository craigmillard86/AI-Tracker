---
id: HAP-13
title: Initiative register core + list UI (register-list.html)
epic: E3-register
wave: 2
fr: [FR-026, FR-027, FR-034, FR-035]
risk: L2                # trigger: EF migrations/schema (no assessment reads; Harris taxonomy as seeded data)
status: todo
estimate: {dev: L, qa: M}
worklog: []
closure: null
---
## Story
As a Manager or BU Lead, I can register AI initiatives classified against the Harris taxonomy and find them through a filterable, searchable list — so the group finally has a live register instead of tribal knowledge.

## Context
- Spec: "Module 2: Initiative Register" FR-026 (identity fields), FR-027 (Harris taxonomy categories incl. "Other — not group-reported"; AI-DLC level 1–3; dimensions advanced), FR-034 (Manager+ create, BU Lead curates own BU), FR-035 (search + facets); User Story 3 scenario 2.
- Plan: data-model.md "Initiative register" — Initiative + **HarrisCategory seeded table** (`group_reported=false` for Other; categories are DATA, not enums); contracts/api.md "Register" endpoints. Stage history/NR/updates are **HAP-14** — this story sets `current_stage=Idea` on create only.
- Mockup: `docs/design/mockups/register-list.html` — binding incl. **stale rows flagged** (StaleRowFlag renders from `last_update_at`; nag jobs are HAP-18) and **red-RAG rows**. Components (A8): **StaleRowFlag**; A4 DataTable (sticky header, pagination >25, right-aligned numerics), badges/chips, one primary button ("New initiative").
- Files: `backend/src/Hap.Domain/**` (Initiative), `backend/src/Hap.Infrastructure/Persistence/**` (**EF migration #6**: Initiative, HarrisCategory + seed), register endpoints, `app/src/screens/register-list/**`, `app/src/components/StaleRowFlag/**`.
- **Serialise with: HAP-10 (migration chain — this migration lands after HAP-10's).**
- Blocked by: HAP-4
- Parallelisable: yes, with HAP-12 (disjoint files)

## Acceptance criteria
- [ ] HarrisCategory seeded from data (5 categories; "Other" has `group_reported=false`, customer-deployed flags correct); a grep-guard test asserts no category name string in C#/TS source (Art. II.4).
- [ ] `POST /api/initiatives`: Managers and BU Leads only, **within their own BU** (roles above BU level are read-only — FR-034 as amended 2026-07-21); requires name, BU, category, AI-DLC level (1–3 validated), owner; BU Lead can create/edit any entry in own BU, not other BUs; role-matrix test includes denied create attempts by Group Leader, Portfolio Leader, and HIG Executive.
- [ ] `GET /api/initiatives` supports full-text search on name/description and facets BU, category, stage, risk tier, AI-DLC level (FR-035; each facet has a test); dimension facet joins dimensions-advanced tags.
- [ ] `PUT /api/initiatives/{id}` permission: owner, creator, or BU Lead of that BU (test each + a denied case).
- [ ] UI implements the mockup: DataTable with columns BU, category, stage (+ Harris-mapped label), AI-DLC level badge, RAG chip, customers, last update; StaleRowFlag on rows >7d (amber) / >14d (red) with day count in text; filters panel; sticky header; pagination at >25 rows.
- [ ] Level badges always print the level number; RAG chips always carry a text label (A2 colour-independence; component tests).
- [ ] vitest-axe passes; strings externalised; tokens only.
- [ ] Wiki/guide (DR-0003, at closure): create `docs/wiki/register.md` (extended by HAP-14) and `docs/user-guide/initiative-register.md` (list/create portions).
- [ ] `./scripts/verify.sh` green (migration idempotent).

## Attempts / notes

**SPEC AUDIT 2026-07-21 (pre-start edit, story was todo):** creation authority bounded per FR-034 as amended — "Manager+" ambiguity removed; above-BU roles are read-only.
