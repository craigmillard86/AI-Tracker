# DR-0006 — A contractor manager gets no individual-score access (Q-006 ruling)

**Date:** 2026-07-21 · **Status:** Accepted · **Scope:** operative reading of FR-025 + constitution Art. VI for spec 001; no constitution text change
**Story key:** HAP-5 (visibility seam) · **Origin:** QUESTIONS.md Q-006 (L2 panel B2 reversal → owner ruling)

## Context

Contractors are excluded from assessment *participation* (FR-005/006), but a contractor can be a line manager. The management chain (§2) is structural, so read literally the chain rule would let a contractor manager moderate and therefore **view** their direct reports' individual assessment scores — UK-GDPR personal data accessed by a non-employee, relevant to G1 and the DPIA. The original provisional answer was "yes, the chain is structural." The HAP-5 L2 panel (B2) reversed this to **restrictive** under Art. V "uncertainty rounds up," pending owner/DPIA ratification, and the seam shipped the restrictive default behind a config flag.

## Decision

**A contractor manager receives no individual-score access to their reports.** The restrictive default is ratified:

1. A contractor ancestor is **excluded** from the individual-read grant. The seam walks *through* them (so employee managers above retain access) but the contractor themselves reads no individual score.
2. A pending review owned by a contractor manager **escalates** to the first non-contractor (employee) manager up the chain.
3. This holds regardless of any grant a contractor might carry — the contractor-eligibility check is independent of role classification, so no grant launders past it.

## Consequences

- HAP-5's Q-006 G1 precondition is **satisfied**: the owner has ratified the restrictive posture the seam already implements.
- `SeamOptions.ContractorManagerPolicy` (restrictive default) is now **ratified behaviour, not a provisional** — the code and tests need no change.
- The DPIA may state that **no non-employee ever holds individual-score access** to employees' assessment data through this application.
- A future reversal to permissive would be a **new DR + a config flip + an L3 seam test update** — it re-opens a UK-GDPR access path and cannot be a drive-by change.

## Relates

- **DR-0005** (Q-015 above-BU direct read) — the companion access ruling.

## Supersedes

Nothing. Resolves QUESTIONS.md Q-006 (raised in the retroactive audit; reversed to restrictive by the HAP-5 L2 panel B2; owner-ratified 2026-07-21).
