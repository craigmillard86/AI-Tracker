# QUESTIONS.md — open questions routed to the owner

Append-only. Each entry is dated and keyed (story or spec). Answers that change behaviour become decision records (DR-NNNN); record the answer here with a pointer, never edit history.

---

## 2026-07-21 · spec 001 (maturity-initiative-register) · Q-001 — Directory attributes across 23 BUs

Which AD/Entra attributes reliably carry the group/portfolio mapping and employee type across all 23 BUs? Expect inconsistency between BU tenants/domains; a directory hygiene audit is needed before rollout.

**Blocks:** deployment/onboarding only — not the local build (directory source is a port; all local data is synthetic).
**Owner action:** commission the directory audit before Cycle 1 onboarding.
**Status:** OPEN

## 2026-07-21 · spec 001 (maturity-initiative-register) · Q-002 — Harris form semantics confirmation

Confirm with the Harris AI Dashboard owner: (a) the stage mapping (Idea+Evaluation → Ideation; Pilot → Development; Production+Scaled → Production; Retired → Ideas Tried but Stopped), and (b) that an initiative's "level" means the AI-DLC autonomy level the initiative embodies (1–3).

**Blocks:** Gate G2 trust in the first real submission — not local build work, which proceeds on the documented interpretation (FR-027, FR-064); the mapping is configuration data, so a corrected answer is a data change, not a code change.
**Owner action:** confirm with whoever owns the Harris AI Dashboard definitions.
**Status:** OPEN

## 2026-07-21 · spec 001 (maturity-initiative-register) · Q-003 — Harris ingestion API roadmap

Does the Harris dashboard team plan an ingestion API? If yes, direct submission replaces the review-and-transcribe step (Phase 3 candidate).

**Blocks:** nothing in Phase 1; informs Phase 3 scope only.
**Owner action:** ask when convenient; low urgency.
**Status:** OPEN
