# DR-0001 — Risk trigger table is ordered highest class first

**Date:** 2026-07-21 · **Status:** Accepted · **Scope:** Constitution Article V (PATCH: 1.1.0 → 1.1.1)
**Story key:** n/a (governance amendment, pre-backlog)

## Context

Article V declares risk classification a "first-match-wins trigger table", but the table as ratified was ordered L0 → L3 while `CLAUDE.md` §7 orders it L3 → L0. Read literally, first-match-wins over an L0-first table would classify a diff touching both documentation and an authorization predicate as L0 — the opposite of the intended "uncertainty rounds up" posture. Classification is meant to be mechanical, so the ambiguity must be resolved in the text, not left to judgment.

## Decision

The Article V table is reordered **L3 → L0**, matching `CLAUDE.md` §7, and Article V now states explicitly that the table is evaluated top-down: a diff touching triggers in more than one row takes the first (highest) matching class. No trigger, panel, or merge-requirement content changed.

## Consequences

- Constitution and `CLAUDE.md` risk tables now agree in both content and evaluation order; `CLAUDE.md` required no change.
- Constitution version bumped to 1.1.1 (clarification, PATCH per Governance).

## Supersedes

Nothing — first decision record. Amends the ratified text of constitution v1.1.0.
