---
name: taskdeck-worktree-issue-worker
description: Implement one Taskdeck GitHub issue in an isolated worktree with focused ownership, small commits, tests, PR creation, and handoff. Use when a coordinator assigns Codex a single issue, asks for issue-to-PR execution, or launches a worker/subagent for backend, frontend, docs, or testing implementation.
---

# Taskdeck Worktree Issue Worker

Deliver one issue cleanly from an isolated worktree.

## First actions

1. If the coordinator used `New-CodexIssueWorktree.ps1` from the main checkout (linked-source invocation is rejected), run its complete printed PowerShell handoff block unchanged. Its first command invokes the exact absolute target `worktree_guard.ps1` with pinned Git; only after that guard succeeds does the bounded exact-target `Initialize-CodexIssueWorktree.ps1` verify the helper-created detached base and perform `switch -c`. A late switch collision removes the unused detached worktree only when its tracked, untracked, and ignored inventory is empty; otherwise it is preserved for inspection. The helper byte-checks target guard/initializer files against reviewed raw blobs before emitting this block, but same-user replacement after emission remains outside this boundary. Use the Codex-native worker route in [the runtime handoff](../../../docs/tooling/CODEX_AUTONOMY_RUNBOOK.md#choose-the-worker-runtime): bind the exact target cwd, detached base, owned paths and complete printed block in the task packet. Codex permissions remain effective; the helper's Claude-specific launch rules are not a Codex authorization mechanism. If this client cannot execute the printed block under its current permissions, preserve the created worktree and report the exact unsupported step. From Bash, launch a reviewed absolute PowerShell application in the target worktree for the whole block; never resolve a bare `powershell` command through PATH.
2. Otherwise run `powershell -File scripts/worktree_guard.ps1`. Do not substitute a PATH-first batch shim.
3. Read applicable `AGENTS.md` instructions, including the nearest file for the owned area.
4. Read the relevant headings or ranges in `docs/STATUS.md`; use the active index rather than loading the complete historical record.
5. Read the assigned issue body and acceptance criteria.
6. Read the relevant Taskdeck skill:
   - backend/API/auth/persistence: `taskdeck-backend-slice`
   - frontend/workspace/UX: `taskdeck-frontend-workspace-slice`
   - capture/inbox/review/proposals: `taskdeck-capture-review-loop`
   - demo/evidence: `taskdeck-demo-regression`
7. Confirm owned files or module boundaries before editing.

Do not use absolute paths from the main checkout. Derive paths with the helper-printed native Git executable and `rev-parse --show-toplevel`; a child PowerShell guard cannot export `$env:WT_PROJECT_DIR` back to its parent shell.

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
git commit --no-gpg-sign -m "<present-tense message>"
```

DCO trailers are optional while enforcement is paused; do not rewrite history to add one.
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

After opening the ready PR, return its exact head/base identity and verification evidence to the
coordinator. Only the coordinator enters or re-enters `review-and-ship` through
`taskdeck-pr-review-loop`; resume this worker only for fixes the coordinator returns from that
pipeline.

## Handoff

Report:

- branch and PR URL
- files changed
- tests added
- commands run and results
- docs changed
- exact PR head/base identity and verification evidence
- any pipeline-directed fix evidence from a resumed worker turn
- any deferred follow-up issue numbers or blocked seeding notes
