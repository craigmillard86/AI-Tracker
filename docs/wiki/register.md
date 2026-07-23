# Initiative register — as built

_Subsystem shipped by HAP-13 (core + list UI). Stage progression, NR/updates, and nag jobs are HAP-14/HAP-18 — this page will be extended there. Describes shipped behaviour only; WHAT/WHY live in `docs/spec/` + `specs/`, decisions in `docs/decisions/`._

## Domain (HAP-13 — FR-026/027/034/035)

`Initiative` (`Hap.Domain/Register`) is the register record: identity fields (name, description, business unit, owner, sponsor, customers), a Harris **category** reference, an **AI-DLC level** (1–3, validated on create), a **risk tier**, a RAG status, `CurrentStage` (set to `Idea` on create only — progression is HAP-14), and `LastUpdateAt` (drives the stale-row flag). `CreatedByPersonId` is recorded alongside `OwnerPersonId` because the PUT-authority rule distinguishes creator from owner (data-model.md gap-fill, domain-specialist concurred; no People FK by deliberate convention, matching AuditLog/OrgOverride).

Migration **#6** (`20260722214357_AddInitiativeRegister`) lands the `initiatives`, `harris_categories`, and `harris_stage_map` tables. It follows HAP-10's migration in the chain (serialised). `RiskTier`/`RagStatus`/`Stage` persist via `HasConversion<string>()`.

### Harris taxonomy is DATA, not code (Art. II.4)

The five Harris categories and the AI-DLC→Harris stage map are seeded from `docs/frameworks/harris-taxonomy.v1.json` by `HarrisTaxonomySeeder`, never from enums or hard-coded strings. "Other" carries `group_reported=false` (excluded from group-level Harris reporting). A grep-guard architecture test (`HarrisTaxonomyContentNotHardcodedTests`) loads the banned category-name strings *from the JSON at test time* and asserts none appears in C#/TS source — so adding a category can never smuggle its name into code.

## Endpoints (`Hap.Api/RegisterEndpoints.cs`)

`POST /api/initiatives` · `GET /api/initiatives` · `GET /api/initiatives/{id}` · `PUT /api/initiatives/{id}`. The file queries only `Initiatives`/`HarrisCategories`/`BusinessUnits`/`FrameworkVersions`/`Dimensions` — **no read path over `Assessments`/`AssessmentScores`** (L2, not L3; confirmed by QA inspection).

### Create authority (FR-034, as amended 2026-07-21)

Managers and BU Leads may create **within their own BU only**; roles above BU level (Group Leader, Portfolio Leader, HIG Executive) and plain individuals are **read-only** and are denied. `ResolveWritableBusinessUnitAsync` returns the caller's own writable BU, then the handler compares it to the request body's `businessUnitId` and **403s on mismatch** — a forged BU in the body can never smuggle a different BU past the caller's actual scope. The above-BU leadership anchors are intercepted and return `null` **before** the bare `IsManager` fallback, so a Group Leader (structurally `IsManager=true`) cannot fall through to the plain-manager branch.

> Write authority here uses `HierarchyRoleResolver`'s depth-derived labels to gate WRITE access over **non-sensitive register data** — this is within the Q-014 clearance carve-out for non-assessment uses and does **not** touch the visibility-seam's individual-score scope. If Q-014's uniform-depth assumption is ever wrong, the blast radius is register create/curate rights, not assessment-score visibility.

### Update authority (FR-026)

`PUT` is allowed for the **owner, the creator, or the BU Lead of that initiative's BU**. A BU Lead of a *different* BU is denied (404). `businessUnitId` and `currentStage` are **not** declarable on `UpdateInitiativeRequest`; default System.Text.Json behaviour silently drops unknown members, so forged BU/stage fields in raw JSON are ignored — BU and stage are immutable via update (QA-verified with a raw-JSON smuggling attempt).

### Validation

`aiDlcLevel` must be 1–3 (0 and 4 both 422). An unknown `categoryId` 422s before `Initiative.Create`. `riskTier` uses `TryParseRiskTier`, which requires **both** `Enum.TryParse` and `Enum.IsDefined` — a named-but-unrecognised value ("Medium") *and* a numeric string ("99") both 422 (the `Enum.IsDefined` check is load-bearing: `Enum.TryParse` alone accepts any integral-convertible string, which then round-trips intact through `HasConversion<string>()`). Null/blank `riskTier` is a valid omitted-field default of `Low`. Search/facet values are fully parameterised via EF (`EF.Functions.ILike`, `.Contains`) — no injection surface.

### Search + facets (FR-035)

`GET` supports case-insensitive full-text search on name/description plus six facets: BU, category, stage, risk tier, AI-DLC level, and dimension (joins dimensions-advanced tags). The BU filter dropdown lists **all** BUs, not only onboarded ones.

## List UI (`app/src/screens/register-list`)

Per `register-list.html`: an A4 DataTable (sticky header, right-aligned numerics, pagination at >25 rows) with columns BU · category · stage (+ Harris-mapped label) · AI-DLC level badge · RAG chip · customers · last update. Search input is debounced ~250ms.

- **StaleRowFlag** (`components/StaleRowFlag`) renders from `LastUpdateAt`: amber >7d, red >14d, with the day-count in text (nag jobs that act on staleness are HAP-18).
- **LevelBadge** always prints the level number (`L{n}`); **RagChip** always carries a text label — colour is reinforcement only, never the sole signal (A2 colour-independence, component tests).
- vitest-axe clean; strings externalised (`en.ts`); tokens-only styling.
