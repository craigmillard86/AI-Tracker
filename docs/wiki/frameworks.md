# Framework engine — as built

_Subsystem shipped by HAP-6 (FR-001, FR-054). Describes shipped behaviour only; WHAT/WHY live in `docs/spec/` + `specs/`, status in `docs/backlog/`, decisions in `docs/decisions/`._

## What exists

The AI-DLC maturity framework as **versioned data**, not code. Assessment content (dimensions, levels, descriptors) is seeded from a JSON file and served to the assessment UI; no dimension name or descriptor string is ever hard-coded in source (constitution Art. II.4).

### Entities (`Hap.Domain/Frameworks`)

- `Framework` (natural key `Key`) → `FrameworkVersion` (key `(FrameworkId, VersionNumber)`, status Draft/Active/Retired) → `Dimension` → `LevelDescriptor`.
- v1 seeds to 7 Dimensions and 28 LevelDescriptors (4 levels × 7 dimensions). `LevelName` is denormalised onto each `LevelDescriptor` (no separate Level entity — data-model.md lists only Dimension/LevelDescriptor).
- `Dimension`/`LevelDescriptor` are immutable by type (get-only, constructor-bound), matching the OrgOverride/RoleGrant/AuditLog convention.

### Immutability (FR-054)

Once a cycle adopts a version, that version and its content freeze. Enforcement is **structural, not by trust**: `FrameworkVersion` owns `AddDimension(...)` / `AddLevelDescriptor(...)`, each calling `EnsureMutable()` (throws `FrameworkVersionLockedException` when locked) before constructing the child. `Activate()`/`Retire()` also call `EnsureMutable()`, so a locked version is frozen in every respect. `Lock()` is a one-way, idempotent flip — **it has no caller in this subsystem; HAP-7's cycle-adoption logic is its first caller.**

Known gap, deferred to HAP-7 (see the HAP-6 closure carry-forwards): the `Dimension.Create` / `LevelDescriptor.Create` static factories remain public, so the "sole guarded path" is convention, not compiler-enforced; a raw-factory write persists content under a locked version. Unreachable while nothing calls `Lock()`; HAP-7 must make the factories internal and add a DB-layer guard when it introduces the first `Lock()` caller.

### Seeding & versioning (`Hap.Infrastructure/Frameworks`)

- `FrameworkSeeder` loads `docs/frameworks/ai-maturity-sdlc.v1.json` (via `FrameworkDefinitionLocator`), idempotent by natural key — re-seed is a no-op; content is built only the first time a version is seen. Runs at startup and on demand via `POST /api/admin/frameworks`.
- The seeder auto-activates a framework's **first** version when none is Active yet (bootstrap rule — the JSON's own `status` is not consulted; see QUESTIONS.md Q-011, provisional). A later v2 draft never auto-activates; creating it leaves v1 and `current` untouched.

### Art. II.4 content guard

`FrameworkContentNotHardcodedTests` derives its banned-string list from the JSON itself at test time and greps `backend/src`, `backend/tests`, and `app/src` for literal occurrences (case-insensitive). A differentiated length floor (multi-word phrases ≥5 chars, single words ≥10) keeps the AC example strings in scope while excluding collision-prone short words like "Tasks". Known coverage limit: single-word dimension names under 10 chars ("Timing"/"Impact") are unguarded — deferred to a word-boundary-matching pass.

## API (`Hap.Api/FrameworkEndpoints.cs`)

- `GET /api/frameworks/current` — the active version with dimensions in `DisplayOrder` and descriptors in level order (the payload that drives the assessment UI entirely from data); 404 if nothing active.
- `GET/POST /api/admin/frameworks` — list, and run the seeder. (Auth placement relative to HAP-4's `/api` group is settled at HAP-4 merge: `/current` authenticated to any role, `/admin/frameworks` PlatformAdmin-only.)
