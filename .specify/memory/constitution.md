# HIG AI Adoption Platform — Constitution

<!--
Spec Kit constitution. Lives at .specify/memory/constitution.md.
Binding on every /specify, /plan, /tasks, /implement run and on every agent session.
Derived from the HIG AI Maturity Platform Specification v0.1 and the Project Helix
operating model (surfaces of truth · five-phase lifecycle · risk-scaled review · honest telemetry),
adapted for a FULLY LOCAL build: no Jira, no Azure, no external services required to deliver.
-->

**Version:** 1.2.1 · **Ratified:** 2026-07-20 · **Last amended:** 2026-07-21 (DR-0001, DR-0002, DR-0003, DR-0007) · **Owner:** Craig Millard (Vanguard)
**Story key:** `HAP` (local identifier scheme; survives unchanged if a tracker is adopted later)

## Preamble

This platform measures and drives AI adoption across HIG's ~23 business units. It will be **built the way it preaches**: agent-first, spec-driven, with humans consulted rather than queued behind. Delivery itself is evidence of AI-DLC Level 2–3 practice; every shortcut that breaks traceability destroys that evidence. Therefore process integrity is a feature, not overhead.

**This build is fully local.** The repo is the entire operation: backlog, process, code, telemetry, and history all live in it. No cloud service, tracker, or tenant is required to start or to finish Phase 1. Cloud concerns (hosting, Entra ID, any future tracker) are isolated behind adapters and deferred decision records, so going local now costs nothing later.

Two facts shape every article below: the system holds **employee personal data under UK GDPR** (individual maturity assessments), and it generates **numbers reported upward to Harris group leadership**. Privacy correctness and reporting correctness are this project's equivalents of a safeguarding gate — non-negotiable, mechanically triggered, never argued down.

---

## Article I — Surfaces of Truth, One Each

Nothing is duplicated as a source. Each surface is authoritative for exactly one thing; everything else links to it. All four live in the one local git repository.

| Surface | Authoritative for | Notes |
|---|---|---|
| **Spec bundle** (`docs/` + `specs/` + `.specify/`) | WHAT to build & WHY | The root application specification (`docs/spec/`), feature specs from `/specify` (under `specs/`, per DR-0002), plans from `/plan`, framework definitions (AI-DLC dimensions/levels as data), decision records, `QUESTIONS.md` |
| **Backlog-as-files** (`docs/backlog/`) | WHAT'S NEXT & WHAT SHIPPED | One markdown file per story (`HAP-N-slug.md`) with YAML frontmatter: status, wave, epic, FR-IDs, risk class, estimates, measured worklogs, closure record (merge SHA, panel, tests). `board.md` is a generated index, never hand-authored truth |
| **Git repository** (local) | WHAT THE CODE IS | .NET 8 API, React/TypeScript client, docker-compose, the spec bundle itself, and the append-only `ai-change-log.csv`. Every merge to `main` is a squash merge that passed its risk-class review. A remote (GitHub) may be added later; local `main` is the line of record until then |
| **`CLAUDE.md`** (repo root) | HOW WORK HAPPENS | The binding agent contract implementing this constitution: drift sweep, phase workflow, risk triggers, time-tracking mechanics, hard don'ts. Loaded by every agent session |
| **Wiki** (`docs/wiki/`) | HOW IT WORKS, AS BUILT | One page per subsystem, describing shipped behaviour (per DR-0003). A derived surface: it explains the system that exists; it never restates the spec (WHAT/WHY), the backlog (status), or decision records (why decided). Updated in the closure commit of any story that changes that subsystem — a stale wiki page is drift |
| **User guide** (`docs/user-guide/` + in-app help content) | HOW USERS OPERATE IT | End-user documentation for the mandated flows (per DR-0003; spec FR-072/FR-073). In-app help content is versioned data per Art. II.4; the printable guide is updated in the closure commit of any story changing user-facing behaviour |

**The join:** the story key is the foreign key of the whole operation. Branch `HAP-N-fr-x-slug`, commit `feat(HAP-N): … [FR-X]`, story file `docs/backlog/HAP-N-*.md`. The `[FR-ID]` travels beside the key in every commit so traceability to requirements survives any future re-tooling. The cost hook writes a row per agent as it completes, tagging its spend to the story via its worktree branch `HAP-N`; the session lead's own spend is written at session end as `session-lead` (DR-0007).

## Article II — Spec Before Code, Always

1. No story is implemented without a governing spec: the signed-off application specification is the root (`docs/spec/`); `/specify` produces feature specs from it (under `specs/`); `/plan` produces technical plans; `/tasks` produces the backlog story files. Code that cannot cite an FR-ID does not merge; FR-IDs are cited from the governing feature spec (FR-NNN scheme, per DR-0002), which traces to the root specification.
2. Specs define **WHAT and WHY, never HOW**. Plans own HOW and must pass the constitution check gates in the Spec Kit templates before tasks are generated.
3. Ambiguity routes to the owner, not to guesses: agents log unclear requirements in `docs/decisions/QUESTIONS.md`. Answers that change behaviour become **append-only decision records** — to change a decision, a new record supersedes it; history is never rewritten.
4. The AI-DLC framework, Harris taxonomy, and submission templates are **data, not code** (per spec §3.1, §6.1). Hard-coding a dimension, level descriptor, or Harris form structure is a constitution violation.

## Article III — Agent-Maximal Delivery

1. **Agents do the work.** Development, test authoring, code review, QA, documentation, and release notes are agent tasks by default. A human performing work an agent could do must be justified in a decision record.
2. **One story per agent at a time, in its own git worktree.** Parallelism comes from multiple agents on independent stories, never from one agent juggling.
3. **QA is a different agent instance** with an adversarial posture: verify every acceptance-criterion clause, add negative-path tests, and — for this project — attempt access-control violations (see Article VI). Honest attribution: tests written in the Dev window are Dev work.
4. **Humans are consulted, never queued.** The owner is not a merge gate. All merges are agent-reviewed at risk-scaled depth; the owner is consulted when a change raises genuinely novel questions and signs only the physical gates in Article VII.

## Article IV — The Five-Phase Story Lifecycle

Every story runs the same loop. The sync points state what each phase writes to which surface.

- **Phase 0 · Drift sweep (every session, before any story).** Check the last 20 squash merges on `main` against the other surfaces: a change-log row exists for each; the story file's frontmatter is `status: done` with a closure record carrying the merge SHA (known bad shapes: closure recorded but the merge absent from `main`; merged but the story file still `in-progress`); no stranded worktrees or branches. Whoever finds drift fixes it first. Repair order is fixed: **story file + change-log committed first, local cleanup last** — a dying shell can never strand the record.
- **Phase 1 · Setup.** Read the story file and its FR in the spec; check for prior attempts (never silently redo); create worktree + branch; classify risk from the trigger table (Article V); write the T-shirt Original Estimate into the story frontmatter. Writes: story file → `status: in-progress` + estimate (committed) · worktree → `.wallclock-HAP-N-dev` start timestamp.
- **Phase 2 · Dev loop.** Implement with tests alongside (TDD default). Run `./scripts/verify.sh` — the gate of record. Launch the review panel sized by risk class; loop until zero blocking notes. Writes: story file → measured dev worklog entry.
- **Phase 3 · QA loop.** Separate agent, adversarial. For any story touching assessment data or rollups: attempt to read scores outside the management chain, defeat small-group suppression, and desynchronise a rollup from its underlying records. Writes: story file → QA worklog entry + QA outcome notes.
- **Phase 4 · Closure.** Squash-merge to `main`; in the same closure sequence commit the story file's closure record (files, tests, merge SHA, risk class, panel) with `status: done`, the `ai-change-log.csv` row, and updates to any `docs/wiki/` and `docs/user-guide/` pages whose subject the story changed; then remove worktree and branch. Ends with the **four-box checklist**: squash merge on main · story file closed with SHA · change-log row on main · worktree gone. **Three of four is not Done.**

## Article V — Mechanical Risk Classification

Classification is a first-match-wins trigger table, not judgment. The table is ordered **highest class first** and is evaluated top-down: a diff touching triggers in more than one row takes the first (highest) matching class. **Uncertainty rounds up.** The privacy and reporting rules make L3 non-negotiable: a one-line diff in a visibility predicate is L3.

| Class | Triggered by touching… | Review panel | To merge |
|---|---|---|---|
| **L3** | **RBAC/visibility predicates and the management-chain resolver · small-group (N<4) suppression · individual-assessment read paths · audit-log write/integrity paths · GDPR retention/erasure/export · Harris submission generation and its aggregation queries · directory-import writes to identity or hierarchy** | 3 agents — + adversarial red-team with a fixed brief: **construct a violation path** (read a score outside the chain, break an aggregate's suppression, make a Harris figure disagree with its records) **or state exactly what was examined** | verify + privacy/reporting regression suite + all three sign-offs; flagged for the relevant Article VII gate |
| **L2** | schema/migrations, directory-import logic, auth abstraction/session handling, scoring & rollup maths, NR aggregation, cycle state machine, notification scheduling, `verify.sh` itself, any new dependency | 2 agents — code-reviewer + domain specialist | verify green + both sign-offs |
| **L1** | UI components, screens, copy, playbook/nudge content, email templates | 1 agent | verify green + sign-off |
| **L0** | docs, comments, test-only additions | 1 agent | verify green + sign-off |

## Article VI — Privacy and Reporting Are the Safeguarding Seam

1. **The visibility seam is architectural from day one.** Every read of assessment data passes through a single authorisation layer (chain-of-command resolver + role scope + N<4 suppression). No query path may reach assessment rows without it.
2. **Synthetic data only — for the entire local build.** All people, orgs, hierarchies, and scores come from the deterministic generators in `scripts/synth/`. No real employee data exists in this build phase at all; the real-data unlock (Gate G1 plus a production identity source) happens at deployment, outside this repo's local scope.
3. **Identity is an abstraction.** The application authenticates against an `IIdentityProvider` port. The local build ships a dev provider (seeded synthetic users covering every role, selectable at sign-in) and the automated test suite runs against it. The Entra ID OIDC adapter is a deferred, decision-recorded story — the seam and RBAC logic it plugs into are fully built and gate-tested locally.
4. **Reported numbers must reconcile.** Every figure in a generated Harris submission must be reproducible from underlying records by an independent query. The QA agent for any L3 reporting story must demonstrate the reconciliation.
5. **Audit integrity is append-only.** Views of individual-level data, score changes, role grants, and org overrides are logged; no code path may update or delete audit rows.

## Article VII — Wave-Gated Delivery with Human-Witnessed Gates

Delivery proceeds in waves per the specification's phasing; build effort is measured in days, but **gates hold regardless of speed**. Both gates run entirely locally on synthetic data, witnessed on the owner's machine:

- **Gate G1 — Privacy gate (end of the RBAC/assessment wave).** A human-witnessed run on the local stack demonstrating: no role can read an individual score outside the management chain; N<4 aggregates suppress; audit rows appear for every individual-level view — exercised across every seeded role from EVP to HIG Executive. **M1 = zero leaks.** G1 is a precondition for any future deployment with real data.
- **Gate G2 — Reporting gate (before the first system-generated Harris submission is trusted).** A human-witnessed reconciliation of a full weekly and monthly submission against underlying register and metrics records, and against the Harris form semantics (stage mapping, level meaning, YTD-vs-current-month rules).

A timeboxed Wave-0 spike proves the visibility-predicate approach against the synthetic hierarchy generator (23 BUs, multi-group, multi-portfolio, edge cases: manager gaps, moves mid-cycle, sub-4 teams) before the architecture hardens.

## Article VIII — Honest Time and Money

Three signals, captured separately, **never back-filled from each other** — the gaps between them are the calibration signal.

1. **The plan:** human-equivalent T-shirt Original Estimate in each story's frontmatter (DEV and QA lines; QA ≈ ⅓–⅕ of Dev), set at Phase 1, never revised after work starts.
2. **The measurement:** AI wall-clock worklog entries in the story frontmatter — UTC timestamp at in-progress, another at close, difference logged; floor 1 minute, sanity check at 4× estimate. Never "felt like" time. Lost timestamp = log nothing, say so.
3. **The bill:** the cost hook appends token spend to `.claude/cost-log.csv` — one row per agent on completion (tagged to its story via the worktree branch) plus one `session-lead` row at session end for the orchestration loop (DR-0007). Gitignored, never hand-edited. Joined with worklogs → $/story and $/hour — the platform's own delivery becomes an AI-adoption data point HIG can cite.

## Article IX — Engineering Standards

1. **Stack (fixed by decision record):** .NET 8 Web API · React/TypeScript · PostgreSQL · **docker-compose local runtime** (API, client, Postgres, mailpit for email capture). The eventual cloud target (Azure per the proposal) is a deferred decision record; nothing in the codebase may assume a specific cloud. Identity per Article VI.3.
2. **TDD is the default**; `./scripts/verify.sh` is the gate of record and must run green before any review panel. It requires only Docker and local toolchains — no network beyond package restore.
3. **Migrations are forward-only** and reviewed at L2 minimum; framework versions, submission templates, and taxonomies are versioned data with immutability once used by a cycle (spec §3.1).
4. **Simplicity first:** no speculative abstraction beyond the two decision-recorded ports (identity, directory source), no plugin architecture, no second framework engine until a second consumer exists. YAGNI violations are review-blocking notes.
5. **Accessibility:** WCAG 2.2 AA for the assessment flow is acceptance criteria, not polish.

## Article X — Hard Don'ts

- Never merge with a red `verify.sh`, a missing review sign-off, or an incomplete four-box checklist.
- Never touch an L3 trigger path without L3 review, regardless of diff size.
- Never introduce real employee data into this build; never weaken a synth generator's distributions silently.
- Never hand-edit the change log, cost log, audit tables, or a closed story file's history; never rewrite a decision record — supersede it.
- Never hard-code framework content, Harris form structure, or hierarchy mappings.
- Never build against a cloud service this repo cannot run without.
- Never silently redo a prior attempt; never guess at ambiguity — route it to `QUESTIONS.md`.
- Never report "felt like" time or back-fill estimates from actuals.

## Governance

This constitution supersedes ad-hoc practice. All `/plan` outputs must pass a constitution check; violations must be justified in the plan's Complexity Tracking section or the plan is rejected. Amendments follow the decision-record process: a superseding record + version bump here (MAJOR for principle removals/redefinitions, MINOR for new articles or material expansions, PATCH for clarifications). `CLAUDE.md` is the runtime enactment of this document and must be updated in the same commit as any amendment. Compliance is verified by the Phase 0 drift sweep and by review panels at every merge.
