---
id: HAP-6
title: Framework engine — versioned framework data seeded from docs/frameworks
epic: E1-foundations
wave: 0
fr: [FR-001, FR-054]
risk: L2                # trigger: EF migrations / schema
status: todo
estimate: {dev: M, qa: S}
worklog: []
closure: null
---
## Story
As a Platform Admin, the AI-DLC framework (dimensions, levels, descriptors) exists as versioned data seeded from `docs/frameworks/ai-maturity-sdlc.v1.json`, immutable once a cycle uses it, so assessment content is never hard-coded and historic scores stay tied to the version they were scored against.

## Context
- Spec: "Module 1: Assessment Framework & Cycles" FR-001 (frameworks/versions/dimensions/descriptors as data), FR-054 (version immutability); "Key Entities" Framework/FrameworkVersion/Dimension/LevelDescriptor.
- Plan: data-model.md "Framework (data, not code)"; contracts/api.md `GET /api/frameworks/current`, `[PA] GET/POST /api/admin/frameworks`; constitution Art. II.4 (hard-coding a dimension name is a violation).
- Files: `backend/src/Hap.Domain/**` (framework entities), `backend/src/Hap.Infrastructure/Persistence/**` (**EF migration #2** + seeder reading the JSON), API endpoints in `Hap.Api`.
- **Serialise with: HAP-3 (migration chain — this migration lands after HAP-3's).**
- Blocked by: HAP-1
- Parallelisable: yes, with HAP-4 (disjoint files) — but only start after HAP-3 merges (migration chain)

## Acceptance criteria
- [ ] Seeder loads `docs/frameworks/ai-maturity-sdlc.v1.json` into Framework/FrameworkVersion(v1)/7 Dimensions/28 LevelDescriptors; idempotent (re-seed = no-op); content equality test against the JSON file (names, order, descriptor text).
- [ ] `GET /api/frameworks/current` returns the active version with dimensions + descriptors in display order — the payload that drives the assessment UI entirely from data.
- [ ] Immutability (FR-054): once a Cycle row references a FrameworkVersion, any write to that version or its dimensions/descriptors is rejected (domain guard test; enforced in the write path, not by trust).
- [ ] No dimension name, level name, or descriptor string appears in C#/TS source: a verify-time grep test asserts strings like "AI Delegated" and "How AI is leveraged" occur only under `docs/frameworks/` and seed output (constitution Art. II.4 guard).
- [ ] Versioning: creating v2 (draft) leaves v1 assessments untouched; `current` returns the active version only (test).
- [ ] `./scripts/verify.sh` green (migration idempotent, run twice).

## Attempts / notes
