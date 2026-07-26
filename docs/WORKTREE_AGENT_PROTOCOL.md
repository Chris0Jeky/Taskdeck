# Worktree Agent Protocol

Last Updated: 2026-07-26

Use this protocol when running multiple agents or Codex sessions in parallel. The failure mode this prevents is simple: workers accidentally operate in the main checkout, switch branches under each other, or commit to the wrong branch.

## Supported Worktree Roots

Accepted agent worktree roots:

- Codex: `.worktrees/codex-<issue>-<slug>/`
- Claude Code: `.claude/worktrees/agent-<id>/`

Other roots are allowed only when the coordinator explicitly names them and updates guard configuration.
The Codex helper intentionally accepts only the repository's `.worktrees/` root; a different
approved root requires a separately reviewed creation path after the guard configuration changes.

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
$coordinatorBranchBaseline = git branch --show-current
$coordinatorStatusBaseline = @(git status --short --untracked-files=all)
powershell -File scripts/git/New-CodexIssueWorktree.ps1 -IssueNumber 123 -Slug short-slug
```

Keep the branch and status baselines for post-run comparison. They may include intentional tracked
or untracked coordinator changes; the helper preserves them instead of requiring a clean checkout.

The helper refreshes an explicit remote branch base (default `origin/main`) before resolving it,
peels annotated tags to one commit, preserves any tracked or untracked source-checkout changes,
and creates only a detached worktree:

- worktree: `.worktrees/codex-123-short-slug`
- detached base: `origin/main`
- planned branch: `issue-123/short-slug` (printed, but not created yet)

If you need a custom branch:

```powershell
powershell -File scripts/git/New-CodexIssueWorktree.ps1 `
  -IssueNumber 123 `
  -Slug short-slug `
  -BranchName "feature/custom-branch"
```

Run the helper's entire printed PowerShell block unchanged from the new worktree. It invokes one
stable, reviewed relative initializer wrapper. The wrapper runs the guard as its first internal
action with the helper-selected argv-safe Git executable, verifies the exact helper-created
worktree and detached base, and only then creates and switches to the planned branch:

```powershell
powershell -NoLogo -NoProfile -NonInteractive -File scripts/git/Initialize-CodexIssueWorktree.ps1 -GitExecutable '<native Git executable printed by the helper>' -BranchName 'issue-123/short-slug' -ExpectedWorktree '<exact worktree printed by the helper>' -ExpectedHead '<detached base OID printed by the helper>'
$handoffSucceeded = $?; $handoffExitCode = $LASTEXITCODE
if (-not $handoffSucceeded -or $handoffExitCode -ne 0) { if ($null -ne $handoffExitCode -and $handoffExitCode -ne 0) { exit $handoffExitCode }; exit 1 }
```

The helper rejects alternate, traversing, rooted, junction-backed, and symlink-backed worktree
roots. Its detached-first handoff is PowerShell-only. From a Bash worker, start PowerShell in the
printed worktree and run the complete block there; do not translate only the branch command.

## Permission Posture In Worktrees

Worktree checkouts do NOT contain the gitignored `.claude/settings.local.json`, so worker
sessions use the committed permissions plus any explicit launch rules. The committed
`acceptEdits` default auto-approves in-scope edits and common filesystem operations, but
acceptEdits does not approve arbitrary Git or PowerShell commands and is not sufficient by itself
for the detached-first handoff. `.claude/settings.json` narrowly allowlists the stable relative
`Initialize-CodexIssueWorktree.ps1` wrapper for both shell-tool shapes.

**Headless workers.** Current Claude Code documents that ordinary non-interactive `-p` runs
disable trust verification, while `--worktree` remains an exception that requires accepted trust.
Skipping that trust check does not approve unmatched commands. Launch a headless worker with the
stable initializer rule plus the task's other reviewed command rules, for example:

```text
--allowedTools "PowerShell(powershell -NoLogo -NoProfile -NonInteractive -File scripts/git/Initialize-CodexIssueWorktree.ps1:*)" <other narrow task rules>
```

When the supplied allowlist covers the whole task, add `--permission-mode dontAsk` so unmatched
tools fail closed instead of prompting in a session that cannot answer. Use the `Bash(...)` form
of the same stable relative prefix when that is the enabled shell tool. Do not present
`acceptEdits`, disabled trust verification, or `--dangerously-skip-permissions` as authorization
for the wrapper; broader bypass requires a separately approved disposable isolation boundary.
See Claude Code's current [permission-mode contract](https://code.claude.com/docs/en/permissions)
and [trust-verification exception for `-p`/`--worktree`](https://code.claude.com/docs/en/security).

## First Worker Command

PowerShell:

```powershell
powershell -File scripts/worktree_guard.ps1
```

When the coordinator used `New-CodexIssueWorktree.ps1`, the first worktree command is the helper's
complete printed `Initialize-CodexIssueWorktree.ps1` handoff instead of this generic command. The
initializer runs the guard first and stops before branch creation on any exact-worktree, detached
base, guard, or switch failure.

Bash:

```bash
source scripts/worktree_guard.sh
```

The Bash guard remains valid for an already-created worktree that does not need the helper's
detached-first handoff. For a helper-created worktree, launch PowerShell and run the complete
printed PowerShell block so its pinned-Git and fail-fast guarantees remain intact.

The guard sets these values in the process where it runs:

- PowerShell: `$env:WT_REPO_ROOT`, `$env:WT_PROJECT_DIR` (a `powershell -File` child prints them,
  but cannot export them back to its parent shell)
- Bash: `$WT_REPO_ROOT`, `$WT_PROJECT_DIR`

Workers must derive absolute paths from a value available in their current shell or from the
helper-printed native Git executable with `rev-parse --show-toplevel`; do not assume a child
PowerShell process changed the parent environment or location.

## Worker Prompt Template

```text
You are implementing Taskdeck issue #NNN in an isolated worktree.

First PowerShell commands (copy the complete block printed by New-CodexIssueWorktree.ps1):
<relative scripts/git/Initialize-CodexIssueWorktree.ps1 command with pinned Git, branch, exact worktree, and detached base>
<capture and fail-fast gate for initializer status and exit code>

Use AGENTS.md and the relevant .codex skill(s). Own only: <files/modules>.
Do not reference or edit the main checkout. Do not revert edits made by others.
Keep scope to acceptance criteria. Make small signed-off commits with git commit -s --no-gpg-sign.
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

- Commit with `git commit -s --no-gpg-sign` in automated/background terminals.
- Do not use `--no-verify`.
- Do not use force push unless the user explicitly asks.
- Prefer merge over rebase when reconciliation stalls; replace `BRANCH_NAME` with the source ref in `git merge --signoff --no-gpg-sign BRANCH_NAME`. After a conflict, resolve and stage the files, then use `git commit -s --no-gpg-sign --no-edit` instead of `git merge --continue`.
- Check for active Git processes before removing `.git/index.lock`.

## Post-Run Verification

From the main checkout:

```powershell
git branch --show-current
git status --short
git worktree list
```

Expected:

- main checkout branch exactly matches `$coordinatorBranchBaseline`
- `git status --short --untracked-files=all` exactly matches `$coordinatorStatusBaseline`, including
  every preserved pre-existing user change and no newly introduced coordinator-checkout change
- completed worktrees can be removed only after their branches/PRs are safely pushed

Remove a completed worktree only when the coordinator is sure it is no longer needed:

```powershell
git worktree remove .worktrees/codex-123-short-slug
```

## Why This Exists

Observed production issue: parallel agents resolved paths back to the main checkout, raced on
branch switches, and landed commits on wrong branches. The generic guard combines approved path
markers with an actual linked-worktree check. The detached-first initializer adds the stronger
helper-specific binding to the exact canonical worktree and detached base before branch creation.
