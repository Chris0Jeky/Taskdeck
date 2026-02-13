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

## Active Supporting Docs

- `personalNotes.txt`
  - Working note capture from product/development perspective.
  - Non-authoritative: ideas from this file must be reconciled through `STATUS.md` and `IMPLEMENTATION_MASTERPLAN.md` before execution.

## Archive

All superseded planning packs, feature-spec bundles, and historical snapshots live under:
- `archive/README.md`
- `archive/2026-02-13_phase4-doc-consolidation/` (latest major consolidation)

## Governance Rules

- Do not add new top-level planning docs by default.
- If a detail spec is no longer actively maintained, archive it.
- Every meaningful behavior change should update:
  1. `STATUS.md`
  2. `IMPLEMENTATION_MASTERPLAN.md`
  3. `TESTING_GUIDE.md` or `MANUAL_TEST_CHECKLIST.md` when verification flow changes.
