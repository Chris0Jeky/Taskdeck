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

Enter the printed worktree and run the helper's complete printed PowerShell block unchanged. It
passes the selected native Git into the guard and exits before branch creation if the guard fails.
From Bash, launch PowerShell in the worktree for the whole block; do not translate only the branch
command or substitute a PATH-first batch shim.

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

### 7. Self-review and post findings

After opening the PR, perform a deliberate reviewer-style pass:

1. Read the full PR diff with `gh pr diff <number>`
2. Check ALL existing PR comments (bot comments, CI output) with `gh pr view <number> --comments`
3. Review for issues at all severity levels (CRITICAL, HIGH, MEDIUM, LOW)
4. Post findings as a PR comment (`gh pr comment <number>`)
5. Fix ALL findings — no "non-blocking" dismissals, no skipping lower priorities
6. If a finding is real but out of scope, seed a GitHub issue to track it
7. Post a follow-up comment mapping findings to fix commits
8. Verify CI is green with `gh pr checks <number>`

### 8. Report back

Provide the PR URL and the handoff summary from `taskdeck-verification-doc-sync`.

## Guardrails

- do not merge the PR -- leave it for human review
- do not skip tests
- if the issue is ambiguous, ask the user before implementing
- if the issue is too large for one PR, propose a split and implement the first slice
- always self-review and post findings on the PR before reporting done
