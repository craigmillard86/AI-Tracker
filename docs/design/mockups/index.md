# HAP — UI Mockup Set

**HAP (HIG AI Adoption Platform)** — L1 review reference mockups for the HIG AI Maturity & Initiative Register application.

These are self-contained static HTML files (inline CSS, Google Fonts only). **Layout, information architecture, and the states shown are binding; exact pixels are not.** Markup is intentionally semantic (real tables, labels, buttons) so build agents read it as intent. Design system: `docs/design/DESIGN.md` (Deep Teal Corporate + Application Addendum). Framework: `docs/frameworks/ai-maturity-sdlc.v1.json`. Spec: `docs/spec/hig-ai-maturity-platform-specification.md`.

Design at 1440px content width; degrades to 1280px. All synthetic data — invented BUs and people, no real employees.

| # | Screen | Role / entry point | Purpose | Spec area (FR) |
|---|--------|--------------------|---------|----------------|
| 1 | [dashboard-bu.html](dashboard-bu.html) | BU Lead (EVP) | BU maturity summary (floor level + mean trend), per-dimension bars with cross-module initiative counts, assessment completion %, initiative pipeline by Harris stage, overdue weekly updates | §3.4 rollups; §4.3 cross-module scorecard |
| 2 | [heatmap-group.html](heatmap-group.html) | Group / Portfolio Leader | 23 BUs × 7 dimensions maturity heatmap + league table (rank, floor level, mean, 6-cycle trend, completion %); portfolio/group filter; small-group suppression | §4.3 group heatmap; §3.4 distribution; §2 N≥4 suppression |
| 3 | [assessment-self.html](assessment-self.html) | Individual | Monthly self-assessment — one dimension per section, four level descriptors as selectable cards, pre-populated from last month, optional evidence, progress + projected floor, save/submit | §3.3.1 self-assessment; Appendix A framework |
| 4 | [assessment-moderation.html](assessment-moderation.html) | Manager | Self vs manager scores side by side per dimension, divergence flagged, comment required at Δ≥2, carry-forward default, calibration delta, review queue | §3.3.2 manager review; §3.3.3 moderated score |
| 5 | [register-list.html](register-list.html) | Manager+ / BU Lead | Filterable initiative table (BU, Harris category, stage→Harris map, AI-DLC level badge, RAG, customers, last update); stale rows flagged; new initiative | §4.1 entity; §4.2 filtering & stale-entry |
| 6 | [register-detail.html](register-detail.html) | BU Lead / owner | One initiative: identity/tech, dimensions advanced, NR lines (Direct/Indirect × One-Time/Recurring), governance & risk flags, stage-history timeline, weekly RAG + one-line update composer | §4.1 full entity; §4.2 weekly update; §6.1 NR |
| 7 | [harris-submission.html](harris-submission.html) | EVP review | Pre-filled weekly Harris submission: declared AI-DLC level beside measured-evidence panel (declared vs measured divergence), category × stage × level counts with source links, mark-reviewed | §6.1 weekly Harris submission |
| 8 | [bu-forms.html](bu-forms.html) | EVP / delegate | Weekly BU AI-DLC declaration (level, next-level date, RAG, note) and monthly BU metrics (support internal + customer, SOR) as two compact forms | §6.2 BU capture points |

## Shown non-happy states (per content rules)
- **Overdue weekly updates** — 3 flagged on the BU dashboard; 11-day overdue banner on register detail.
- **Red RAG / off-track** — Claims Triage Copilot (register + detail), several register rows.
- **Incomplete assessment** — self-assessment shown at 5 of 7 dimensions, projected floor L0.
- **Suppressed aggregate** — Verdant AgTech rendered as "— (group too small, n=3 < 4)" in heatmap and league table.
- **Divergence** — manager review Δ≥2 on one dimension forcing a required comment; declared-vs-measured AI-DLC divergence on the Harris submission.
- **Maturity gap** — "Value measured" weakest dimension with zero initiatives advancing it (dashboard callout).

## Design-system conformance notes
- Shell (top bar + left nav) is the only deep-navy #002c36 surface; content is white / #f7f8f9.
- brand-teal #008099 for links, active nav, focus, and exactly one filled pill primary button per view.
- Montserrat 700 page titles (30px); Inter Tight everywhere else at app scale; no Roboto, no hero type.
- Maturity ramp L0 #D5DDE3 · L1 #67bfd0 · L2 #008099 · L3 #002c36 — level value always printed in the cell/badge.
- RAG green #1F7A4D · amber #B45309 · red #B3362B, always with a text label. Radii 50/10/8px; card-elevation shadow; 3px teal focus outlines.
