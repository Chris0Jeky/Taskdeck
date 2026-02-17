# GitHub Project Automation Guide

This document defines the canonical setup for the `Taskdeck Execution` GitHub Project.
Use this to keep intake and status transitions consistent for every issue and PR.
Last Updated: 2026-02-17

## Canonical Status Model

Required `Status` options:
- `Pending` (default intake state)
- `Now`
- `Next`
- `Blocked`
- `Review`
- `Done`

Rules:
- Every new project item must receive `Status=Pending` automatically.
- `Done` is terminal for closed or merged work.
- `Now` is WIP-limited to one major item at a time (team discipline + weekly audit).

## Required Labels

Operational labels:
- `bug` (GitHub default; keep it present because `bug_report` template uses it)
- `security`
- `hardening`
- `backend`
- `frontend`
- `ux`
- `testing`
- `docs`
- `refactor`
- `tech-debt`
- `starter-packs`
- `llm`

## Project Views

Keep these views:
- `Pending` (filter: `status:"Pending"`)
- `Now` (filter: `status:"Now"`)
- `Next` (filter: `status:"Next"`)
- `Blocked` (filter: `status:"Blocked"`)
- `Review` (filter: `status:"Review"`)
- `Done` (filter: `status:"Done"`)
- `Execution Board` (board view, `Column by: Status`)

Operational safety views:
- `No Status` table view with `Status` empty filter (`no:status`).
- `WIP Audit` table view with `status:"Now"` for weekly WIP cap validation.

Safety discipline:
- Check `No Status` before each release candidate and during weekly backlog seeding.
- Resolve all empty-status items before merge trains or release tagging.

## Workflow Automation (GitHub Project UI)

Project: `Taskdeck Execution`

1. `Auto-add to project` (ON)
- Filter must include repository `Chris0Jeky/Taskdeck`.
- Intake filter must include both issues and pull requests.

2. `Item added to project` (ON)
- Action: `Set field`.
- Field: `Status`.
- Value: `Pending`.

3. `Item reopened` (ON)
- Action: set `Status=Pending`.

4. `Item closed` (ON)
- Action: set `Status=Done`.

5. `Pull request linked to issue` (ON)
- Action: set linked issue `Status=Review`.

6. `Pull request merged` (ON)
- Action: set `Status=Done`.

Optional:
- `Code review approved` can set `Status=Review`.
- `Code changes requested` can set `Status=Now` or `Status=Blocked`.

## Drift Controls

- Issue templates must only use labels that exist in the repo.
- Blank issues should be disabled to force templates.
- CI must run governance checks:
  - `node scripts/check-docs-governance.mjs`
  - `node scripts/check-github-ops-governance.mjs`

## Verification Checklist

After setup changes:
- Create a test issue and confirm it auto-adds with `Status=Pending`.
- Reopen the issue and confirm it returns to `Pending`.
- Close the issue and confirm `Status=Done`.
- Create a PR linked to an issue and confirm issue `Status=Review`.
- Merge PR and confirm issue and PR items move to `Done`.
- Open `No Status` and confirm only empty-status items are listed.

## Weekly Backlog Seeding Cadence (OPS-06)

Goal:
- Keep the project populated with near-horizon, dependency-aware items without overloading WIP.

Weekly process:
1. Review `docs/STATUS.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`, and `docs/TaskdeckNextWorkChecklist.md`.
2. Select the highest-priority items whose dependencies are complete.
3. Create/update issues with explicit acceptance criteria and required labels.
4. Ensure each issue body includes dependency mapping (`Depends on #...`, `Unblocks #...` when applicable).
5. Place items into project statuses according to WIP rules.

WIP-aware intake limits:
- Maximum 5 newly-seeded issues per week.
- Maximum 1 major issue in `Now`.
- Maximum 2 issues in `Next`.
- Remaining seeded issues stay in `Pending` until promoted.

Evidence of execution:
- 2026-02-16 seeding pass populated Stage 0 governance issues (`#43`, `#59`, `#41`, `#55`, `#60`, `#56`) and Stage 1 security tranche issues.

