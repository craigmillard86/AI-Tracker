---
id: HAP-17
title: Harris submission review UI + PDF print view (harris-submission.html)
epic: E4-harris
wave: 2
fr: [FR-049]
risk: L1                # trigger: React screens displaying L3-produced data; no aggregation logic in UI
status: todo
estimate: {dev: M, qa: S}
worklog: []
closure: null
---
## Story
As an EVP, I review my pre-filled Harris submission on screen — declared level beside measured evidence with divergence flagged, every count linked to its register source — and export a PDF mirroring the form layout, so submission becomes review-and-transcribe instead of data assembly.

## Context
- Spec: FR-049 (PDF export mirroring form layout); User Story 4 scenarios (review, source links, export); root spec §6.1 form structure.
- Plan: research **D6** (print stylesheet + browser print dialog — NO PDF library; new dependency would be L2 and is rejected); contracts/api.md submission GET endpoints (data comes fully formed from HAP-16 — this story renders, never computes: a UI test asserts no arithmetic on counts in components).
- Mockup: `docs/design/mockups/harris-submission.html` — binding incl. **declared-vs-measured divergence state** and mark-reviewed affordance. Components (A8): **PrintLayout**; reuses **EvidencePanel**, **DivergenceFlag**; A4 tables/badges.
- Files: `app/src/screens/harris-submission/**`, `app/src/components/PrintLayout/**` only (no backend changes).
- Blocked by: HAP-16
- Parallelisable: yes, with HAP-18 and HAP-19 (disjoint files)

## Acceptance criteria
- [ ] Screen renders a generated weekly submission per the mockup: declaration section (declared level, RAG, next-level date) beside EvidencePanel; DivergenceFlag + sentence when declared ≠ measured; category × stage × level count tables; customers; Ideas Tried but Stopped; each count cell links to the register list pre-filtered to its source query (link-target test).
- [ ] Monthly view renders NR tables (Direct/Indirect × One-Time/Recurring, right-aligned $USD, descriptions) + Support/SOR sections per the root spec §6.1 monthly table.
- [ ] "Export PDF" opens the browser print dialog; `@media print` output: no shell/nav, white background, page-black text, section page-breaks, tables with border-gray rules (PrintLayout A8 spec) — asserted via print-media rendering test (Vitest + matchMedia print emulation) plus a documented manual check in QA.
- [ ] Mark-reviewed affordance updates review state via API and renders the reviewed state (mockup state).
- [ ] Numeric fields display exactly the persisted submission values — a test seeds a known submission and asserts screen values match line-for-line (no client-side recomputation).
- [ ] vitest-axe passes; strings externalised; tokens only.
- [ ] Wiki/guide (DR-0003, at closure): create `docs/user-guide/harris-submission.md`.
- [ ] `./scripts/verify.sh` green.

## Attempts / notes
