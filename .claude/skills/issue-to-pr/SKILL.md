---
name: issue-to-pr
description: End-to-end issue implementation. Takes a GitHub issue number, creates a branch, implements the change, runs tests, opens a PR linking the issue, and reports back for review.
user-invocable: true
---

# Issue to PR

Autonomous workflow: GitHub issue -> branch -> implementation -> tests -> PR.

## Input

The user provides a GitHub issue number (e.g., `#350` or just `350`).

## Workflow

### 1. Understand the issue

```bash
gh issue view <number> --json title,body,labels,assignees,milestone
```

Read the issue thoroughly. Identify:
- what needs to change
- acceptance criteria
- labels (which layers are involved: backend, frontend, docs, testing)
- linked issues or dependencies

### 2. Orient to current state

Use the `taskdeck-repo-onramp` skill mentally:
- read `docs/STATUS.md` for current constraints
- identify affected files and layers

### 3. Create a detached worktree, guard it, then create the branch

From the coordinator checkout, refresh and create from the explicit remote base without changing
or cleaning the coordinator's branch or working tree:

```powershell
powershell -File scripts/git/New-CodexIssueWorktree.ps1 `
  -IssueNumber <number> `
  -Slug <short-slug>
```

Enter the printed worktree and run the helper's complete printed PowerShell block unchanged. Its
first command invokes the exact absolute target `worktree_guard.ps1` with selected native Git; the
bounded exact-target `Initialize-CodexIssueWorktree.ps1` follows on success, verifies the detached
base, then performs `switch -c`. A late collision removes the unused detached worktree before
failing. The helper validates target guard/initializer bytes against reviewed raw blobs before
emission, but same-user replacement after emission remains a residual. Use both exact additive full-command permission
rules printed by the helper (guard plus initializer), including every applicable pinned argument and no wildcard, when launch authorization requires them; never substitute a generic relative rule. From Bash, launch a reviewed absolute PowerShell
application in the worktree for the whole block; do not resolve bare `powershell`, translate only
the branch command, or substitute a PATH-first batch shim.

Branch naming remains `issue-<number>/<2-4 word slug>` (for example,
`issue-350/capture-validation`). Continue the implementation there; never switch the coordinator
checkout merely to obtain a clean tree.

### 4. Implement

- follow the relevant skill for the layer being changed (backend-slice, frontend-workspace-slice, capture-review-loop)
- make incremental commits, one per logical change
- run tests after each significant change

### 5. Verify

Run the appropriate checks based on what changed:

- `.cs` files: `dotnet test backend/Taskdeck.sln -c Release -m:1`
- `.ts`/`.vue` files: `cd frontend/taskdeck-web && npx vitest --run --reporter=verbose && npm run typecheck`
- both: run both
- E2E-relevant: `npx playwright test` with targeted spec

### 6. Push and open PR

```bash
git push -u origin issue-<number>/<short-slug>
```

Open PR with:

```bash
gh pr create --title "<concise title>" --body "$(cat <<'EOF'
## Summary
<what changed and why>

Closes #<number>

## Changes
<bullet list of key changes>

## Test plan
<what was verified and how>
EOF
)"
```

### 7. Coordinator handoff

Open the PR ready-for-review, capture its exact head/base identity and verification evidence, then
return it to the coordinator. Only the coordinator enters or re-enters the global
`review-and-ship` pipeline (global laws 2 and 11) with the Taskdeck-specific
`taskdeck-pr-review-loop` lenses. Resume implementation only for pipeline-directed fixes returned
by the coordinator.

### 8. Report back

Provide the PR URL and the handoff summary from `taskdeck-verification-doc-sync`.

## Guardrails

- do not skip tests
- if the issue is ambiguous, ask the user before implementing
- if the issue is too large for one PR, propose a split and implement the first slice
- hand the ready PR and exact evidence to the coordinator; do not enter the review pipeline or
  decide merge disposition from this implementation skill
