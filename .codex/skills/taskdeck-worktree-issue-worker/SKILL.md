---
name: taskdeck-worktree-issue-worker
description: Implement one Taskdeck GitHub issue in an isolated worktree with focused ownership, small commits, tests, PR creation, and handoff. Use when a coordinator assigns Codex a single issue, asks for issue-to-PR execution, or launches a worker/subagent for backend, frontend, docs, or testing implementation.
---

# Taskdeck Worktree Issue Worker

Deliver one issue cleanly from an isolated worktree.

## First actions

1. Run `powershell -File scripts/worktree_guard.ps1`.
2. Read `docs/STATUS.md`.
3. Read `AGENTS.md`.
4. Read the assigned issue body and acceptance criteria.
5. Read the relevant Taskdeck skill:
   - backend/API/auth/persistence: `taskdeck-backend-slice`
   - frontend/workspace/UX: `taskdeck-frontend-workspace-slice`
   - capture/inbox/review/proposals: `taskdeck-capture-review-loop`
   - demo/evidence: `taskdeck-demo-regression`
6. Confirm owned files or module boundaries before editing.

Do not use absolute paths from the main checkout. Derive paths from `$env:WT_PROJECT_DIR` or `git rev-parse --show-toplevel`.

## Implementation rules

- Keep the branch scoped to one issue.
- Do not revert edits made by other workers.
- Search existing patterns with native `rg`.
- Make the smallest cohesive change that satisfies acceptance criteria.
- Add tests for behavior changes.
- Use deterministic mock provider posture unless live-provider work is explicit.
- Handle error cases explicitly.
- Update docs only when reality changed or the assigned issue asks for docs.

## Commit rules

Use small present-tense commits. Prefer file- or concern-scoped commits.

Command shape:

```powershell
git add <paths>
git commit -s --no-gpg-sign -m "<present-tense message>"
```

Do not use `--no-verify`. If hooks fail, fix the cause.

## Verification

Run targeted checks first. Broaden only when blast radius requires it.

Backend:

```powershell
dotnet test backend/Taskdeck.sln -c Release -m:1
```

Frontend:

```powershell
Set-Location frontend/taskdeck-web
npm run typecheck; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
npm run build; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
npx vitest --run
```

UI flow changes may also need targeted Playwright and screenshots.

## PR creation

Open a PR only after local verification appropriate to the change.

PR body must include:

- summary
- implementation notes
- tests added/updated
- commands run and results
- docs updated or why not
- risks/follow-ups
- `Closes #<issue>`

After opening the ready PR, enter the global `review-and-ship` pipeline through
`taskdeck-pr-review-loop`, then hand the returned pipeline state back to the coordinator.

## Handoff

Report:

- branch and PR URL
- files changed
- tests added
- commands run and results
- docs changed
- canonical review-pipeline state and any finding disposition it returned
- any deferred follow-up issue numbers or blocked seeding notes
