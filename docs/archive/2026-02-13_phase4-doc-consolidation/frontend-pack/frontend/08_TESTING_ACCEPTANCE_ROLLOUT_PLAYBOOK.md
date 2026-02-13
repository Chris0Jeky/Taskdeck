# Testing, Acceptance, and Rollout Playbook

Last Updated: 2026-02-12
Status: Authoritative playbook for frontend overhaul rollout

## 1. Purpose

Provide step-by-step validation and rollout guidance for migrating from current frontend to redesigned workspace without behavioral regressions.

## 2. Rollout Strategy

Chosen strategy: Hybrid
- build new shell first
- migrate major surfaces in batches
- keep legacy routes until explicit cutover criteria are met

## 3. Phase Gates

## Gate A: Shell Foundation

Must pass:
- new shell renders and routes correctly
- legacy board still accessible within shell adapter
- command palette scaffold functional

## Gate B: Auth + Permissions

Must pass:
- login/register/session lifecycle stable
- protected routes enforce auth
- board access management flows work end-to-end

## Gate C: Boards Redesign

Must pass:
- board CRUD, column CRUD, card CRUD, label CRUD parity retained
- drag/drop parity retained
- keyboard workflow parity improved

## Gate D: Activity + Ops

Must pass:
- audit views functional
- logs and correlation inspection functional
- CLI/endpoint operator paths functional (if backend bridge endpoints delivered)

## Gate E: Automation + Archive + Portability

Must pass:
- queue/proposal review interactions functional
- archive restore UX functional
- export/import UX functional

## Gate F: Cutover

Must pass:
- legacy routes disabled or redirected
- all acceptance criteria met
- docs and test scripts updated

## 4. Test Matrix by Slice

## 4.1 Unit Test Requirements

Minimum unit targets:
- feature stores and reducers
- permission selectors
- shortcut context registry
- error mappers
- diff and log formatters

## 4.2 Integration Test Requirements

Minimum integration targets:
- auth forms and session restore
- board access CRUD forms
- board workspace mutation flows
- queue and proposal panels
- export/import wizards
- logs and ops console views

## 4.3 E2E Requirements

Mandatory E2E journeys:
1. register/login and create board
2. invite user and update board access role
3. keyboard-only: create/edit/move card flow
4. WIP rejection and recovery path
5. queue request -> process -> proposal review decision
6. export board -> import board -> verify data
7. archive entity -> restore entity -> verify visibility
8. diagnose failed mutation via logs correlation

## 5. Acceptance Criteria (Global)

The redesign is accepted only when all are true:
- no critical regression in existing board workflows
- all active backend slices have a frontend entry point
- all primary actions can be done by keyboard
- auth and role gating are correct and visible
- failed operations are diagnosable with trace context
- automated suites pass in CI

## 6. CI and Quality Gates

Required gate alignment:
- backend unit/integration/contract tests
- frontend unit tests
- E2E suite (expanded beyond smoke)

Recommended additions:
- dedicated keyboard-focused E2E subset
- accessibility checks (automated and manual)
- contract checks for endpoint matrix coverage

## 7. Manual Verification Additions

Extend `docs/MANUAL_TEST_CHECKLIST.md` with:
- auth/session scenarios
- board access management
- automation proposal review
- ops console command and logs flow
- archive restore and import/export scenarios

## 8. Cutover Checklist

Before cutover:
1. feature flags all green in staging
2. parity checklist signed by maintainers
3. incident rollback path documented
4. docs pack synced with final implementation

Cutover steps:
1. enable new shell/routes for all users
2. keep legacy fallback for one stabilization window
3. monitor logs/errors/latency
4. remove legacy routes after stabilization success

## 9. Risk Register and Mitigations

Risk: auth transition mismatch between frontend and backend
- Mitigation: endpoint-level identity mode flags and integration tests

Risk: keyboard regressions with redesigned components
- Mitigation: focus graph tests and keyboard-only E2E suite

Risk: ops console security exposure
- Mitigation: strict allowlist, role gates, audit logs for all runs

Risk: documentation drift
- Mitigation: update docs pack before each implementation phase merge

Risk: feature-flag complexity
- Mitigation: explicit flag ownership and removal deadlines

## 10. Ownership Model

Recommended owners:
- frontend architecture/spec maintenance: frontend maintainers
- endpoint matrix maintenance: frontend + backend shared ownership
- rollout playbook maintenance: release owner per cycle

## 11. Definition of Done

Playbook stage is complete when:
- all phase gates are defined and measurable,
- acceptance criteria are testable and automated where possible,
- cutover and rollback are fully documented,
- documentation-first package is sufficient for implementation without new design decisions.
