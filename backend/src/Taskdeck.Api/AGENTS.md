# Taskdeck.Api – SECURITY RETROFIT PLAYBOOK (Horizon A)

Goal: eliminate caller-supplied actor identity and make auth behavior consistent + test-backed.

## Retrofit sequence (do not improvise)
1) Choose ONE controller family (Boards/Columns/Cards/Labels first).
2) Implement auth + claims retrofit + tests for that family.
3) Only then move to the next family.

## Required endpoint posture
- Endpoint must be either:
  A) [AllowAnonymous] (explicit) OR
  B) [Authorize] + claims-derived actor + authz checks
- Never accept acting userId in query/body where claims apply.

## Implementation steps (per controller)
1) Add [Authorize] at controller or action level (unless explicitly anonymous).
2) Derive actor from claims (single helper). Do NOT use request userId.
3) Normalize authz:
    - board-scoped actions must verify board access
    - mutations must verify permission level (write/admin/etc)
4) Standardize errors:
    - 401: missing/invalid token
    - 403: authenticated but forbidden
    - 404: resource missing OR intentionally hidden across users (pick policy consistently)

## Regression tests (mandatory for each retrofitted controller)
Add API integration tests that assert:
- 401 when no token
- 403 when token valid but no access
- cross-user isolation (user A cannot read/mutate user B resources)
- happy path still works

Minimum test matrix per endpoint:
- unauthenticated
- authenticated/no access
- authenticated/wrong board
- authenticated/has access

## Definition of Done (per controller family)
- No endpoint in that family depends on caller-supplied actor identity.
- Auth behavior is consistent and covered by integration tests.
- dotnet test backend/Taskdeck.sln -c Release passes.

## Scope reminders (project reality)
Legacy controllers to converge (until fully covered):
boards/columns/cards/labels/export/audit/queue/board-access/users
(automation/chat/ops/logs already have [Authorize]; don’t regress them)

## Output requirement
When finishing a slice, list:
- files changed
- tests added
- dotnet test result
- docs updated (STATUS + MASTERPLAN if behavior changed)
