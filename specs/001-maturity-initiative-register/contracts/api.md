# API Contract — Phase 1

REST/JSON over ASP.NET Core. Cookie-authenticated session (research D3). Errors: RFC 7807 problem+json. All timestamps UTC ISO-8601.

**Markers**
- **[S]** — response contains aggregates subject to N<4 + complement suppression (FR-014). Suppressed nodes return `{ "suppressed": true, "reason": "N<4" | "Complement" }` — never zeros, never omitted silently (FR-071).
- **[A]** — individual-level assessment read; the seam writes an `IndividualView` audit row **before** returning data; audit-write failure fails the request (FR-050). Viewing your own data is not audited as an individual view.
- **[PA]** — Platform Admin only.

Scope enforcement: every endpoint resolves the caller's scope via `Hap.Api/Authorization` (chain resolver + role scope). Out-of-scope requests return **404** (not 403) for person-addressed resources, to avoid existence leaks.

## Ports (the only two — Art. IX.4)

### IIdentityProvider
```
ChallengeAsync(HttpContext)        → initiates sign-in (dev: role-picker page)
SignInAsync(userKey)               → ClaimsPrincipal { person_id, explicit_roles[] }
SignOutAsync()
```
Local dev provider only in this feature. Contract test: principal shape identical to what an OIDC adapter would produce (person matched by external_ref).

### IDirectorySource
```
FetchSnapshotAsync()               → DirectorySnapshot { persons[]: { external_ref, name, email,
                                     job_title, manager_external_ref?, bu_code, employee_type,
                                     is_active, on_leave }, bus[]: { code, name, group, portfolio } }
```
Synthetic adapter reads `Hap.Synth` JSON output. Import applies snapshot then re-applies OrgOverrides; import writes are L3.

## Auth

| Method & path | Who | Notes |
|---|---|---|
| GET /auth/signin | anonymous | Dev provider role-picker (seeded users, one per role minimum) |
| POST /auth/signin | anonymous | `{ userKey }` → sets cookie |
| POST /auth/signout | any | — |

## Self scope (any authenticated person)

| Endpoint | M | Notes |
|---|---|---|
| GET /api/me | | Profile, computed roles, current cycle status |
| GET /api/me/assessment | | Current-cycle assessment, pre-populated from prior cycle (FR-062); includes purpose-limitation copy key (FR-066) |
| PUT /api/me/assessment/scores | | Upsert self scores/evidence while cycle Open; validates 0–3 per dimension of the cycle's framework version |
| POST /api/me/assessment/submit | | InProgress → Submitted |
| GET /api/me/assessment/result | | Moderated scores, comments, divergence highlights (FR-012); 404 until moderated/auto-adopted |
| GET /api/me/history | | Own past results per cycle |
| GET /api/me/team/summary | [S] | Own team aggregate only (FR spec §2 "Sees") |
| GET /api/me/export | | Right-of-access JSON export of all own data (FR-051); writes `Export` audit row |
| GET /api/frameworks/current | | Active framework version: dimensions + level descriptors (drives assessment UI from data) |

## Manager scope (has active direct reports)

| Endpoint | M | Notes |
|---|---|---|
| GET /api/team/reviews | | Review queue: state, leave flags (FR-069), carry-forward defaults (FR-063) |
| GET /api/team/members/{personId}/assessment | [A] | Chain-checked; includes self scores + evidence |
| PUT /api/team/reviews/{assessmentId} | | Moderated scores; **rejects Δ≥2 without comment** (FR-009); Submitted → Moderated |
| GET /api/team/completion | | Team completion + unmoderated % (FR-019/FR-068) — completion facts, no score data |

## BU Lead scope (EVP of the BU)

| Endpoint | M | Notes |
|---|---|---|
| GET /api/bus/{buId}/dashboard | [S] | Mean/floor rollups per dimension, completion %, cross-module initiative counts (FR-041), overdue updates |
| GET /api/bus/{buId}/people/{personId}/assessment | [A] | Anywhere in BU chain (spec §2) |
| GET /api/bus/{buId}/completion | | Per-team completion/unmoderated (FR-061 dashboards) |
| GET/POST /api/bus/{buId}/declarations | | Weekly AI-DLC declaration (FR-047); GET includes measured-evidence panel **[S]** |
| GET/POST /api/bus/{buId}/metrics | | Monthly metrics with prior-month carry-forward (FR-048) |
| GET /api/bus/{buId}/submissions/weekly?asOf= | | Generates + persists weekly Harris submission (FR-043/044/064/065) |
| GET /api/bus/{buId}/submissions/monthly?month= | | Monthly submission (FR-045/046) |

## Group / Portfolio / HIG Executive scope

| Endpoint | M | Notes |
|---|---|---|
| GET /api/org/tree | | Nodes visible to caller's scope |
| GET /api/org/{nodeType}/{nodeId}/rollup | [S] | Aggregates only — **no individual-level endpoint exists at these scopes by construction** (FR-025) |
| GET /api/initiatives?scope=… | | Register read across scope (spec §2) |

## Register (Manager+ create; BU Lead curates own BU — FR-034)

| Endpoint | M | Notes |
|---|---|---|
| GET /api/initiatives | | Full-text + facets: BU, stage, category, risk tier, model/vendor, dimension (FR-035) |
| POST /api/initiatives | | Category/level required per FR-027; taxonomy from seeded tables |
| GET /api/initiatives/{id} | | Full entity incl. stage history, NR lines, update trail |
| PUT /api/initiatives/{id} | | Field edits (permission: owner/creator or BU Lead of that BU) |
| POST /api/initiatives/{id}/stage | | **Forward-only transition** (FR-028); 409 on backward |
| POST /api/initiatives/{id}/updates | | Weekly RAG + note (FR-033) |
| POST /api/initiatives/{id}/nr-lines · DELETE …/nr-lines/{lineId} | | NR lines editable until referenced by a persisted monthly submission, then 409 (reconciliation integrity) |
| GET /api/initiatives/export.csv | | FR-039 |
| GET /api/reporting/register | | Read-only downstream API (FR-040); register data only, never assessment data |

## Platform Admin **[PA]**

| Endpoint | Notes |
|---|---|
| POST /api/cycles · POST /api/cycles/{id}/open · POST /api/cycles/{id}/close | Close runs auto-adoption (FR-068), snapshots + suppression verdicts (research D2/D4) |
| POST /api/cycles/{id}/late-override | Manager-or-admin per FR-002 (manager variant scoped to own team) |
| POST /api/admin/sync | Run directory import (synthetic adapter) |
| GET/POST /api/admin/overrides | Org overrides; each write audited (FR-023) |
| POST /api/admin/role-grants | Explicit grants (audited, FR-050) |
| GET /api/admin/audit?subject=&action=&from= | Read-only audit search (no mutation endpoints exist) |
| GET/POST /api/admin/frameworks | Framework/version management; version immutable once cycle-referenced (FR-054) |
| POST /api/admin/notifications/run | Deterministic trigger for reminder/escalation jobs (research D7) |
| POST /api/admin/retention/run | Retention job (FR-052); writes `RetentionErasure` audit rows |

## Contract tests (minimum, all `Category=PrivacyReporting` unless noted)

1. **Role matrix**: every endpoint × all seven seeded roles → allowed/denied exactly per scope table; person-addressed out-of-scope → 404.
2. **Suppression**: [S] endpoints against engineered hierarchy (n=3 team, sub-4 BU, single-team BU, 4-of-7 complement) → correct suppressed markers; no numeric leakage.
3. **Audit**: every [A] call writes exactly one IndividualView row; audit failure → request fails; no API mutates audit.
4. **Reconciliation**: every HarrisSubmissionLine equals its independent SQL recomputation; "Other" category absent from group-reported counts.
5. **Divergence**: PUT review with Δ≥2 and no comment → 422 (not PrivacyReporting-tagged; ordinary domain test).
