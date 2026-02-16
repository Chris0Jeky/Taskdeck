# Issue Execution Guide

Last Updated: 2026-02-16
Scope: How agents should execute the current GitHub issue backlog safely and in the right order.

## Purpose

Use this file when starting work from the issue backlog. It prevents out-of-order development and keeps security/ops/doc guardrails ahead of feature expansion.

## Start Protocol (Required)

1. Read `docs/STATUS.md`.
2. Read `docs/IMPLEMENTATION_MASTERPLAN.md`.
3. Read `docs/GITHUB_PROJECT_AUTOMATION.md`.
4. Confirm current branch is clean and based on `main`.
5. Pick the highest-priority issue whose dependencies are complete.

## Project Status Workflow (Required)

- Move issue to `Now` only when active implementation starts.
- Move issue to `Review` when PR is open and linked.
- Move issue to `Done` only after merge and verification notes are posted.
- If item is blocked by dependency or external input, move to `Blocked` and add blocking note.

## Execution Order (Dependency-Aware)

### Stage 0: Baseline and Governance

1. `#42` BASE-01 baseline verification pass on `main`
2. `#43` BASE-02 freeze active docs as source of truth
3. `#59` OPS-05 no-status safety audit view
4. `#41` OPS-06 weekly backlog seeding policy
5. `#55` DOC-01 weekly docs reconciliation ritual
6. `#60` DOC-02 active docs encoding normalization and stale-link sweep
7. `#56` REL-01 release-candidate hard gate policy

### Stage 1: Security and Identity Convergence (Highest Priority)

1. `#58` SEC-01 enforce auth on legacy controllers
2. `#33` SEC-02 claims-first identity retrofit
3. `#34` SEC-03 authz regression matrix tests
4. `#44` SEC-04 standardized API error-contract assertions

Notes:
- Policy is fixed: `401` unauthenticated, `403` authenticated-but-unauthorized/cross-user, `404` true missing.
- `#27` SEC-00 is already closed as the ratified policy decision record.

### Stage 2: UX Reliability and Interaction Safety

1. `#35` UX-01 archive lifecycle coherence
2. `#45` UX-02 drag/edit interaction safety
3. `#36` UX-03 command palette keyboard model
4. `#37` UX-04 activity selector discoverability
5. `#46` UX-05 escape behavior contract

### Stage 3: Starter Packs Foundation and Adoption

1. `#47` PACK-01 manifest RFC + schema
2. `#48` PACK-02 backend apply endpoint with dry-run/conflicts
3. `#49` PACK-03 frontend catalog preview/apply flow
4. `#50` PACK-04 first-party starter packs v1
5. `#51` PACK-05 deterministic QA/E2E fixture packs

### Stage 4: Automation and Provider Hardening

1. `#39` AUTO-01 production-capable provider strategy
2. `#40` AUTO-02 planner/executor hardening
3. `#57` MVP-01 chat-to-project bootstrap flow

### Stage 5: Tech Debt and Structural Follow-through

1. `#52` DEBT-01 nullability warning reduction (CS8618)
2. `#53` DEBT-02 log query scalability pass
3. `#54` DEBT-03 export/import implementation vs ADR deferral decision

## Per-Issue Delivery Checklist

1. Branch from latest `main`.
2. Keep change scope limited to issue acceptance criteria.
3. Add/update tests for behavior changes.
4. Run required verification commands.
5. Update docs (`STATUS`/`IMPLEMENTATION_MASTERPLAN`/test docs) if reality changed.
6. Open PR with linked issue and risk notes.
7. Move project item to `Review`.
8. After merge, move item to `Done` and post final verification summary.

## WIP Discipline

- Prefer one major implementation issue at a time.
- Keep at most:
  - 1 issue in `Now`
  - 1 issue in `Review`
- If parallel work is needed, split by non-overlapping layers (example: docs-only issue plus one code issue).

## Escalation Rules

Stop and ask for direction if:
- acceptance criteria conflict with `STATUS.md` reality,
- dependency issue is incomplete but required,
- the change would alter auth policy or project workflow conventions.

