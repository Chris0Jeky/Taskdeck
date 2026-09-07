---
name: taskdeck-repo-onramp
description: Orient to the Taskdeck repo before editing. Use when starting a session, entering an unfamiliar area, reconciling a broad request against current reality, or turning a vague task into a scoped plan.
---

# Taskdeck Repo Onramp

Establish current Taskdeck truth before editing code or docs.

## Read first

1. `autodoc/AGENT_INDEX.md` to locate the task's seam.
2. `AGENTS.md`, the shared repository facts and proving checks in `CLAUDE.md`, and applicable scoped instructions. Re-read `.agent-harness/tier.json` for authority.
3. Relevant headings or line ranges in `docs/STATUS.md`; reconcile claims with code and current execution evidence. Do not read the complete historical record.
4. `.codex/memories/00_ACTIVE.md` for lane coordination before claiming work, and `OUTSTANDING_TASKS.md` for remaining human actions. Neither grants authority.

Read when relevant:

- `docs/START_HERE.md` for product-facing or UX work
- `docs/GITHUB_PROJECT_AUTOMATION.md` for issue, PR, or project-ops work
- Relevant sections of `docs/IMPLEMENTATION_MASTERPLAN.md` and `docs/ISSUE_EXECUTION_GUIDE.md` when selecting or sequencing work
- Relevant invariants in `docs/GOLDEN_PRINCIPLES.md` and proving sections in `docs/TESTING_GUIDE.md` for the touched seam
- feature-specific docs for the touched slice

## Produce a working summary

Extract only what the current task needs:

- current thesis and near-horizon priorities
- shipped path versus planned breadth
- constraints that must not be broken
- likely files, layers, tests, and docs affected

Fixed truths unless the task explicitly changes them:

- capture should stay low-friction
- automation stays review-first
- no silent or destructive apply by default
- novice-first product legibility beats breadth
- active docs beat archive docs on conflict

## Plan before edits

Write a short plan covering:

- files likely touched
- approach
- risks
- tests to run
- docs that may need sync

## Multi-agent split

If subagents are efficient or effective, split by ownership:

- backend implementation agent
- frontend implementation agent
- docs or verification agent

Keep one coordinator responsible for synthesis and final verification.

## Do not use this skill when

- the task is already tightly scoped in a familiar area
- you only need final verification or doc sync
