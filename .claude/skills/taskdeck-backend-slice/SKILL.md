---
name: taskdeck-backend-slice
description: Implement Taskdeck backend changes safely. Use when changing .NET API, application, domain, infrastructure, worker, auth, provider-runtime, import-export, notification, archive, or persistence behavior.
---

# Taskdeck Backend Slice

Implement the smallest backend slice that fits the existing layering and contract rules.

## Read first

1. `CLAUDE.md`
2. `AGENTS.md`
3. `docs/STATUS.md`
4. `docs/GOLDEN_PRINCIPLES.md`
5. `docs/TESTING_GUIDE.md`

Read as needed:

- `docs/ISSUE_EXECUTION_GUIDE.md` for backlog-driven work
- feature docs for the touched slice

## Placement rules

Respect the existing layering:

- `Taskdeck.Domain`: core rules and entities only
- `Taskdeck.Application`: use cases, orchestration, service contracts
- `Taskdeck.Infrastructure`: persistence and adapters
- `Taskdeck.Api`: HTTP wiring and transport concerns

Do not move logic outward just to make a controller easier to write.

## Backend guardrails

- keep claims-first identity and authz intact
- preserve `ApiErrorResponse` behavior and stable `401/403/404/409` semantics
- do not trust caller-supplied actor identity
- handle failure branches explicitly
- keep local and test posture deterministic; use mock providers unless live behavior is the explicit task

## Workflow

1. Find the existing pattern before inventing a new one.
2. Put the change in the narrowest correct layer.
3. Add or update the nearest tests.
4. Run targeted tests first, then broaden only as blast radius requires.

## Test routing

- domain rules -> `Taskdeck.Domain.Tests`
- application and service logic -> `Taskdeck.Application.Tests`
- HTTP contracts, authz, and error mapping -> `Taskdeck.Api.Tests`
- CLI behavior -> `Taskdeck.Cli.Tests`
- architecture constraints -> `Taskdeck.Architecture.Tests`

## Multi-agent split

If the task is broad, split by non-overlapping ownership:

- implementation in one layer or feature family
- API contract or regression tests
- docs or handoff verification

## Do not use this skill when

- the task is frontend-only, docs-only, or purely demo-evidence work
- the task is really about capture/review semantics and needs the capture-review-loop skill as primary guide
