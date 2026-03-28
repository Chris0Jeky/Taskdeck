# Codex-Friendly Task Catalog

Last Updated: 2026-03-28

This folder contains well-scoped, self-contained tasks designed for token-efficient agents like Codex.
Each task file is a standalone prompt with everything needed: source paths, pattern files, acceptance criteria, and verify commands.

## Structure

```
docs/codex-tasks/
  README.md                  # this index
  frontend-api/              # API module unit tests
  frontend-composables/      # composable unit tests
  frontend-stores/           # store unit tests (real coverage, not demo)
  backend-domain/            # domain entity unit tests
  backend-services/          # service/worker unit tests
```

## Task Tiers (by complexity)

| Tier | Folder | Difficulty | ~Tokens | Description |
|------|--------|------------|---------|-------------|
| 1 | `frontend-api/` | Trivial | Low | Mock HTTP, verify URL/payload. Copy-paste pattern. |
| 2 | `frontend-composables/` | Easy | Low-Med | Pure function tests + Vue composable lifecycle. |
| 3 | `frontend-stores/` | Medium | Medium | Pinia store tests with mocked API layer. |
| 4 | `backend-domain/` | Easy | Low | Entity construction, validation, invariants. |
| 5 | `backend-services/` | Medium | Medium | Service tests with dependency mocking. |

## How to Use with Codex

Each `.md` file contains:
- **Branch name** to create
- **Source file** to read (the code under test)
- **Pattern file** to follow (an existing test with the same shape)
- **Test cases** to implement
- **Verify command** to run
- **GitHub issue** link for traceability

Give the entire `.md` file as the Codex prompt. The agent should:
1. Read the source file and pattern file
2. Create the test file
3. Run the verify command
4. Commit and push on the specified branch

## Task Index

| ID | Issue | Task File | Scope |
|----|-------|-----------|-------|
| TST-CODEX-01 | [#415](https://github.com/Chris0Jeky/Taskdeck/issues/415) | `frontend-api/labelsApi-tests.md` | labelsApi: 4 methods |
| TST-CODEX-02 | [#416](https://github.com/Chris0Jeky/Taskdeck/issues/416) | `frontend-api/columnsApi-tests.md` | columnsApi: 5 methods |
| TST-CODEX-03 | [#417](https://github.com/Chris0Jeky/Taskdeck/issues/417) | `frontend-api/usersApi-tests.md` | usersApi: all methods |
| TST-CODEX-04 | [#418](https://github.com/Chris0Jeky/Taskdeck/issues/418) | `frontend-composables/useErrorMapper-tests.md` | 3 functions, ~10 cases |
| TST-CODEX-05 | [#419](https://github.com/Chris0Jeky/Taskdeck/issues/419) | `frontend-composables/useEscapeToClose-tests.md` | 3 cases |
| TST-CODEX-06 | [#420](https://github.com/Chris0Jeky/Taskdeck/issues/420) | `frontend-composables/useShortcutContext-tests.md` | context stack, ~7 cases |
| TST-CODEX-07 | [#421](https://github.com/Chris0Jeky/Taskdeck/issues/421) | `frontend-stores/auditStore-tests.md` | real unit tests (not demo) |
| TST-CODEX-08 | [#422](https://github.com/Chris0Jeky/Taskdeck/issues/422) | `frontend-stores/queueStore-tests.md` | real unit tests (not demo) |
| TST-CODEX-09 | [#423](https://github.com/Chris0Jeky/Taskdeck/issues/423) | `backend-domain/CardComment-entity-tests.md` | entity invariants |
| TST-CODEX-10 | [#424](https://github.com/Chris0Jeky/Taskdeck/issues/424) | `backend-domain/Notification-entity-tests.md` | entity invariants |
| TST-CODEX-11 | [#425](https://github.com/Chris0Jeky/Taskdeck/issues/425) | `backend-domain/AutomationProposal-entity-tests.md` | lifecycle + transitions |
| TST-CODEX-12 | [#426](https://github.com/Chris0Jeky/Taskdeck/issues/426) | `backend-domain/LlmUsageRecord-entity-tests.md` | entity invariants |
| TST-CODEX-13 | [#427](https://github.com/Chris0Jeky/Taskdeck/issues/427) | `backend-services/OutboundWebhookSignature-tests.md` | expand 1 -> full |
| TST-CODEX-14 | [#428](https://github.com/Chris0Jeky/Taskdeck/issues/428) | `backend-services/WorkerHeartbeatRegistry-tests.md` | registration + stale |
| TST-CODEX-15 | [#429](https://github.com/Chris0Jeky/Taskdeck/issues/429) | `backend-services/CompositeBoardRealtimeNotifier-tests.md` | delegation + fault isolation |

## Conventions

- Frontend tests: `frontend/taskdeck-web/src/tests/{category}/{name}.spec.ts`
- Backend tests: `backend/tests/Taskdeck.{Layer}.Tests/{subfolder}/{Name}Tests.cs`
- All tests must pass before commit
- One task = one branch = one PR = one issue
