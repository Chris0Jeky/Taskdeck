# Frontend Overhaul Documentation Pack

Last Updated: 2026-02-12
Primary Goal: Documentation-first execution package for the full frontend overhaul, with no immediate implementation required.

## Purpose

This folder is the canonical implementation-spec pack for the Taskdeck frontend overhaul.
It translates product intent, personal notes, and current system reality into decision-complete engineering documentation.

Use this pack when:
- sequencing frontend overhaul work,
- implementing new frontend slices,
- validating backend/frontend contract alignment,
- planning QA and rollout for the redesigned UX.

## Source Alignment

This pack is aligned with:
- `docs/personalNotes.txt`
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/PR_MERGE_READINESS_REPORT_2026-02-12.md`
- current controllers under `backend/src/Taskdeck.Api/Controllers`
- current frontend app under `frontend/taskdeck-web/src`

This pack does not override status truth in `docs/STATUS.md`.

## Document Map

1. `docs/frontend/01_OVERHAUL_ARCHITECTURE.md`
   - target information architecture, app shell, component/store/API patterns, and implementation sequencing
2. `docs/frontend/02_ENDPOINT_ENTRYPOINT_MATRIX.md`
   - endpoint-by-endpoint mapping from backend surface to frontend entry points and UI behavior
3. `docs/frontend/03_KEYBOARD_ACCESSIBILITY_SPEC.md`
   - keyboard-first interaction model and accessibility baseline for the redesign
4. `docs/frontend/04_AUTH_PERMISSIONS_ROLLOUT_SPEC.md`
   - auth/session/permission UI and rollout strategy through claim-based identity
5. `docs/frontend/05_AUTOMATION_REVIEW_FLOW_SPEC.md`
   - proposal/review/diff model for automation and LLM-assisted workflows
6. `docs/frontend/06_OPS_CONSOLE_LOGS_SPEC.md`
   - CLI exposure, endpoint explorer, logs and observability UX
7. `docs/frontend/07_ARCHIVE_EXPORT_IMPORT_SPEC.md`
   - archive recovery, export/import UX, and migration-safe behavior
8. `docs/frontend/08_TESTING_ACCEPTANCE_ROLLOUT_PLAYBOOK.md`
   - detailed test matrix, acceptance criteria, and cutover playbook

## How To Use This Pack

Recommended working order:
1. Read `01_OVERHAUL_ARCHITECTURE.md` for system shape.
2. Use `02_ENDPOINT_ENTRYPOINT_MATRIX.md` as implementation contract.
3. Implement interaction foundations from `03_KEYBOARD_ACCESSIBILITY_SPEC.md`.
4. Roll out identity and roles with `04_AUTH_PERMISSIONS_ROLLOUT_SPEC.md`.
5. Add advanced surfaces (`05`, `06`, `07`) in slices.
6. Validate every slice using `08_TESTING_ACCEPTANCE_ROLLOUT_PLAYBOOK.md`.

## Documentation Rules

When implementation starts, update these docs when changes occur in:
- route topology,
- API contract assumptions,
- role/permission behavior,
- keyboard interaction contracts,
- rollout phases and feature flags,
- acceptance criteria.

If constraints change, update this pack first, then implementation.
