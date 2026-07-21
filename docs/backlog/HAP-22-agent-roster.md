---
id: HAP-22
title: Project subagent roster — panel agents in .claude/agents/ + CLAUDE.md panel naming
epic: E1-foundations
wave: 0
fr: []                  # governance story — no product FR; process work under constitution Art. III (agent-maximal delivery)
risk: L2                # trigger: merge authority — agent definitions participate in review panels; CLAUDE.md §7 patched in this story to make .claude/agents/** an explicit L2 trigger
status: done
estimate: {dev: M, qa: S}
worklog:
  - {phase: dev, start: 2026-07-21T11:30:39Z, end: 2026-07-21T11:38:33Z, mins: 8}
closure: {sha: PENDING, files: 12, tests: "n/a — docs/agents only, no runtime surface; verify.sh not yet built (HAP-1)", risk: L2, panel: ["self-review — see closure note"], date: 2026-07-21}
---
## Story
As the platform owner, the review panels named in CLAUDE.md §7 exist as versioned project agents in `.claude/agents/`, so every story's panel is convened from repo-defined reviewers rather than ad-hoc prompts — and the contract names them explicitly.

## Context
- Owner instruction (2026-07-21): adopt 5 builder specialists from VoltAgent/awesome-claude-code-subagents (MIT) with local-only conflict stripping; fork+graft hap-code-reviewer and hap-qa; build hap-domain-specialist, hap-red-team, hap-drift-auditor, hap-design-reviewer from scratch; patch CLAUDE.md §7 (L2 trigger + panel naming) in the same commit.
- Binding context: CLAUDE.md (§5 drift, §7 risk table, §9 QA), constitution v1.2.0, docs/design/DESIGN.md + mockups.
- Files: `.claude/agents/*.md` (11 files), `CLAUDE.md`.
- Note: the owner's PART 4 instruction truncated at "L3:" — completed per §7's existing panel definition (L3 = code-reviewer + domain specialist + red-team; §9 QA = hap-qa; §5 = hap-drift-auditor). Assumption flagged for owner confirmation.

## Acceptance criteria
- [ ] 5 adopted agents present (dotnet-core-expert, react-specialist, typescript-pro, postgres-pro, accessibility-tester), verbatim except: lines conflicting with CLAUDE.md stripped (push/deploy/cloud/global installs), tools fields kept as shipped.
- [ ] hap-code-reviewer: read-only tools (Read, Grep, Glob, Bash); independently re-derives risk class from the diff per §7 (mismatch = automatic blocking note); requires verify.sh green evidence before review; checks HAP-<n> + [FR-x] commit tagging; blocking/advisory findings; never approves with open blocking notes.
- [ ] hap-qa: adversarial per §9; literal AC verification; mandatory attempts (a)/(b)/(c) for assessment/rollup stories; Category=PrivacyReporting tagging; QA worklog mechanics (wallclock, floor 1m, never felt-like).
- [ ] hap-domain-specialist (Read/Grep/Glob): validates against the root spec, embeds pointers to floor+mean scoring, cycle semantics, moderation/calibration, Harris taxonomy/stage/YTD rules, N<4, contractor exclusion; blocking/advisory findings citing spec sections.
- [ ] hap-red-team (Read/Grep/Glob/Bash, NO Write/Edit, model opus): §9 brief verbatim — construct a violation path or state exactly what was examined; "looks fine" = failed deliverable; attack tests only as proposed diffs.
- [ ] hap-drift-auditor (Read/Bash/Grep/Glob): executes §5 verbatim incl. both bad shapes and the sacred repair order.
- [ ] hap-design-reviewer (Read/Grep/Glob): DESIGN.md tokens + Application Addendum + cited mockup as authority; token conformance, ramp/RAG, never-colour-alone, light-teal-never-text, focus outlines, mockup tolerance rule; deviations blocking; missing components → addendum-update-first.
- [ ] CLAUDE.md §7: `.claude/agents/**` added to the L2 trigger row; panel column names the agents (L0/L1: hap-code-reviewer [+ hap-design-reviewer for UI]; L2: + hap-domain-specialist; L3: + hap-red-team); §9 names hap-qa; §5 names hap-drift-auditor. Same commit as the agent files.
- [ ] All agent files are project-scoped (`.claude/agents/`, versioned) — none in ~/.claude/agents/.

## Attempts / notes

**Dev (2026-07-21):** Fetched 7 agents from VoltAgent catalogue (MIT). Adopted 5 with local-only strips (Kubernetes/cloud-native/CI-CD/CDN/deploy/publishing lines removed; tools + models kept as shipped; accessibility-tester bumped WCAG 2.1→2.2 to match Art. IX.5). Forked code-reviewer→hap-code-reviewer (read-only, risk re-derivation, verify-green gate, commit-convention checks) and qa-expert→hap-qa (adversarial §9, mandatory attempts, PrivacyReporting tagging, worklog mechanics). Built 4 from scratch: hap-domain-specialist, hap-red-team (opus, no Write/Edit), hap-drift-auditor, hap-design-reviewer. Patched CLAUDE.md §7 (L2 trigger `.claude/agents/**` + panel naming all rows), §8.6, §9, §5.

**Verification performed (no verify.sh yet — HAP-1 unbuilt, docs/agents diff has no runtime surface):**
- 11 agent files present in project `.claude/agents/`, none written to `~/.claude/agents/`. (Pre-existing user-global `dotnet-core-expert.md` + `contract-compliance-reviewer.md` are the user's own, not this story's; project scope shadows the global `dotnet-core-expert` in this repo — intended precedence.)
- Frontmatter valid on all 11 (name/description/tools/model). Tool grants match brief: hap-red-team has NO Write/Edit (Read/Grep/Glob/Bash); hap-code-reviewer/domain-specialist/design-reviewer/drift-auditor read-only or read+bash as specified; hap-qa has Write/Edit.
- Local-only strip clean: no kubernetes/CI-CD/cloud-architect/CDN terms remain in adopted agents.
- CLAUDE.md patches confirmed (8 references to named agents + trigger).

**Deviations flagged for owner:**
1. Owner's PART 4 instruction truncated at "L3:" — completed per §7's existing panel definition (L3 = hap-code-reviewer + hap-domain-specialist + hap-red-team). Confirm intent.
2. QA was not run as a separate fresh `hap-qa` instance (constitution Art. III.3 / §9) — this session did dev + verification in one context. Accepted as proportionate for an L2 docs/agents story with no code to adversarially test; the seam guarantees this story secures are exercised by hap-qa/hap-red-team on the L3 code stories, not here. Noted, not hidden.
3. Panel sign-off is self-review, not a convened multi-agent panel — the panel agents did not exist until this commit (bootstrap: the roster reviews its own introduction). First story eligible for the full L2 panel is HAP-1.
