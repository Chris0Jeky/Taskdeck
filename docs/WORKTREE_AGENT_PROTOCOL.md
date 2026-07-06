# Worktree Agent Protocol

Last Updated: 2026-04-25

Use this protocol when running multiple agents or Codex sessions in parallel. The failure mode this prevents is simple: workers accidentally operate in the main checkout, switch branches under each other, or commit to the wrong branch.

## Supported Worktree Roots

Accepted agent worktree roots:

- Codex: `.worktrees/codex-<issue>-<slug>/`
- Claude Code: `.claude/worktrees/agent-<id>/`

Other roots are allowed only when the coordinator explicitly names them and updates guard configuration.

## Coordinator Rules

1. Keep one coordinator in the main checkout.
2. Create one worktree per implementation issue or PR recovery task.
3. Do not pass absolute paths to the main checkout in worker prompts.
4. Assign explicit file/module ownership.
5. Tell workers they are not alone in the codebase and must not revert others' edits.
6. Only the coordinator resolves cross-worktree conflicts and updates canonical batch docs unless a docs worker owns that task.
7. After workers finish, verify the main checkout branch and cleanliness.

## Codex Worktree Creation

From the main checkout:

```powershell
powershell -File scripts/git/New-CodexIssueWorktree.ps1 -IssueNumber 123 -Slug short-slug
```

This creates:

- worktree: `.worktrees/codex-123-short-slug`
- branch: `issue-123/short-slug`

If you need a custom branch:

```powershell
powershell -File scripts/git/New-CodexIssueWorktree.ps1 `
  -IssueNumber 123 `
  -Slug short-slug `
  -BranchName "feature/custom-branch"
```

## Permission Posture In Worktrees

Worktree checkouts do NOT contain the gitignored `.claude/settings.local.json`, so worker
sessions run under the committed default (`acceptEdits`) plus the committed allowlist — not
`bypassPermissions`. The guard commands below are allowlisted in `.claude/settings.json`; if a
worker needs broader trust, launch it with an explicit `--permission-mode` or seed a
`settings.local.json` into the worktree at creation time.

## First Worker Command

PowerShell:

```powershell
powershell -File scripts/worktree_guard.ps1
```

Bash:

```bash
source scripts/worktree_guard.sh
```

The guard exports:

- PowerShell: `$env:WT_REPO_ROOT`, `$env:WT_PROJECT_DIR`
- Bash: `$WT_REPO_ROOT`, `$WT_PROJECT_DIR`

Workers must derive absolute paths from those values or from `git rev-parse --show-toplevel`.

## Worker Prompt Template

```text
You are implementing Taskdeck issue #NNN in an isolated worktree.

First command:
powershell -File scripts/worktree_guard.ps1

Use AGENTS.md and the relevant .codex skill(s). Own only: <files/modules>.
Do not reference or edit the main checkout. Do not revert edits made by others.
Keep scope to acceptance criteria. Make small commits with --no-gpg-sign.
Run targeted tests first. Open a PR with Closes #NNN and test evidence.
After opening the PR, self-review, post findings or explicit no-finding result, fix findings, and report back.
```

## Parallel Runtime Isolation

When multiple worktrees run local services:

- use unique frontend ports
- use unique API ports
- use unique SQLite/E2E database paths
- avoid sharing Playwright output directories when runs are concurrent
- keep Docker/container names unique if compose stacks run in parallel

## Git Rules

- Commit with `--no-gpg-sign` in automated/background terminals.
- Do not use `--no-verify`.
- Do not use force push unless the user explicitly asks.
- Prefer merge over rebase when reconciliation stalls.
- Check for active Git processes before removing `.git/index.lock`.

## Post-Run Verification

From the main checkout:

```powershell
git branch --show-current
git status --short
git worktree list
```

Expected:

- main checkout remains on the intended coordinator branch
- unrelated changes are not present in the main checkout
- completed worktrees can be removed only after their branches/PRs are safely pushed

Remove a completed worktree only when the coordinator is sure it is no longer needed:

```powershell
git worktree remove .worktrees/codex-123-short-slug
```

## Why This Exists

Observed production issue: parallel agents resolved paths back to the main checkout, raced on branch switches, and landed commits on wrong branches. Path-marker guards are intentionally simple because they survive Windows/MSYS path-format differences better than comparing low-level Git directory paths.
