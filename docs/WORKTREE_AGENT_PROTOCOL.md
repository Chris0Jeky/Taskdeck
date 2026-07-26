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
powershell -File scripts/git/New-CodexIssueWorktree.ps1 -IssueNumber 123 -Slug short-slug
```

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

Run the helper's entire printed PowerShell block unchanged from the new worktree. The guard pins
the same argv-safe native Git executable selected by the helper, and each command is fail-fast:

```powershell
& '<PowerShell host printed by the helper>' -NoLogo -NoProfile -NonInteractive -File scripts/worktree_guard.ps1 -GitExecutable '<native Git executable printed by the helper>'
$guardSucceeded = $?; $guardExitCode = $LASTEXITCODE
if (-not $guardSucceeded -or $guardExitCode -ne 0) { if ($null -ne $guardExitCode -and $guardExitCode -ne 0) { exit $guardExitCode }; exit 1 }
& '<native Git executable printed by the helper>' switch -c 'issue-123/short-slug'
$switchSucceeded = $?; $switchExitCode = $LASTEXITCODE
if (-not $switchSucceeded -or $switchExitCode -ne 0) { if ($null -ne $switchExitCode -and $switchExitCode -ne 0) { exit $switchExitCode }; exit 1 }
```

The helper rejects alternate, traversing, rooted, junction-backed, and symlink-backed worktree
roots. Its detached-first handoff is PowerShell-only. From a Bash worker, start PowerShell in the
printed worktree and run the complete block there; do not translate only the branch command.

## Permission Posture In Worktrees

Worktree checkouts do NOT contain the gitignored `.claude/settings.local.json`, so worker
sessions run under the committed default (`acceptEdits`) plus the committed allowlist — not
`bypassPermissions`. The guard commands below are allowlisted in `.claude/settings.json`; if a
worker needs broader trust, launch it with an explicit `--permission-mode` or seed a
`settings.local.json` into the worktree at creation time.

**Workspace-trust caveat (headless workers).** A committed allowlist is necessary but not
sufficient. Claude Code applies a project's `permissions.allow` rules only after the workspace
is *trusted*, and trust is keyed on the git-repository root — a freshly created worktree is a
new, untrusted root. In non-interactive mode (`claude -p` / `--print`) the trust dialog never
appears and untrusted project allow rules **stay ignored**, so even the allowlisted guard
command can prompt or block. A headless worktree worker must therefore be launched with one of:
`--allowedTools "Bash(powershell -File scripts/worktree_guard.ps1:*) ..."`, an explicit
`--permission-mode acceptEdits`, or `--dangerously-skip-permissions` in a disposable
environment. Interactive workers accept the one-time trust prompt instead. (Refs: Claude Code
permissions — *project allow rules and workspace trust*; security — *trust verification*.)

## First Worker Command

PowerShell:

```powershell
powershell -File scripts/worktree_guard.ps1
```

When the coordinator used `New-CodexIssueWorktree.ps1`, use the helper's complete printed
PowerShell block instead of this generic command. It pins native Git into the guard and stops on
both guard and branch-creation failures.

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
<pinned-native-Git guard command>
<capture and fail-fast gate for guard status and exit code>
<pinned-native-Git switch command>
<capture and fail-fast gate for switch status and exit code>

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

- main checkout remains on the intended coordinator branch
- unrelated changes are not present in the main checkout
- completed worktrees can be removed only after their branches/PRs are safely pushed

Remove a completed worktree only when the coordinator is sure it is no longer needed:

```powershell
git worktree remove .worktrees/codex-123-short-slug
```

## Why This Exists

Observed production issue: parallel agents resolved paths back to the main checkout, raced on branch switches, and landed commits on wrong branches. Path-marker guards are intentionally simple because they survive Windows/MSYS path-format differences better than comparing low-level Git directory paths.
