---
name: taskdeck-repo-onramp
description: Orient to the Taskdeck repo before editing. Use when starting a Taskdeck session, entering an unfamiliar area, reconciling a broad request against current shipped reality or roadmap constraints, or turning a vague task into a scoped implementation plan.
---

# Taskdeck Repo Onramp

Establish current Taskdeck truth before editing code or docs.

## Read first

1. `AGENTS.md`
2. `docs/STATUS.md`
3. `docs/IMPLEMENTATION_MASTERPLAN.md`
4. `docs/GOLDEN_PRINCIPLES.md`
5. `docs/ISSUE_EXECUTION_GUIDE.md`
6. `docs/MCP_TOOLING_GUIDE.md`
7. `docs/TESTING_GUIDE.md`

Read these only when relevant:

- `docs/START_HERE.md` for product-facing or UX-facing work
- `docs/GITHUB_PROJECT_AUTOMATION.md` for issue, PR, or project-ops work
- feature-specific docs for the touched slice

## Produce a working summary

Extract only what the current task needs:

- current thesis and near-horizon priorities
- shipped path versus planned breadth
- constraints that must not be broken
- likely files, layers, tests, and docs affected

Treat these as fixed unless the task explicitly changes them:

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

If work spans concerns, split by ownership:

- backend implementation
- frontend implementation
- docs or verification

Keep one coordinator responsible for synthesis and final verification.

## Tool posture

- follow MCP-first guidance from `docs/MCP_TOOLING_GUIDE.md`
- use `rg` for repo search on Windows
- do not guess current repo truth when active docs already define it

## Do not use this skill when

- the task is already tightly scoped in a familiar area and you can move straight to a more specific Taskdeck skill
- you only need final verification or doc sync

