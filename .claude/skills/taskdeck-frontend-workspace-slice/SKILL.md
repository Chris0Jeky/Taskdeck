---
name: taskdeck-frontend-workspace-slice
description: Implement Taskdeck frontend shell/workspace changes - Vue routes, stores, components, Home/Today/Boards, keyboard and help states, novice legibility.
paths: ["frontend/**"]
---

# Taskdeck Frontend Workspace Slice

Strengthen the shipped Taskdeck workspace without drifting into disconnected surface breadth.

## Read first

Orient via `autodoc/AGENT_INDEX.md` (the seam map); root `CLAUDE.md` and region rules auto-load. (`frontend/taskdeck-web/CLAUDE.md` is the region rule.)

Read as needed:

- relevant docs under `docs/product` and `docs/manual`
- `frontend/taskdeck-web/package.json`

## Product framing

Prefer changes that reinforce the shipped path:

- `Home -> Inbox or capture -> Review -> Board`
- `Today` as the daily reset and routing surface
- advanced surfaces remain secondary unless the task explicitly targets them

## Frontend guardrails

- preserve board-centered continuity across routes
- preserve review-first trust in copy and action design
- favor readable and actionable empty states
- keep keyboard and escape behavior sane
- do not claim product breadth that is not actually shipped

## Workflow

1. Identify the primary surface and the supporting routes, stores, and components.
2. Reuse existing patterns before adding new state or abstractions.
3. Add or update unit tests for the changed behavior.
4. Use Playwright when route flow, keyboard behavior, or multi-step UX changes.

## Pairing rule

If the task changes capture, proposal review, provenance, or explicit board handoff semantics, use `taskdeck-capture-review-loop` alongside this skill.

## Multi-agent split

If the user authorized subagents, delegation, or parallel work, good parallel splits are:

- route or component implementation
- store or API adjustments
- Playwright or docs follow-through

## Do not use this skill when

- the task is backend-only
- the main risk is in capture, proposal, execute, or provenance semantics rather than workspace UX
