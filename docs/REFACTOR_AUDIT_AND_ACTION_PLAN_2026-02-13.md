# Refactor Audit and Action Plan (February 13, 2026)

## Scope Reviewed

This audit covered the current branch `copilot/refactor-codebase-for-maintainability-again` against `main`, including:

- Backend API controllers and new shared backend refactor files.
- Backend tests added for error mapping.
- Frontend API/store utility extraction changes.
- Shared frontend type changes and lockfile churn.

## Findings

### Backend

1. **Primary refactor goal is achieved**
   - Error mapping duplication was correctly removed from controllers by introducing `ResultExtensions.ToErrorActionResult()`.
   - Duplicated authenticated-user extraction was correctly consolidated into `AuthenticatedControllerBase`.

2. **Remaining backend duplication/consistency gaps**
   - A remaining status-code switch existed in `ChatController` stream error handling.
   - Error payloads were still manually shaped with anonymous objects in multiple places.
   - A raw string literal (`"ValidationError"`) remained in one controller.

### Frontend

1. **Primary refactor goal is achieved**
   - Duplicate query-string builders and error message extractors were successfully centralized.

2. **Type-level coupling introduced by refactor**
   - `ProposalFilters` and `LogQuery` were modified to extend `Record<...>` only to satisfy utility typing.
   - This couples domain-facing types to a generic utility implementation detail.

3. **Behavior edge case in query utility**
   - `buildQueryString()` included blank string values, which can generate noisy query params (for example `?status=`).

4. **Validation coverage gap**
   - New shared frontend utilities did not have direct unit tests.

## Plan

1. Strengthen backend refactor by centralizing reusable HTTP status mapping.
2. Standardize backend error payload contract with a shared DTO.
3. Decouple frontend API filter types from generic query utility typing.
4. Harden frontend query utility behavior for empty-string inputs.
5. Add focused utility tests to reduce regression risk.

## Actions Applied in This Pass

### Backend actions

- Added `ApiErrorResponse` DTO (`backend/src/Taskdeck.Api/Contracts/ApiErrorResponse.cs`).
- Extended `ResultExtensions` with `ToHttpStatusCode()` and reused it from `ToErrorActionResult()`.
- Updated stream error handling in `ChatController` to use shared status mapping.
- Replaced remaining anonymous error payloads in updated controllers with `ApiErrorResponse`.
- Replaced string literal validation code in `LlmQueueController` with `ErrorCodes.ValidationError`.
- Added/expanded backend tests in `ResultExtensionsTests` to cover `ToHttpStatusCode()` and typed error payload assertions.

### Frontend actions

- Improved `buildQueryString()` to:
  - ignore null/undefined,
  - ignore blank strings,
  - serialize only string/number/boolean values.
- Removed `Record<...>` inheritance from:
  - `frontend/taskdeck-web/src/types/automation.ts` (`ProposalFilters`),
  - `frontend/taskdeck-web/src/types/ops.ts` (`LogQuery`).
- Added utility tests:
  - `frontend/taskdeck-web/src/tests/utils/queryBuilder.spec.ts`
  - `frontend/taskdeck-web/src/tests/utils/errorMessage.spec.ts`

## Validation Notes

- `dotnet test backend/Taskdeck.sln` passed (all test projects green).
- Frontend build command `npm run build` currently fails in this environment because `vue-tsc` is unavailable locally (dependency/tooling availability issue, not a code error signal).

## Suggested Follow-Ups

1. Add a small backend integration test for `GET /api/llm/chat/sessions/{id}/stream` failure status mapping to lock in `ToHttpStatusCode()` reuse.
2. Clean up unrelated lockfile churn in `frontend/taskdeck-web/package-lock.json` if it was not intentionally required by dependency updates.
3. Consider a shared backend helper for custom non-`Result` error branches (manual guard clauses) if more are added.
