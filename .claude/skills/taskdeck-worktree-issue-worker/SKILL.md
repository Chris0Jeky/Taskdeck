---
name: taskdeck-worktree-issue-worker
description: Implement one Taskdeck issue in an isolated Claude or git worktree with narrow ownership, tests, PR creation, and canonical review handoff.
---

# Taskdeck Worktree Issue Worker

Use this skill when assigned one issue or task in an isolated worktree.

## First Commands

When the coordinator used `scripts/git/New-CodexIssueWorktree.ps1` (from the main checkout; linked-source
invocation is rejected), run its complete printed PowerShell handoff block unchanged: the exact absolute
target `worktree_guard.ps1` command with pinned Git, its fail-fast gate, then the bounded
`Initialize-CodexIssueWorktree.ps1` command, which verifies the exact helper-created worktree and detached
base before `switch -c`. Everything else about that block — late-collision handling, the Bash launch
rule (a reviewed absolute PowerShell application, never bare `powershell` through PATH), headless
`--allowedTools` authorization with both exact printed rules, the PowerShell-tool posture, and why
`acceptEdits` alone is not authorization — is the "Helper Handoff Contract" in
`docs/WORKTREE_AGENT_PROTOCOL.md`. Follow it; do not paraphrase it.

For an already-created worktree that does not need the helper handoff, validate isolation with:

```powershell
powershell -File scripts/worktree_guard.ps1
```

Do not substitute a PATH-first batch shim.

Only then orient via `autodoc/AGENT_INDEX.md` (the seam map); root `CLAUDE.md` and region rules
auto-load. Read the issue's body and acceptance criteria and the domain skill matching the files you own.

## Ownership

Own only the files/modules assigned by the coordinator. You are not alone in the codebase:

- do not revert edits made by others
- do not broaden scope without coordinator approval
- keep commits small and present tense
- do not use `--no-verify`
- DCO trailers are optional while enforcement is paused; use `git commit --no-gpg-sign` in
  automated/background terminals and do not repair history to add a trailer

## Implementation Loop

1. Re-state the issue goal and acceptance criteria.
2. Inspect only relevant code and tests.
3. Implement the smallest complete slice.
4. Add or update tests for behavior changes.
5. Run the seam's proving check first (root `CLAUDE.md` table); broaden only as blast radius requires.
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
