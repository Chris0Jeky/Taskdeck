---
name: taskdeck-verification-doc-sync
description: Finish a Taskdeck change with the right checks and doc updates. Use at the end of implementation to choose verification scope, decide whether canonical docs changed, and prepare the repo's required handoff summary.
---

# Taskdeck Verification And Doc Sync

Finish the work completely: verify what changed, update the right docs, and report the result cleanly.

## Read first

1. `AGENTS.md`
2. `docs/TESTING_GUIDE.md`
3. `docs/STATUS.md`
4. `docs/IMPLEMENTATION_MASTERPLAN.md`

Read when relevant:

- `docs/MANUAL_TEST_CHECKLIST.md`
- product or manual docs touched by the change

## Verification workflow

1. Run targeted checks for the touched area.
2. Broaden only if the blast radius justifies it.
3. Decide whether shipped reality or roadmap sequencing actually changed.
4. Update the right docs.
5. Prepare the required Taskdeck handoff summary.

## Canonical doc rule

Update `docs/STATUS.md` and `docs/IMPLEMENTATION_MASTERPLAN.md` only when one of these is true:

- shipped product or engineering reality changed
- the active roadmap or next-step sequencing changed

Do not touch them for narrow local-tooling changes, draft-doc improvements, or evidence-only work. Update `AGENTS.md`, testing docs, product docs, or the specific touched document instead.

## Required handoff shape

Provide:

- summary of changes
- files touched
- tests added or updated
- commands run and results
- docs updated
- notable risks or follow-ups

## Do not claim

- a path is verified if you only reasoned about it
- a feature is shipped if only demo tooling changed
- canonical docs are current if implementation changed and the source-of-truth docs were left stale

