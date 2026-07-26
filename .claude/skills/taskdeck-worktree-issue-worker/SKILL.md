---
name: taskdeck-worktree-issue-worker
description: Implement one Taskdeck issue in an isolated Claude or git worktree with narrow ownership, tests, PR creation, and self-review.
---

# Taskdeck Worktree Issue Worker

Use this skill when assigned one issue or task in an isolated worktree.

## First Commands

When the coordinator used `New-CodexIssueWorktree.ps1`, run its complete printed PowerShell
handoff block unchanged. Its stable relative `scripts/git/Initialize-CodexIssueWorktree.ps1`
wrapper runs the pinned-Git guard first, verifies the exact helper-created worktree and detached
base, then performs `switch -c`; every failure exits before implementation. The printed block
invokes the wrapper in the already-running PowerShell host. From Bash, launch a reviewed absolute
PowerShell application in the worktree for that block; never resolve a bare `powershell` command
through PATH. For headless launch authorization, use the reviewed
effective-permission posture in `docs/WORKTREE_AGENT_PROTOCOL.md`; the launch allowlist is not the
sole boundary, and `acceptEdits` alone is insufficient.

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
8. Perform a self-review and fix findings before handoff.

## Stop Conditions

Stop and ask the coordinator when:

- acceptance criteria conflict with `docs/STATUS.md`
- another worker owns required files
- tests reveal broad main-branch failure unrelated to your issue
- the issue requires new auth/security policy
- GitHub/project writes are required but unavailable

