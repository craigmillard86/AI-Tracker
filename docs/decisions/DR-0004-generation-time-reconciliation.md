# DR-0004 — Harris reconciliation is proven at generation time

**Date:** 2026-07-21 · **Status:** Accepted · **Scope:** operative reading of constitution Art. VI.4 for spec 001 (FR-046); no constitution text change
**Origin:** retroactive spec-quality audit (checklist F1) + L2 panel review advisory A2

## Context

Constitution Art. VI.4: "Every figure in a generated Harris submission must be reproducible from underlying records by an independent query." The clause is tense-neutral — it does not say *when* reproducibility must hold. The register's classification fields (category, AI-DLC level, customer counts) are mutable without temporal history, so a submission generated last week cannot be recomputed after those fields change; only stage carries immutable history (FR-028/FR-064). Leaving the window undefined made the reconciliation obligation unimplementable as literally read, and HAP-16's original QA criterion inherited that impossibility.

## Decision

Reconciliation is proven **at generation time**:

1. At the moment a submission is generated, every line MUST equal an independent recomputation from raw records — exactly, no tolerance (FR-046; PrivacyReporting suite).
2. Persisted submissions are **immutable**. Later register edits never retroactively alter or re-validate a persisted document; regeneration produces a new reconciled document with a new as-of.
3. **Stage is the deliberate asymmetry**: lines involving stage (including Ideas Tried but Stopped at stage-when-retired) read the immutable stage history as of the generation instant; mutable classification fields are read at their then-current value.
4. **Gate G2 witnesses a freshly generated** weekly + monthly submission (HAP-20 walkthrough), never a stale persisted one.

## Consequences

- Art. VI.4's trust intent is preserved (every reported figure is independently checkable when it is produced — which is when it is transcribed to Harris); the impossible retro-recomputation reading is explicitly rejected rather than silently ignored.
- Encoded in: spec FR-046, research D5, HAP-16 QA criteria, HAP-20 walkthrough criterion.
- If future requirements demand retro-recomputable submissions, the remedy is field-level temporal history (a new decision superseding this one), not weakening reconciliation.

## Supersedes

Nothing — first interpretation record for Art. VI.4. Complements DR-0001..DR-0003.
