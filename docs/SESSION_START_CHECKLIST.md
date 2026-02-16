# Session Start Checklist

Use this at the beginning of every coding session.

## 1) Sync and branch hygiene

1. Confirm you are on the intended branch.
2. Ensure branch is up to date with `main` (or rebase/merge per team flow).
3. Confirm working tree state before changes.

## 2) Read before coding

1. `docs/STATUS.md` (current truth)
2. `docs/IMPLEMENTATION_MASTERPLAN.md` (priority context)
3. `docs/ISSUE_EXECUTION_GUIDE.md` (dependency-aware order)
4. `docs/GITHUB_PROJECT_AUTOMATION.md` (status/workflow rules)

## 3) Choose issue correctly

1. Pick the highest-priority issue whose dependencies are complete.
2. Move project item to `Now` when work starts.
3. If blocked, move to `Blocked` and note why.

## 4) Define execution slice

1. Write a short plan (files, approach, risks, tests).
2. Keep scope aligned to issue acceptance criteria.
3. Avoid mixed refactors while delivering issue scope.

## 5) Build and verify

1. Add/update tests with behavior changes.
2. Run required checks:
   - `dotnet test backend/Taskdeck.sln -c Release`
   - `cd frontend/taskdeck-web && npm run typecheck && npm run build && npx vitest --run`
   - Playwright E2E when UI/cross-surface behavior changes

## 6) Docs and tracking

1. Update docs if reality changed:
   - `docs/STATUS.md`
   - `docs/IMPLEMENTATION_MASTERPLAN.md`
   - `docs/TESTING_GUIDE.md` or `docs/MANUAL_TEST_CHECKLIST.md` as needed
2. Link issue in PR (`Closes #<id>`).
3. Move project item to `Review` once PR is open.

## 7) Commit discipline

1. Prefer small focused commits (often one per changed file).
2. For pure move/rename batches, one grouped commit is acceptable.
