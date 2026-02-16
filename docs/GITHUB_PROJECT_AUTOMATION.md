# GitHub Project Automation Guide

This document defines the canonical setup for the `Taskdeck Execution` GitHub Project.
Use this to keep intake and status transitions consistent for every issue and PR.

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
- `Done` is terminal for closed/merged work.
- `Now` is WIP-limited to one major item at a time (team discipline + weekly audit).

## Required Labels

Operational labels:
- `security`
- `backend`
- `frontend`
- `ux`
- `testing`
- `docs`
- `refactor`
- `starter-packs`
- `llm`

## Project Views

Keep these views:
- `Now` (filter: `status:"Now"`)
- `Next` (filter: `status:"Next"`)
- `Pending` (filter: `status:"Pending"`)
- `Blocked` (filter: `status:"Blocked"`)
- `Review` (filter: `status:"Review"`)
- `Done` (filter: `status:"Done"`)
- `Execution Board` (Board view, `Column by: Status`)

Operational safety views:
- `Needs Status` table view with `Status` empty filter.
- `WIP Audit` table view with `status:"Now"` for weekly WIP cap validation.

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
- Action: set `Status=Review` (for the linked issue item).

6. `Pull request merged` (ON)
- Action: set `Status=Done`.

Optional:
- `Code review approved` can set `Status=Review` if you want explicit reviewer-state visibility.
- `Code changes requested` can set `Status=Now` or `Blocked` per team preference.

## Drift Controls

- Issue templates must only use labels that exist in the repo.
- Blank issues should be disabled to force templates.
- CI must run governance checks:
  - `node scripts/check-docs-governance.mjs`
  - `node scripts/check-github-ops-governance.mjs`

## Verification Checklist

After setup changes:
- Create a test issue -> confirm it auto-adds with `Status=Pending`.
- Reopen the issue -> confirm it returns to `Pending`.
- Close the issue -> confirm `Done`.
- Create a test PR linked to an issue -> confirm linked issue moves to `Review`.
- Merge PR -> confirm issue and PR items become `Done`.

