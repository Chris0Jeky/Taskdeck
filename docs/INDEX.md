# Documentation Index

This directory is the canonical documentation entrypoint for Taskdeck.

## Active Authoritative Docs

- `STATUS.md`
  - Single source of truth for shipped behavior, verified test totals, and known gaps.
- `IMPLEMENTATION_MASTERPLAN.md`
  - Single source of truth for forward execution sequencing and priorities.
- `TESTING_GUIDE.md`
  - Canonical automated test commands, coverage map, and CI gate mapping.
- `MANUAL_TEST_CHECKLIST.md`
  - Canonical manual validation script for UI/API/ops workflows.
- `GOLDEN_PRINCIPLES.md`
  - Stable repository invariants and lightweight mechanically enforced principles.

## Recommended Read Paths

- New user / evaluator:
  - `START_HERE.md` -> `USER_MANUAL.md` -> `DEMO_PLAYBOOK.md`
- Maintainer / planner:
  - `STATUS.md` -> `IMPLEMENTATION_MASTERPLAN.md` -> `TESTING_GUIDE.md`
- Demo operator:
  - `START_HERE.md` -> `DEMO_PLAYBOOK.md` -> `SCENARIOS.md` -> `DOGFOODING_GUIDE.md`

## Active Operational Docs

- `GITHUB_PROJECT_AUTOMATION.md`
  - Canonical setup and automation rules for GitHub Project statuses/workflows.
- `GITHUB_LABEL_TAXONOMY.md`
  - Canonical descriptions and usage rules for repository labels (including `Priority I` to `Priority V`).
- `TASKDECK_HUMAN_OPERATIONS.md`
  - Human runbook for repository/project operations.
- `TaskdeckNextWorkChecklist.md`
  - Checklist-to-issue planning source for backlog seeding.
- `ISSUE_EXECUTION_GUIDE.md`
  - Execution order and operating protocol for agents tackling issue backlog.
- `SESSION_START_CHECKLIST.md`
  - Lightweight start-of-session runbook for branch hygiene, issue selection, and verification discipline.
- `STARTER_PACK_MANIFEST_SCHEMA.md`
  - PACK-01 manifest schema and validation contract for starter-pack foundations.
- `DEPLOYMENT_CONTAINERS.md`
  - Container deployment runbook for Docker images, compose baseline, reverse-proxy posture, and staging bootstrap.
- `DEPLOYMENT_TERRAFORM_BASELINE.md`
  - Terraform/IaC baseline for dev/staging/prod single-node AWS environments that host the existing Docker deployment layer.
- `DEPLOYMENT_HARDENING_MATRIX.md`
  - Deployment/container hardening verification matrix with pass/fail criteria and automation command (`#142`).
- `LLM_PROVIDER_SETUP_GUIDE.md`
  - Provider runtime posture and demo setup guide (`Mock`/`OpenAI`/`Gemini`), including Playwright full-demo auto-enable, suppression rules, and fresh-server behavior when live-provider overrides are injected.
- `IMPORT_ADAPTERS_GUIDE.md`
  - External import adapter runtime contract and CSV outreach-profile mapping/dedupe guide (`#75`).
- `RATE_LIMITING_POLICY.md`
  - API abuse-protection policy, throttle contract, and tuning guidance for rate-limited endpoints (`#81`, delivered).
- `SECURITY_OWASP_BASELINE.md`
  - OWASP baseline hardening record for API security headers and CSRF posture (`#80`, delivered).
- `SECURITY_DEPENDENCY_VULNERABILITY_POLICY.md`
  - Dependency vulnerability scan policy, severity SLAs, exception rules, and CI/release signal workflow (`#106`).
- `SECURITY_LOGGING_REDACTION.md`
  - Logging redaction policy and implementation guidance for capture/auth-sensitive flows (`#212`, delivered).
- `DEMO_PLAYBOOK.md`
  - Demo-runbook for seeded first-run walkthrough, scenario harness (`demo:run`), autopilot, demo director smoke, and opt-in stakeholder recorder.
  - Script entry points: `demo:seed`, `demo:run`, `demo:autopilot`, `demo:director`, `demo:snapshot`, `demo:director:smoke`.
- `SCENARIOS.md`
  - JSON scenario runner reference for deterministic demo/test setup, step contracts, and interpolation rules.
- `DOGFOODING_GUIDE.md`
  - Daily operating cadence for capture-triage-proposal execution and friction logging.
- `START_HERE.md`
  - Audience-first bridge doc for the first 15 minutes: product thesis, current golden path, demo entry point, and page map.
- `USER_MANUAL.md`
  - End-user reference for current shipped workbench flows; distinguishes the normal capture/review/board path from advanced/operator surfaces.
- `DEMO_EXPANSION_MIGRATION_SOT.md`
  - Canonical staged migration map for importing `TaskdeckDemoExpansion` (`v0` through `v3`) into this repository.
- `temp_description.txt`
  - Curated summary of demo-expansion provenance and source-folder mapping; points to migration source-of-truth.

## Active Tooling Docs

- `MCP_TOOLING_GUIDE.md`
  - MCP selection rules and safe operation patterns.
- `MCP_OPERATIONS_RUNBOOK.md`
  - MCP credential wiring, verification commands, and daily/weekly operator workflows.
- `DEVTOOLS_OBSERVABILITY_ADDON.md`
  - Debug workflow guidance for Playwright/DevTools/log signals.
- `FUTURE_HARNESS_BACKLOG.md`
  - Deferred tooling and harness upgrades.

## Working Notes (Non-authoritative)

- `analysis/`
  - Dated repository analysis snapshots and follow-through mapping notes (non-authoritative unless promoted into active docs).
  - includes `analysis/2026-03-07_mvp-expansion-reconciliation-tracker.md` for ongoing promotion of `docs/InReview/MVP_EXPANSION/` into canonical docs and backlog guidance.
  - includes `analysis/2026-03-07_mvp-expansion-gap-map.md` for the dated doc and issue reconciliation baseline.
  - includes `analysis/2026-02-23_testing-harness-synthesis.md` for testing-harness wave reconciliation (`#254` to `#260`).
  - includes `analysis/2026-02-23_outreach-crm-synthesis.md` for outreach CRM deferred-wave reconciliation (`#262` to `#268`).
- `InReview/`
  - Staging area for human briefs and currently active in-review materials before promotion into active docs (`STATUS.md`, `IMPLEMENTATION_MASTERPLAN.md`, testing/checklists).

## Archive

All superseded planning packs, snapshots, and historical notes live under:
- `archive/README.md`
- `archive/2026-02-13_phase4-doc-consolidation/`
- `archive/2026-02-16_docs-curation/`
- `archive/2026-02-25_docs-cleanup/`
  - Archived stale note artifacts (`personalNotes.txt`, `notesFromManualTesting.txt`) promoted/reconciled into canonical docs and issue wave execution.
- `archive/2026-02-25_inreview-repo-pack/`
  - Archived historical `REPO_PACK` planning/seeding bundle after promotion into canonical docs + issue tracker waves.

## Governance Rules

- Do not add new top-level planning docs by default.
- If a detail spec is no longer actively maintained, archive it.
- Every meaningful behavior change should update:
  1. `STATUS.md`
  2. `IMPLEMENTATION_MASTERPLAN.md`
  3. `TESTING_GUIDE.md` or `MANUAL_TEST_CHECKLIST.md` when verification flow changes.
- Every root doc in `docs/` must be listed in this index as one of:
  - Active authoritative
  - Active operational/tooling
  - Working notes (non-authoritative)
  - Archived

