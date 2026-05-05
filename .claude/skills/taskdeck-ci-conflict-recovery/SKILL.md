---
name: taskdeck-ci-conflict-recovery
description: Triage and fix Taskdeck PR failures involving CI, stale branches, merge conflicts, review comments, bot comments, or blocked checks.
---

# Taskdeck CI And Conflict Recovery

Use this skill when a PR is blocked by CI, review/bot comments, conflicts, stale branches, or unclear checks.

## Read First

1. `docs/STATUS.md`
2. `CLAUDE.md`
3. `AGENTS.md`
4. PR body, checks, comments, review threads, and linked issue
5. `docs/TESTING_GUIDE.md`
6. `docs/tooling/CODEX_AUTONOMY_RUNBOOK.md` for shared recovery discipline; substitute Claude entrypoints for Codex-specific preflight.

## Triage Sequence

1. Inspect failed checks and logs.
2. Inspect review comments, bot comments, annotations, and artifacts.
3. Classify each issue:
   - blocker
   - non-blocking risk
   - pre-existing noise
   - invalid signal
4. Reproduce narrowly where practical.
5. Fix root cause with focused commits.
6. Re-run the smallest meaningful verification.
7. Comment with what changed and what was verified.

## Conflict Rules

- Preserve both branches' intended behavior.
- Prefer merge over rebase when conflict resolution starts stalling.
- Do not rewrite history unless explicitly authorized.
- Re-run tests for both sides of the conflict surface.

## Handoff

Report:

- checks/comments inspected
- failures fixed
- commands run and results
- unresolved risk or missing access
- any follow-up issues needed

