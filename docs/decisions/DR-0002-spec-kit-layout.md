# DR-0002 — Adopt the Spec Kit layout: feature specs under `specs/`, FR-NNN citation scheme

**Date:** 2026-07-21 · **Status:** Accepted · **Scope:** Constitution Articles I & II (PATCH: 1.1.1 → 1.1.2) + CLAUDE.md §§1, 2, 3, 6
**Story key:** n/a (governance amendment, pre-backlog)

## Context

The contract as ratified placed all specs under `docs/spec/` and had commits cite FR-IDs "from the root spec" (section-numbered, e.g. `FR-3.3`). The Spec Kit tooling actually in use (`/speckit-specify` → `/speckit-plan` → `/speckit-tasks`) writes feature specs to `specs/<nnn>-<slug>/spec.md` with flat `FR-NNN` requirement identifiers, tracked via `.specify/feature.json`. The first feature spec (`specs/001-maturity-initiative-register/`) was generated before this mismatch was resolved, leaving two competing homes for specs and two competing FR-ID schemes. Nothing had cited an FR-ID yet, so the decision was still free.

## Decision

1. **Feature specs live under `specs/`**, produced by the Spec Kit flow. `docs/spec/` holds the root application specification only, committed verbatim as the signed-off WHAT/WHY document (`docs/spec/hig-ai-maturity-platform-specification.md`, Draft v0.1).
2. **The FR citation scheme is `FR-NNN` from the governing feature spec.** Commits (`[FR-NNN]`), branch names (`fr-<nnn>`), and story frontmatter (`fr: [FR-NNN]`) all cite the feature spec's identifiers. Each feature spec traces its requirements to the root specification; the root spec's own section numbering is not a citation scheme.
3. **FR numbers are append-only identifiers**, not an ordering: amendments add new FR-NNN values (e.g. FR-060+ added by the 2026-07-21 review) and never renumber existing ones once cited.
4. Framework content extracted from the root spec's Appendix A lives as data at `docs/frameworks/ai-maturity-sdlc.v1.json` per Article II.4.

## Consequences

- Constitution Article I (spec-bundle surface) now reads `docs/` + `specs/` + `.specify/`; Article II.1 names the FR-NNN scheme. Version 1.1.2.
- CLAUDE.md updated in the same commit: repo map gains `specs/`, §1 points commits at the feature spec's FR-IDs, §6 Phase 1 reads the feature spec first, backlog example uses `fr: [FR-008]`.
- The generated feature spec's branch field was corrected to the Spec Kit name (`001-maturity-initiative-register`); `HAP-<n>` story keys remain reserved for backlog stories, one per story, never for spec directories.

## Supersedes

Nothing removed; amends constitution v1.1.1 (ratified layout wording). Complements [DR-0001](DR-0001-risk-table-ordering.md).
