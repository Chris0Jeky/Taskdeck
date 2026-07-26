# Worktree Agent Protocol

Last Updated: 2026-07-26

Use this protocol when running multiple agents or Codex sessions in parallel. The failure mode this prevents is simple: workers accidentally operate in the main checkout, switch branches under each other, or commit to the wrong branch.

## Supported Worktree Roots

Accepted agent worktree roots:

- Codex: `.worktrees/codex-<issue>-<slug>/`
- Claude Code: `.claude/worktrees/agent-<id>/`

Other roots are allowed only when the coordinator explicitly names them and updates guard configuration.
The Codex helper intentionally accepts only the exact lowercase repository `.worktrees/` root; a
different spelling or approved root requires a separately reviewed creation path after the guard
configuration changes.

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
and creates only a detached worktree. The invoking checkout's committed, clean
`scripts/worktree_guard.ps1` and `scripts/git/Initialize-CodexIssueWorktree.ps1` blobs are the
reviewed trust anchor: the helper refuses staged, unstaged, or missing source artifacts, and every
selected base must contain those exact blob identities. A commit or tag with missing or different
handoff code is rejected before the target path or Git worktree registration is created:

- worktree: `.worktrees/codex-123-short-slug`
- detached base: `origin/main`
- planned branch: `issue-123/short-slug` (printed, but not created yet)

With `-WhatIf`, the helper resolves local bases and queries explicit remote bases with
`git ls-remote` without updating local refs. Missing bases still fail; valid dry runs create no
target, branch, worktree registration, or ref update. Local dry runs also compare the exact
reviewed handoff blobs. An explicit remote dry run can only prove that the remote branch exists
without fetching it; an actual creation performs the controlled tracking-ref refresh, then compares
both blobs before any target or registration mutation.

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
& 'scripts/git/Initialize-CodexIssueWorktree.ps1' -GitExecutable '<native Git executable printed by the helper>' -BranchName 'issue-123/short-slug' -ExpectedWorktree '<exact worktree printed by the helper>' -ExpectedHead '<detached base OID printed by the helper>'
$handoffSucceeded = $?; $handoffExitCode = $LASTEXITCODE
if (-not $handoffSucceeded -or $handoffExitCode -ne 0) { if ($null -ne $handoffExitCode -and $handoffExitCode -ne 0) { exit $handoffExitCode }; exit 1 }
```

The helper rejects alternate, traversing, rooted, junction-backed, and symlink-backed worktree
roots, including case variants such as `.WORKTREES`. Its detached-first handoff is PowerShell-only
and invokes the initializer in the already-running host instead of resolving another PowerShell
command through PATH. From a Bash worker, start a reviewed absolute `powershell.exe` or `pwsh.exe`
application in the printed worktree and run the complete block there; do not use a bare
`powershell` command or translate only the branch command.

## Permission Posture In Worktrees

The linked-worktree checkout does not physically contain the gitignored
`.claude/settings.local.json`, but Claude Code stores project-local approvals at the main
repository root and applies them to sessions in its linked worktrees. Permission allow arrays also
merge across enabled settings sources. Physical absence therefore does not mean that a local or
user allow rule is ineffective. The committed `acceptEdits` default auto-approves in-scope edits
and common filesystem operations, but acceptEdits does not approve arbitrary Git or PowerShell
commands and is not sufficient by itself for the detached-first handoff. The committed
`.claude/settings.json` allowlists the stable in-process
`Initialize-CodexIssueWorktree.ps1` invocation for the PowerShell tool shape as one rule in the
effective permission set.

**Headless workers.** Current Claude Code documents that ordinary non-interactive `-p` runs
disable trust verification, while `--worktree` remains an exception that requires accepted trust.
Skipping that trust check does not approve unmatched commands. `--allowedTools` adds launch rules;
it does not replace allows from other enabled settings sources. The command-line
`--permission-mode dontAsk` overrides a file-backed `defaultMode` for that session, including
`bypassPermissions`, but it likewise does not erase merged allow rules. The supported unattended
posture for this repository is:

1. Use a Claude Code version that supports `--setting-sources` and launch with
   `--setting-sources project`. This excludes file-backed user and project-local permissions,
   including approvals stored in the main checkout for linked worktrees.
2. Review the committed `.claude/settings.json` permission and hook rules plus every explicit
   launch rule as one effective configuration. Organization-managed settings remain effective and
   are an administrator-owned trust boundary that this flag cannot remove; do not use an
   unattended worker if that boundary is not trusted for the task.
3. Add only the task-specific launch rules that the worker needs, including the stable initializer
   rule, then use `--permission-mode dontAsk` so calls that would otherwise prompt are denied. This
   mode does not revoke matching allow rules, built-in read-only Bash commands, or applicable hook
   approvals; those remain part of the reviewed trust surface.

For example:

```text
--setting-sources project --allowedTools "PowerShell(& 'scripts/git/Initialize-CodexIssueWorktree.ps1':*)" <other reviewed task rules> --permission-mode dontAsk
```

The helper handoff itself requires the PowerShell tool shape. Do not present the launch allowlist
as the sole authorization boundary, and do not present
`acceptEdits`, disabled trust verification, or `--dangerously-skip-permissions` as authorization
for the wrapper. If the installed CLI cannot exclude user and local setting sources, use an
interactive reviewed launch instead of claiming the unattended posture; broader bypass requires a
separately approved disposable isolation boundary.
See Claude Code's current [permission and settings-precedence contract](https://code.claude.com/docs/en/permissions#settings-precedence),
[`--setting-sources` CLI contract](https://code.claude.com/docs/en/cli-usage#cli-flags), and
[trust-verification exception for `-p`/`--worktree`](https://code.claude.com/docs/en/security).

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
detached-first handoff. For a helper-created worktree, launch a reviewed absolute PowerShell
application and run the complete printed block so its pinned-Git and fail-fast guarantees remain
intact; never reopen resolution through a bare `powershell` command.

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
