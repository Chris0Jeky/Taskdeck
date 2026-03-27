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

### 3. Create a branch

```bash
git checkout main
git pull origin main
git checkout -b issue-<number>/<short-slug>
```

Branch naming: `issue-<number>/<2-4 word slug>` (e.g., `issue-350/capture-validation`).

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

### 7. Report back

Provide the PR URL and the handoff summary from `taskdeck-verification-doc-sync`.

## Guardrails

- do not merge the PR — leave it for human review
- do not skip tests
- if the issue is ambiguous, ask the user before implementing
- if the issue is too large for one PR, propose a split and implement the first slice
