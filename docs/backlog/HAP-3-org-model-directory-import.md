---
id: HAP-3
title: Org model, directory import port, overrides, and append-only audit foundation
epic: E1-foundations
wave: 0
fr: [FR-020, FR-021, FR-022, FR-023, FR-024, FR-050, FR-053]
risk: L3                # trigger: directory-import writes to people/hierarchy + audit-log write paths
status: todo
estimate: {dev: L, qa: M}
worklog: []
closure: null
---
## Story
As the platform team, we need the org hierarchy (person → team → BU → group → portfolio) imported from the directory port with a manual-override layer and an append-only audit log, so role scopes and every later feature stand on trustworthy org data.

## Context
- Spec: "Functional Requirements — Module 1: Organization Structure" (FR-020..024), "Data Management & Audit" (FR-050, FR-053); "Key Entities" Person/BusinessUnit/Group/Portfolio/OrganisationOverride/AuditLog.
- Plan: data-model.md "Org & identity" + "Audit & GDPR" (Team is DERIVED from manager links — no Team table); contracts/api.md "Ports — IDirectorySource" and "[PA] POST /api/admin/sync, GET/POST /api/admin/overrides"; research D1 (audit fails closed).
- Files: `backend/src/Hap.Infrastructure/Directory/**` (IDirectorySource + SyntheticDirectoryAdapter reading Hap.Synth output), `backend/src/Hap.Infrastructure/Persistence/**` (DbContext + **EF migration #1**: Person, BusinessUnit, GroupOrg, Portfolio, OrgOverride, RoleGrant, AuditLog), `backend/src/Hap.Infrastructure/Audit/**` (append-only writer), `backend/src/Hap.Domain/**` (entities), sync endpoint in `Hap.Api`.
- **Migration chain: this is migration #1 — HAP-6 serialises behind it.**
- Blocked by: HAP-2
- Parallelisable: no

## Acceptance criteria
- [ ] `POST /api/admin/sync` imports the full synthetic snapshot: person count, manager links, BU/group/portfolio mappings match `directory.json` exactly (integration test asserts counts and spot rows).
- [ ] Re-running sync is idempotent (no duplicates, updated fields overwrite) and never deletes: leavers become `is_active=false`, rows retained (FR-024).
- [ ] A person changing manager or BU in a modified snapshot is updated on next sync; OrgOverride rows survive re-sync and re-apply after import (FR-023 test with an override + re-sync).
- [ ] Every override write produces exactly one AuditLog row (`OrgOverride`); test tagged `Category=PrivacyReporting`.
- [ ] AuditLog has no UPDATE/DELETE path: EF model maps no setters for mutation, no API mutates it, and `Hap.Architecture.Tests` asserts no code path calls Update/Remove on the AuditLog DbSet (tagged `Category=PrivacyReporting`).
- [ ] Audit write failure fails the audited operation (fails closed — research D1): test forces audit failure and asserts the override write rolls back.
- [ ] Migration applies idempotently under `./scripts/verify.sh` (run twice = no-op); verify green.
- [ ] QA (adversarial, fresh agent): attempt to create/modify a person via any non-sync endpoint (must not exist); attempt to delete an audit row via SQL through any exposed path (none exists); document both in this file.

## Attempts / notes
