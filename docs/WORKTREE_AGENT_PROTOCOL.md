# Worktree Agent Protocol

Last Updated: 2026-07-31

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
Run the helper only from the repository's main checkout. It compares the absolute per-worktree Git
directory with the common Git directory and rejects linked source worktrees before fetch, ref, target
path, or worktree-registration mutation.

The helper refreshes an explicit remote branch base (default `origin/main`) before resolving it;
`origin/HEAD` is resolved from the remote's current symbolic default before that refresh, so a stale
local `refs/remotes/origin/HEAD` cannot select an old base. It
peels annotated tags to one commit, preserves any tracked or untracked source-checkout changes,
and creates only a detached worktree. The helper binds its own exact repository path, then compares
the raw index blob identities and actual bytes of the helper, guard, and initializer with their
committed `HEAD` blobs before its intended mutating operations. The byte comparison does not invoke
Git content filters, accepts only exact bytes or deterministic LF-to-CRLF checkout expansion, and
therefore still detects changes hidden by index flags such as `skip-worktree`. Missing, staged, or
different working artifacts fail closed. This is a dirty-artifact hygiene check, not external
authentication of the helper: PowerShell must begin executing the helper before it can perform its
own path and byte checks. A same-user process could replace the helper before or during those checks;
closing that bootstrap boundary would require an independently reviewed, hash-pinned launcher.
Every helper-owned Git process disables Git/Git Credential Manager prompts and has a 45-second deadline by default, including
the raw-blob reads used by these hygiene checks. A timeout terminates and boundedly reaps the launched
process tree before the helper returns a failure; a cleanup or output-drain timeout is reported as a
separate failure rather than as a successful reap. `-GitCommandTimeoutSeconds` can adjust that bound
for a controlled test or measured exceptional environment. On Windows, cleanup verifies the captured Git
root PID and start time before using `taskkill /T`; this prevents ordinary stale-PID targeting but
does not make the shared same-user process namespace an authentication boundary. The environment
disables Git and Git Credential Manager prompts; an independently launched SSH transport can still
use its own console prompt, but the process deadline bounds it. If `git worktree add` times out after
registering and populating the reserved target, cleanup revalidates the exact detached identity and
registration, inventories tracked/untracked/ignored content, and rejects pre-existing
`assume-unchanged` or `skip-worktree` index entries that could hide modified bytes. It unlocks only
that registration when safe and uses plain removal. Unverified, dirty, index-hidden, or incomplete
partial state is preserved with an explicit cleanup failure rather than force-removed.
Every selected base must contain the exact reviewed `scripts/worktree_guard.ps1` and
`scripts/git/Initialize-CodexIssueWorktree.ps1` blob identities. A commit or tag with missing or
different handoff code is rejected before the target path or Git worktree registration is created.
The final target directory is atomically reserved under the revalidated approved root; after Git
creates the worktree, both target handoff files are byte-compared with those reviewed raw blobs
before any worker command is emitted:

- worktree: `.worktrees/codex-123-short-slug`
- detached base: `origin/main`
- planned branch: `issue-123/short-slug` (printed, but not created yet)

With `-WhatIf`, the helper resolves local bases and queries explicit remote bases with
`git ls-remote` without updating local refs. Missing bases still fail; valid dry runs create no
target, branch, worktree registration, or ref update. Local dry runs also compare the exact
reviewed handoff blobs. An explicit remote dry run can only prove that the remote branch exists
without fetching it; an actual creation performs the controlled tracking-ref refresh, then compares
both blobs before any target or registration mutation. An occupied final target fails before either
the normal or `-WhatIf` path can refresh a ref or enter `ShouldProcess`; the later atomic reservation
still closes creation races.

If you need a custom branch:

```powershell
powershell -File scripts/git/New-CodexIssueWorktree.ps1 `
  -IssueNumber 123 `
  -Slug short-slug `
  -BranchName "feature/custom-branch"
```

Custom branches must also be representable as Windows ref paths. The helper rejects `<`, `>`, `:`,
`"`, `\`, `|`, `?`, `*`, trailing periods, Windows-reserved device components such as `CON` or
`LPT1`, directory components longer than 255 UTF-16 code units, and final components longer than 250
so Git's `.lock` suffix still fits. It also rejects an existing local branch at any ancestor or
descendant of the planned name. Git's platform-neutral `check-ref-format` alone accepts names that
Windows cannot create as loose refs or lock files, and exact ref lookup misses namespace collisions.

Run the helper's entire printed PowerShell block unchanged from the new worktree. It invokes the
target guard itself as the first worktree command with the helper-selected argv-safe Git executable,
then invokes the bounded initializer at its exact absolute path. The initializer rechecks the exact
helper-created worktree and detached base, then creates and switches to the planned branch. If a
late branch collision makes that switch fail, the initializer inventories tracked, untracked, and
ignored content before cleanup. It schedules a plain removal only when that inventory is empty;
otherwise it preserves the worktree path and registration for inspection. The delayed remover
revalidates the exact top-level, common Git directory, detached base, and empty inventory immediately
before removal, including repositories whose common Git directory is stored separately from the
main checkout. If target-byte verification discovers dirtiness limited
to exact reviewed handoff artifacts at the expected detached commit, cleanup temporarily marks only
those verified per-worktree index entries `skip-worktree`, performs a plain (never forced) worktree
removal, and restores the flags if removal fails. Any pre-existing index-hiding flag or other
tracked, untracked, or ignored dirt is left intact and cleanup fails closed. The creation-time target byte comparison is not ongoing
execution-time authentication: a same-user process can still replace the target guard or initializer
after the helper emitted this block. An external hash-pinned launcher would be required to close that
post-emission TOCTOU boundary. If the block is accidentally run from another checkout, the target
guard rejects that current directory before the initializer can create a branch:

```powershell
& '<exact helper-created worktree>\scripts\worktree_guard.ps1' -GitExecutable '<native Git executable printed by the helper>'
$guardSucceeded = $?; $guardExitCode = $LASTEXITCODE
if (-not $guardSucceeded -or $guardExitCode -ne 0) { if ($null -ne $guardExitCode -and $guardExitCode -ne 0) { exit $guardExitCode }; exit 1 }
& '<exact helper-created worktree>\scripts\git\Initialize-CodexIssueWorktree.ps1' -GitExecutable '<native Git executable printed by the helper>' -BranchName 'issue-123/short-slug' -ExpectedWorktree '<exact worktree printed by the helper>' -ExpectedHead '<detached base OID printed by the helper>'
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
`.claude/settings.json` deliberately does not allow a generic relative initializer command because
that rule could match the wrong checkout. The helper prints two exact full-command PowerShell rules
for each task: one for the mandatory guard and one for the initializer. They include the target,
pinned Git, branch, worktree, and head arguments as applicable, with no wildcard; review and add
both rules explicitly when the launch surface requires them.

**Headless workers.** Current Claude Code documents that non-interactive `claude -p` does not show
the trust dialog. That does not make an untrusted workspace trusted: project `permissions.allow` and
`additionalDirectories` are ignored until project trust has been accepted. Skipping the dialog also
does not approve unmatched commands. `--allowedTools` adds launch rules; in a trusted workspace it
does not replace allows from other enabled settings sources. The command-line
`--permission-mode dontAsk` overrides a file-backed `defaultMode` for that session, including
`bypassPermissions`, but it likewise does not erase merged allow rules. The supported unattended
posture for this repository is:

1. Before relying on project settings, permissions, or hooks, accept this workspace's trust in a
   prior interactive coordinator session. `-p` is not a trust grant.
2. Use a Claude Code version that supports `--setting-sources` and launch with
   `--setting-sources project`. This excludes file-backed user and project-local permissions,
   including approvals stored in the main checkout for linked worktrees.
3. Review the committed `.claude/settings.json` permission and hook rules plus every explicit
   launch rule as one effective configuration. Organization-managed settings remain effective and
   are an administrator-owned trust boundary that this flag cannot remove; do not use an
   unattended worker if that boundary is not trusted for the task.
4. Add only the task-specific launch rules that the worker needs, including both exact additive
   full-command guard and initializer rules printed by the helper. Keep all other command execution on the
   repository's Git Bash surface, then use `--permission-mode dontAsk` so
   calls that would otherwise prompt are denied. This mode does not revoke matching allow rules,
   built-in read-only Bash commands, or applicable hook approvals; those remain part of the
   reviewed trust surface.

For an intentionally untrusted launch, do not rely on project-provided permissions, hooks,
additional directories, or environment. Pass every required allow rule through CLI argv and
proceed only if the task does not require ignored project configuration.

For example:

```powershell
Set-Location -LiteralPath '<exact helper-created worktree>'
$previousPowerShellToolValue = [Environment]::GetEnvironmentVariable('CLAUDE_CODE_USE_POWERSHELL_TOOL', [EnvironmentVariableTarget]::Process)
try {
    $env:CLAUDE_CODE_USE_POWERSHELL_TOOL = '1'
    $guardAllowRule = @'
PowerShell(<exact absolute guard command and pinned Git argument printed by the helper>)
'@
    $initializerAllowRule = @'
PowerShell(<exact absolute initializer command and pinned arguments printed by the helper>)
'@
    $handoffAllowRules = @($guardAllowRule, $initializerAllowRule)
    claude -p --setting-sources project --allowedTools $handoffAllowRules --permission-mode dontAsk <reviewed task prompt>
} finally {
    if ($null -eq $previousPowerShellToolValue) {
        Remove-Item Env:CLAUDE_CODE_USE_POWERSHELL_TOOL -ErrorAction SilentlyContinue
    } else {
        $env:CLAUDE_CODE_USE_POWERSHELL_TOOL = $previousPowerShellToolValue
    }
}
```

Do not add `--worktree`: Claude Code would create a second `.claude/worktrees/...` checkout instead
of staying in the helper-created target, so the exact-worktree guard would reject the handoff. The
helper handoff requires the PowerShell tool shape, but the repository deliberately does not enable
that tool or grant PowerShell commands project-wide. Enable it only in the trusted host environment
for this task-scoped launch, and restore its prior process value after `claude -p` returns.
When enabled, PowerShell becomes Claude Code's primary shell; on Windows it is not sandboxed, and
Taskdeck's command deny/failure/pre-commit hooks are currently Bash-only. Therefore the unattended
posture permits only the exact guard and initializer PowerShell rules; keep other command execution on Git Bash
until PowerShell hook parity is separately reviewed. Do not present the launch allowlist as the sole
authorization boundary, and do not present
`acceptEdits`, disabled trust verification, or `--dangerously-skip-permissions` as authorization
for the wrapper. If the installed CLI does not support the PowerShell tool enablement or cannot
exclude user and local setting sources, use an interactive coordinator launch instead of claiming
the unattended posture; broader bypass requires a separately approved disposable isolation
boundary.
See Claude Code's current [permission and settings-precedence contract](https://code.claude.com/docs/en/permissions#settings-precedence),
[`--setting-sources` CLI contract](https://code.claude.com/docs/en/cli-usage#cli-flags), and
[non-interactive trust behavior](https://code.claude.com/docs/en/permissions#project-allow-rules-and-workspace-trust),
[manual-worktree launch guidance](https://code.claude.com/docs/en/worktrees#manage-worktrees-manually), and
[PowerShell preview limitations](https://code.claude.com/docs/en/tools-reference#preview-limitations).

## First Worker Command

PowerShell:

```powershell
powershell -File scripts/worktree_guard.ps1
```

When the coordinator used `New-CodexIssueWorktree.ps1`, the complete printed handoff begins with
this guard command (using its printed pinned Git executable), followed by the bounded initializer.
The initializer stops before branch creation on any exact-worktree, detached-base, guard, or switch
failure and removes its unused detached worktree if a late branch collision occurs.

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
<absolute helper-created target worktree_guard.ps1 command with pinned Git>
<capture and fail-fast gate for guard status and exit code>
<absolute helper-created target Initialize-CodexIssueWorktree.ps1 command with pinned Git, branch, exact worktree, and detached base>
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
