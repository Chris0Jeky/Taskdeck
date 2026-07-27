---
name: taskdeck-issue-batch-orchestrator
description: Coordinate high-autonomy Taskdeck issue batches from selection through PR review and handoff. Use when the user asks Codex to take care of many issues, pick next issues, run a batch, coordinate subagents/worktrees, seed follow-ups, reconcile GitHub project status, or automate issue-to-PR execution across multiple independent workstreams.
---

# Taskdeck Issue Batch Orchestrator

Coordinate many issues without losing review quality, docs sync, or follow-up accountability.

## Read first

1. `docs/STATUS.md`
2. `AGENTS.md`
3. `docs/IMPLEMENTATION_MASTERPLAN.md`
4. `docs/ISSUE_EXECUTION_GUIDE.md`
5. `docs/GITHUB_PROJECT_AUTOMATION.md`
6. `docs/tooling/CODEX_AUTONOMY_RUNBOOK.md`
7. `docs/TESTING_GUIDE.md`

## Batch intake

Use this selection order unless the user gives explicit issues:

1. Highest-priority unblocked issues from `docs/ISSUE_EXECUTION_GUIDE.md`.
2. Issues whose dependencies are complete.
3. Issues that can be split by non-overlapping ownership.
4. Smaller slices before broad refactors.
5. Security/auth/capture-review/product-legibility work before surface breadth.

Do not exceed the repo WIP model unless the user explicitly asks for a batch override. If overriding, keep one coordinator and isolate every implementation issue in a branch/worktree.

## Coordinator responsibilities

The coordinator must own:

- issue selection and dependency checks
- worktree naming and worker prompts
- final conflict resolution
- PR body quality and linked issues
- GitHub project status/priority sync
- adversarial-review assignment
- CI/comment/conflict loops
- docs and testing-guide rehydration
- final handoff

Never delegate final synthesis.

## Work splitting

Split only when file ownership or concerns do not overlap. Good splits:

- one backend issue per worker
- one frontend issue per worker
- one docs-only issue per worker
- one reviewer worker per PR
- one CI/conflict worker per failing PR

Avoid parallel workers on the same view, store, service, migration chain, project file, or canonical doc unless the coordinator plans the merge order.

## Structured patch discipline

When editing with structured patches:

- Default each `apply_patch` (or equivalent structured patch operation) to one target file. Use a multi-file operation only for a tightly homogeneous mechanical set where every file has its own independently stable anchor.
- In prose or Unicode-bearing files, anchor hunks on nearby ASCII-stable headings or lines instead of typography-sensitive exact text.
- After a context rejection, inspect the live target before retrying, then reduce the retry to the smallest independently anchored hunk.
- Never repeat the same broad multi-file patch after it is rejected.
- Record every repeated or unresolved patch rejection in the active orchestrator/run ledger and final handoff, even when work resumes.
- Keep the record bounded and sanitized: include target(s), a failure class/error summary, a safe reproduction pointer when available, and the working invocation or workaround. Redact secrets, omit full patch payloads, and truncate oversized error context.
- Then invoke `taskdeck-failure-capture` to classify the failure and escalate it to the repository failure ledger when that skill's criteria apply.

## Worker setup

For each issue:

1. Create a worktree with `scripts/git/New-CodexIssueWorktree.ps1`.
2. In the worker prompt, forbid absolute paths to the main checkout.
3. Require first command: `powershell -File scripts/worktree_guard.ps1`.
4. Tell the worker which files or module it owns.
5. Tell the worker it is not alone in the codebase and must not revert others' edits.
6. Require small signed-off commits with `git commit -s --no-gpg-sign` when committing.
7. Require targeted tests before PR.
8. Require every file-editing worker prompt to restate the structured patch discipline above.

Use `taskdeck-worktree-issue-worker` for implementation workers.

## Review loop

Every PR needs:

1. Worker self-review after opening the PR.
2. Coordinator review of PR body, linked issue, test evidence, and docs impact.
3. Fresh adversarial review for sensitive or risky PRs:
   - auth/authz/security
   - migrations/persistence
   - capture/review/proposal execution
   - CI/workflows/project automation
   - broad frontend flow changes
   - flaky or failing tests
4. Posted review findings or explicit no-finding comment.
5. Fix commits for findings.
6. Re-review after fixes.

Use `taskdeck-pr-review-loop` for review workers.

## CI and comments

After PR creation and after every fix push:

- inspect CI status
- inspect review comments and bot comments
- classify failures by lane
- address root causes, not symptoms
- rerun only the affected checks locally when practical
- push fix commits
- comment with what changed and what was re-run

Use `taskdeck-ci-conflict-recovery` for failing CI, comments, or conflicts.

## Project priority sync

Audit project priority drift before handoff:

```powershell
powershell -File scripts/github/Sync-TaskdeckProjectPriority.ps1
```

Apply fixes when the GitHub CLI has project write scope:

```powershell
powershell -File scripts/github/Sync-TaskdeckProjectPriority.ps1 -Apply
```

If apply fails with missing `project` scope, tell the coordinator/user to run `gh auth refresh -s project` and rerun the apply command.

## Deferral rule

No silent deferrals. If a task uncovers out-of-scope work, choose one:

- fix immediately if small, on-scope, and low-risk
- add a tracked follow-up issue with acceptance criteria, priority label, and dependency notes
- document a risk in the PR and ask the user if issue seeding is blocked by missing GitHub access

## Docs rehydration

At the end of the batch, reconcile:

- `docs/STATUS.md` when shipped reality changed
- `docs/IMPLEMENTATION_MASTERPLAN.md` when sequencing or delivery record changed
- `docs/TESTING_GUIDE.md` when testing expectations or verified totals changed
- `docs/MANUAL_TEST_CHECKLIST.md` or slice runbooks when manual/headed testing is now required
- feature docs or manual docs when user-visible behavior changed

Use `taskdeck-verification-doc-sync` for final reconciliation.

## Stop conditions

Pause and ask when:

- acceptance criteria conflict with `docs/STATUS.md`
- required GitHub/project write access is unavailable
- an issue would require unreviewed auth policy changes
- multiple workers need the same files and cannot be sequenced safely
- CI indicates a systemic main-branch failure unrelated to the PRs
