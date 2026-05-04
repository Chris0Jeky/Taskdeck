---
name: taskdeck-worktree-issue-worker
description: Implement one Taskdeck issue in an isolated Claude or git worktree with narrow ownership, tests, PR creation, and self-review.
---

# Taskdeck Worktree Issue Worker

Use this skill when assigned one issue or task in an isolated worktree.

## First Commands

Read:

1. `CLAUDE.md`
2. `AGENTS.md`
3. `docs/STATUS.md`
4. the issue body and acceptance criteria
5. the domain skill matching the files you own

Validate worktree isolation with the command required by `docs/WORKTREE_AGENT_PROTOCOL.md` or:

```powershell
powershell -File scripts/worktree_guard.ps1
```

## Ownership

Own only the files/modules assigned by the coordinator. You are not alone in the codebase:

- do not revert edits made by others
- do not broaden scope without coordinator approval
- keep commits small and present tense
- do not use `--no-verify`
- use `--no-gpg-sign` only when the environment requires it

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

