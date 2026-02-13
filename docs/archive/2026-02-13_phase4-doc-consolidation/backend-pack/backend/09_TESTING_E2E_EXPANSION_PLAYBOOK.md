# Testing and E2E Expansion Playbook

Last Updated: 2026-02-12

## 1. Objective

Expand backend and Playwright coverage from smoke-level to release-grade regression coverage, with explicit anti-hang CI controls.

## 2. Test Expansion Targets

### 2.1 Backend Unit

Add coverage for:
- authz policy evaluator matrix,
- proposal lifecycle and risk policy engine,
- archive restore conflict strategies,
- command allowlist validation,
- worker retry and dead-letter logic,
- chat instruction classification and guardrails.

### 2.2 Backend Integration

Add API test files:
- `AutomationProposalsApiTests.cs`
- `ArchiveApiTests.cs`
- `OpsApiTests.cs`
- `LogsApiTests.cs`
- `LlmChatApiTests.cs`
- `AuthEnforcementRegressionApiTests.cs`

Each must include success and failure paths.

### 2.3 Frontend E2E (Playwright)

Add suites:
- `auth-regression.spec.ts`
- `access-role-matrix.spec.ts`
- `automation-proposals.spec.ts`
- `chat-to-proposal.spec.ts`
- `archive-restore.spec.ts`
- `ops-logs-correlation.spec.ts`
- `long-session-regression.spec.ts`

## 3. Mandatory E2E Journeys

1. Failed login attempts do not create redirect loops.
2. User can login and session restore survives refresh.
3. Queue request generates proposal.
4. Reviewer edits and approves proposal; mutation appears on board.
5. Reviewer rejects destructive proposal with reason.
6. Archive restore returns entity to visible workspace.
7. Ops command run is visible with correlated logs.
8. Viewer cannot execute admin-only operations.

## 4. CI Anti-Hang Controls

Workflow controls:
- step timeout for Playwright execution (`timeout-minutes`).
- global Playwright timeout and per-test timeout settings.
- `--max-failures` for early abort on cascading failures.
- capture artifacts on failure:
  - traces
  - screenshots
  - videos
  - backend logs
- explicit web-server readiness probing before tests start.

Recommended defaults:
- Playwright job timeout: 20 minutes
- per-test timeout: 45 seconds
- expect timeout: 8 seconds
- max failures: 3

## 5. Operational Validation

Add non-functional test checks:
- worker liveness and queue drain behavior,
- SSE stream stability over reconnects,
- command timeout and truncation behavior,
- log query latency bounds.

## 6. Test Data and Determinism

- keep isolated DB file per E2E run (`TASKDECK_E2E_DB`).
- seed deterministic users, boards, and permissions.
- avoid clock-sensitive assertions without tolerance windows.

## 7. Coverage Gates

Minimum merge gates for backend activation slices:
- backend unit suite passing,
- backend integration suite passing with new API files,
- frontend unit suite passing,
- expanded E2E subset passing (not only smoke),
- no flaky-test quarantine without owner and issue link.

## 8. Failure Triage Protocol

For failed E2E:
1. inspect trace first,
2. inspect backend logs by correlation ID,
3. classify root cause:
   - UI timing
   - API contract mismatch
   - auth/policy mismatch
   - worker delay
4. patch deterministic wait/assertion strategy before retrying.

## 9. Acceptance Criteria

- expanded E2E suite executes reliably under CI timeout budget,
- backend activation features are covered by dedicated integration tests,
- hang-prone behavior is guarded by explicit timeout and readiness controls.
