# Quickstart — run & validate locally

Prerequisites: Docker Desktop, .NET 8 SDK, Node 20+. No credentials, no network beyond package restore.

## Run

```bash
# 1. Generate the synthetic directory (deterministic — canonical seed baked into the wrapper)
./scripts/synth/generate.sh

# 2. Start the stack: api, app, postgres, mailpit
docker compose up -d --build

# 3. Import the directory + seed framework v1 and Harris taxonomy (idempotent)
curl -X POST localhost:8080/api/admin/sync            # or: sign in as Platform Admin → Admin → Run sync

# 4. Open the app
#    app:      http://localhost:5173
#    api:      http://localhost:8080
#    mailpit:  http://localhost:8025   (all outbound email lands here)
```

Sign-in is the local dev provider's role picker — one seeded user per role: Individual, Manager, BU Lead, Group Leader, Portfolio Leader, HIG Executive, Platform Admin.

## Gate of record

```bash
./scripts/verify.sh
```
Builds both stacks warnings-as-errors, spins a disposable Postgres, applies migrations idempotently, runs backend + frontend tests, lint, typecheck, and **always** the `Category=PrivacyReporting` suite. Green exit or the story is not reviewable.

## Validation scenarios

### V1 — Assessment round-trip (FR-007…FR-012, FR-062, FR-066)
1. Platform Admin: open a cycle for the active framework version.
2. Individual: complete self-assessment (purpose-limitation banner visible; descriptors inline; 7 dimensions), submit.
3. Manager: open review queue → moderate; set one dimension Δ≥2 → comment is forced.
4. Individual: view result — manager scores, comment, divergence highlight.
**Expect**: moderated scores are the scores of record; calibration delta recorded.

### V2 — Cycle close & unmoderated handling (FR-068)
1. Second individual submits; manager does **not** review.
2. Admin: close the cycle.
**Expect**: assessment state `AutoAdopted`, flagged unmoderated; team unmoderated % > 0 on dashboards; excluded from calibration delta.

### V3 — Privacy spot-checks (G1 rehearsal; SC-005/SC-006)
For **each** of the seven roles:
1. Attempt `GET /api/team/members/{id}/assessment` for a person outside your chain → **404**.
2. Find the seeded n=3 team and sub-4 BU → aggregates render "Suppressed", API returns suppression marker, no numbers.
3. Find the seeded single-team BU / 4-of-7 complement case → child aggregate suppressed (differencing defense).
4. As Manager, view a report's assessment → Platform Admin audit search shows the IndividualView row.
**Expect**: zero reads outside the chain; suppression holds including complements; every individual view audited.

### V4 — Register & weekly discipline (FR-026…FR-037)
1. Manager: create an initiative (category, AI-DLC level, dimensions advanced).
2. Advance stage Idea → Evaluation → Pilot; attempt Pilot → Evaluation → **409** (forward-only).
3. Post a weekly RAG update; add NR lines (Direct/Recurring etc.).
4. Age an initiative past 7 days (admin notifications run) → owner nag lands in mailpit; past 14 → BU Lead escalation.

### V5 — Harris submission reconciliation (G2 rehearsal; SC-004)
1. BU Lead: submit weekly declaration (evidence panel shows measured distribution; note declared-vs-measured divergence surfaced).
2. Generate the weekly submission → counts by category × Harris stage × level; verify a category count by hand-filtering the register (mapped stages, "Other" excluded).
3. Enter monthly metrics; generate monthly submission → NR YTD lines match register NR lines summed to the month.
4. Export: print dialog → PDF mirrors the on-screen form.
**Expect**: every line matches its independent recount — exact, not approximate.

### V6 — Notifications (FR-057, FR-061)
Admin → run notifications: cycle reminders to non-responders, escalation summaries to managers/BU Lead near close. All visible in mailpit; none sent externally.

## Gate readiness (flag, never self-certify)

- **G1 (privacy)** — after HAP-12: owner witnesses V3 executed across all seven roles on the synthetic stack. M1 = zero leaks.
- **G2 (reporting)** — after HAP-20: owner witnesses V5 for a full weekly + monthly submission reconciled line-by-line.
