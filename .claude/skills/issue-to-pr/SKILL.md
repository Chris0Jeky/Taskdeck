---
name: issue-to-pr
description: Implement one GitHub issue end to end - worktree, branch, change, tests, ready PR linking the issue, coordinator handoff.
argument-hint: "<issue number>"
user-invocable: true
disable-model-invocation: true
---

# Issue to PR

Autonomous workflow: GitHub issue -> detached worktree -> implementation -> seam tests -> ready PR.

## Input

`$ARGUMENTS` is a GitHub issue number (`#350` or `350`).

## Workflow

### 1. Understand the issue

```bash
gh issue view <number> --json title,body,labels,assignees,milestone
```

Identify what must change, the acceptance criteria, the layers involved (labels: backend, frontend,
docs, testing), and linked issues or dependencies. Orient via `autodoc/AGENT_INDEX.md`; root
`CLAUDE.md` and region rules auto-load.

### 2. Create a detached worktree, guard it, then create the branch

From the coordinator checkout, without changing or cleaning its branch or working tree:

```powershell
powershell -File scripts/git/New-CodexIssueWorktree.ps1 -IssueNumber <number> -Slug <short-slug>
```

Enter the printed worktree and run the helper's complete printed PowerShell block unchanged: the exact
pinned-Git `worktree_guard.ps1` command first, then the bounded `Initialize-CodexIssueWorktree.ps1`
command, which verifies the detached base and performs `switch -c`. Headless authorization rules and
the Bash launch rule are the "Helper Handoff Contract" in `docs/WORKTREE_AGENT_PROTOCOL.md`.

Branch naming: `issue-<number>/<2-4 word slug>` (for example `issue-350/capture-validation`). Never
switch the coordinator checkout merely to obtain a clean tree.

### 3. Implement

Follow the layer skill (`taskdeck-backend-slice`, `taskdeck-frontend-workspace-slice`,
`taskdeck-capture-review-loop`). Incremental commits, one per logical change; run the seam's tests
after each significant change.

### 4. Verify

Use the proving-check table in root `CLAUDE.md` — the narrowest command per seam:

- `.cs`: one layer — `dotnet test backend/tests/Taskdeck.<Layer>.Tests/Taskdeck.<Layer>.Tests.csproj -c Release -m:1`;
  the full solution only for cross-layer changes.
- `.ts`/`.vue`: `cd frontend/taskdeck-web; npx vitest --run --maxWorkers=2 <path/to.spec.ts>; npm run typecheck`
  (bare `vitest --run` OOMs on this box).
- Flow changes: `npx playwright test tests/e2e/<file>.spec.ts --reporter=line` against a running stack.
- Docs: `node scripts/check-docs-governance.mjs`.

### 5. Push and open the PR ready for review

```bash
git push -u origin issue-<number>/<short-slug>
gh pr create --title "<concise title>" --body-file <body.md>
```

Body: `## Summary` (what and why), `Closes #<number>`, `## Changes`, `## Test plan` (what was verified,
how, and what was NOT verified). Write the body to a file first — backtick-led lines inside a heredoc
trip the floor hook.

### 6. Coordinator handoff

Return the PR URL, exact head/base identity, and verification evidence to the coordinator. Only the
coordinator enters the global `review-and-ship` pipeline (laws 2 and 11) with `taskdeck-pr-review-loop`
lenses; resume implementation only for pipeline-directed fixes. Report with the handoff shape from
`taskdeck-verification-doc-sync`.

## Guardrails

- Do not skip tests.
- Ambiguity: run `taskdeck-question-batch` — batch true blockers into one question, otherwise proceed on a
  named assumption and record it in the PR (law 6).
- Too large for one PR: propose a split and implement the first slice.
- Hand the ready PR and exact evidence to the coordinator; do not decide merge disposition here.
