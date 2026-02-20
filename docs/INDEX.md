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

## Active Tooling Docs

- `MCP_TOOLING_GUIDE.md`
  - MCP selection rules and safe operation patterns.
- `DEVTOOLS_OBSERVABILITY_ADDON.md`
  - Debug workflow guidance for Playwright/DevTools/log signals.
- `FUTURE_HARNESS_BACKLOG.md`
  - Deferred tooling and harness upgrades.

## Working Notes (Non-authoritative)

- `personalNotes.txt`
  - Idea capture only. Must be reconciled through `STATUS.md` and `IMPLEMENTATION_MASTERPLAN.md` before execution.

## Archive

All superseded planning packs, snapshots, and historical notes live under:
- `archive/README.md`
- `archive/2026-02-13_phase4-doc-consolidation/`
- `archive/2026-02-16_docs-curation/`

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
