---
id: HAP-15
title: BU capture forms — weekly AI-DLC declaration + monthly metrics (bu-forms.html)
epic: E4-harris
wave: 2
fr: [FR-047, FR-048]
risk: L2                # trigger: EF migrations/schema (evidence panel reads only seam-published aggregates)
status: todo
estimate: {dev: M, qa: S}
worklog: []
closure: null
---
## Story
As an EVP (or delegate), I declare my BU's weekly AI-DLC level beside the measured evidence, and complete the monthly Support/SOR metrics with YTD carry-forward — the two capture points that feed everything in the Harris submission that isn't derivable from the register.

## Context
- Spec: "Reporting & Harris Submissions" FR-047 (weekly declaration + evidence panel showing measured distribution and trend), FR-048 (monthly metrics; YTD auto-carry; SOR current-month only); root spec §6.2; User Story 7 scenarios.
- Plan: data-model.md BUAIDLCDeclaration + BUMonthlyMetrics (**EF migration #8**); contracts/api.md "BU Lead scope" declarations/metrics endpoints (declaration GET includes measured-evidence panel **[S]** — consumed from HAP-11's rollup output, no new score queries).
- Mockup: `docs/design/mockups/bu-forms.html` — two compact forms. Components (A8): **EvidencePanel** (reuses DimensionBar; divergence rendered via DivergenceFlag + sentence); A4 forms (8px field radius), one primary button per form.
- Files: domain + endpoints, `backend/src/Hap.Infrastructure/Persistence/**` (migration #8), `app/src/screens/bu-forms/**`, `app/src/components/EvidencePanel/**`.
- **Serialise with: HAP-14 (migration chain).**
- Blocked by: HAP-11
- Parallelisable: no (migration chain)

## Acceptance criteria
- [ ] `POST /api/bus/{buId}/declarations`: declared level 0–3, next-level date, RAG, optional note; one per BU per week (second post same week → 409 or upsert per contract — pick upsert, test it); BU Lead/delegate of that BU only (role test).
- [ ] `GET /api/bus/{buId}/declarations` returns declaration history + the measured evidence panel (floor distribution + mean trend from rollups), and the declared-vs-measured divergence value that HAP-16 will report (FR-047).
- [ ] `POST /api/bus/{buId}/metrics` month N: YTD fields pre-populated from month N-1 for editing; SOR field starts empty/current-month-only (FR-048 tests for both behaviours).
- [ ] UI implements the mockup: two forms side-by-side/stacked per layout, EvidencePanel beside the declaration (level distribution + trend, divergence sentence), YTD carry-forward visible as pre-filled values.
- [ ] vitest-axe passes; strings externalised; tokens only.
- [ ] Wiki/guide (DR-0003, at closure): create `docs/user-guide/bu-declarations-and-metrics.md`.
- [ ] `./scripts/verify.sh` green (migration idempotent).

## Attempts / notes
