---
name: taskdeck-ci-conflict-recovery
description: Triage and fix Taskdeck PR failures involving CI, stale branches, merge conflicts, review comments, bot comments, or blocked checks.
---

# Taskdeck CI And Conflict Recovery

Use this skill when a PR is blocked by CI, review/bot comments, conflicts, stale branches, or unclear checks.

## Read First

Orient via `autodoc/AGENT_INDEX.md` (the seam map) — find your area in its seams table and jump to the entry point. Read only the relevant section of `docs/STATUS.md` (source of truth; ~1.3k lines — never read end-to-end); don't bulk-read `docs/IMPLEMENTATION_MASTERPLAN.md`. Root `CLAUDE.md`/`AGENTS.md` auto-load — don't re-read them.

For the CI topology (`ci-required.yml` = the merge gate; `reusable-*` + scheduled lanes) use the **Harness/CI/docs** seam row of the map. `scripts/agent_hooks/CLAUDE.md` auto-loads only when the fix touches hook scripts — it does NOT cover `.github/workflows/` changes. Read as needed: the failing workflow under `.github/workflows/`, `docs/TESTING_GUIDE.md`, and the PR's checks/comments/annotations.

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
- Prefer merge over rebase when conflict resolution starts stalling; replace `BRANCH_NAME` with the source ref in `git merge --signoff --no-gpg-sign BRANCH_NAME`. After a conflict, stage the resolution and finish with `git commit -s --no-gpg-sign --no-edit` instead of `git merge --continue`.
- Do not rewrite history unless explicitly authorized.
- Re-run tests for both sides of the conflict surface.

## Handoff

Report:

- checks/comments inspected
- failures fixed
- commands run and results
- unresolved risk or missing access
- any follow-up issues needed

