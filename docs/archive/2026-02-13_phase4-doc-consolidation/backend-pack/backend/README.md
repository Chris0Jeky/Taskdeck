# Backend Activation Documentation Pack

Last Updated: 2026-02-12
Primary Goal: Document a complete, safe, testable backend activation path that matches the shipped frontend overhaul and personal notes.

## Purpose

This folder is the canonical implementation-spec pack for Taskdeck backend activation.
It translates:
- current backend reality,
- frontend overhaul expectations,
- personal objectives for automation and LLM workflows,
into decision-complete engineering documentation.

Use this pack when:
- activating side-track backend capabilities into production runtime behavior,
- implementing automation proposal and approval flows,
- adding LLM chat-driven command orchestration,
- adding operational and reliability controls,
- expanding backend and E2E verification beyond smoke tests.

## Source Alignment

This pack is aligned with:
- `docs/personalNotes.txt`
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/frontend/README.md`
- `docs/frontend/IMPLEMENTATION_SUMMARY.md`
- current backend controllers/services under `backend/src`
- current test baselines under `backend/tests` and `frontend/taskdeck-web/tests/e2e`

This pack does not override implementation truth in `docs/STATUS.md`.

## Document Map

1. `docs/backend/01_BACKEND_ACTIVATION_ARCHITECTURE.md`
   - target architecture, phased activation strategy, and component boundaries
2. `docs/backend/02_FRONTEND_BACKEND_TRACEABILITY_MATRIX.md`
   - route and capability mapping from frontend surfaces to backend endpoints
3. `docs/backend/03_AUTHN_AUTHZ_ENFORCEMENT_SPEC.md`
   - endpoint-level auth and permission enforcement strategy
4. `docs/backend/04_AUTOMATION_FRAMEWORK_SPEC.md`
   - proposal-first automation framework and execution contracts
5. `docs/backend/05_LLM_CHAT_COMMAND_EXECUTION_SPEC.md`
   - chat session APIs, LLM adapter model, and command-to-proposal pipeline
6. `docs/backend/06_OPS_CLI_LOGS_OBSERVABILITY_SPEC.md`
   - CLI bridge, log query/streaming, and observability requirements
7. `docs/backend/07_ARCHIVE_RECOVERY_SPEC.md`
   - archive inventory and restore architecture
8. `docs/backend/08_BACKGROUND_WORKERS_RELIABILITY_SPEC.md`
   - queue and proposal worker design, health, retries, and failure handling
9. `docs/backend/09_TESTING_E2E_EXPANSION_PLAYBOOK.md`
   - test strategy, CI anti-hang controls, and acceptance gates
10. `docs/backend/10_SECURITY_GUARDRAILS_POLICY_SPEC.md`
    - security controls for automation, LLM usage, and ops endpoints
11. `docs/backend/11_AUTOMATION_EXAMPLES_CATALOG.md`
    - concrete example automations and approval behavior
12. `docs/backend/IMPLEMENTATION_SUMMARY.md`
    - implementation sequence, dependencies, and completion checklist

## Recommended Reading Order

1. Read `01_BACKEND_ACTIVATION_ARCHITECTURE.md`.
2. Use `02_FRONTEND_BACKEND_TRACEABILITY_MATRIX.md` to validate scope.
3. Lock security and identity rules with `03` and `10`.
4. Implement feature surfaces in this order: `04`, `05`, `06`, `07`, `08`.
5. Execute validation and rollout from `09`.
6. Track delivery progress against `IMPLEMENTATION_SUMMARY.md`.

## Documentation Maintenance Rules

When implementation starts, update this pack when changes occur in:
- endpoint routes, payloads, or status codes,
- authorization policy behavior,
- automation safety and approval rules,
- LLM provider integration interfaces,
- worker reliability and retry behavior,
- CI gating and E2E reliability controls.

If constraints change, update this backend pack before coding changes land.
