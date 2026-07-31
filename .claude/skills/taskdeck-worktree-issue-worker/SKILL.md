---
name: taskdeck-worktree-issue-worker
description: Implement one Taskdeck issue in an isolated Claude or git worktree with narrow ownership, tests, PR creation, and canonical review handoff.
---

# Taskdeck Worktree Issue Worker

Use this skill when assigned one issue or task in an isolated worktree.

## First Commands

When the coordinator used `New-CodexIssueWorktree.ps1` from the main checkout (linked-source
invocation is rejected), run its complete printed PowerShell
handoff block unchanged. Its first command invokes the exact absolute target `worktree_guard.ps1`
with pinned Git; the bounded `Initialize-CodexIssueWorktree.ps1` follows only on guard success,
verifies the exact helper-created worktree and detached base, then performs `switch -c`. A late
switch collision removes the unused detached worktree before failing. The printed block
invokes the wrapper in the already-running PowerShell host. Creation-time blob checks do not
authenticate a same-user replacement after handoff emission, though the helper checks target
guard/initializer bytes against reviewed raw blobs before emitting commands. From Bash, launch a reviewed absolute
PowerShell application in the worktree for that block; never resolve a bare `powershell` command
through PATH. Pass the helper's ordered guard-plus-initializer rule array as two `--allowedTools`
argv values. For a headless worker, start `claude -p` in the exact helper-created target without
`--worktree`, accept project trust interactively before relying on settings or hooks, and note that
the project grants no PowerShell commands. Enable the PowerShell tool only through the trusted host
environment for the two exact handoff rules and restore the prior host value when the launch returns.
The tool is unsandboxed on Windows and Taskdeck's command hooks are Bash-only, so keep later commands on Git
Bash. For an untrusted launch, supply every allow through CLI argv. Unsupported clients require an
interactive coordinator launch. For headless launch authorization, use the reviewed
effective-permission posture and both exact additive full-command task rules printed by the helper,
including every applicable pinned argument and no wildcard; never use a generic relative handoff rule. The launch allowlist is not the sole boundary, and `acceptEdits` alone
is insufficient.

For an already-created worktree that does not need the helper handoff, validate isolation with:

```powershell
powershell -File scripts/worktree_guard.ps1
```

Do not substitute a PATH-first batch shim.

Only then orient via `autodoc/AGENT_INDEX.md` (the seam map) — find your area in its seams table and jump to the entry point. Read only the relevant section of `docs/STATUS.md` (source of truth; ~1.3k lines — never read end-to-end); don't bulk-read `docs/IMPLEMENTATION_MASTERPLAN.md`. Root `CLAUDE.md`/`AGENTS.md` auto-load — don't re-read them. Then read the issue's body + acceptance criteria and the domain skill matching the files you own.

## Ownership

Own only the files/modules assigned by the coordinator. You are not alone in the codebase:

- do not revert edits made by others
- do not broaden scope without coordinator approval
- keep commits small and present tense
- do not use `--no-verify`
- require a `Signed-off-by:` trailer on every new commit; use `git commit -s --no-gpg-sign` in automated/background terminals

## Implementation Loop

1. Re-state the issue goal and acceptance criteria.
2. Inspect only relevant code and tests.
3. Implement the smallest complete slice.
4. Add or update tests for behavior changes.
5. Run targeted checks first.
6. Update docs only if current reality, roadmap, testing expectations, or operator workflow changed.
7. Open a PR with summary, linked issue, tests, docs impact, and risks.
8. Return the ready PR, exact head/base identity, and verification evidence to the coordinator.
   Only the coordinator enters or re-enters `review-and-ship`; resume this worker only for fixes
   that the coordinator returns from that pipeline.

## Stop Conditions

Stop and ask the coordinator when:

- acceptance criteria conflict with `docs/STATUS.md`
- another worker owns required files
- tests reveal broad main-branch failure unrelated to your issue
- the issue requires new auth/security policy
- GitHub/project writes are required but unavailable

