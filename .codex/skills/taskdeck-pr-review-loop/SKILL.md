---
name: taskdeck-pr-review-loop
description: Taskdeck-specific review lenses for a PR — what to look at in this codebase. The review pipeline itself is the global review-and-ship skill and global laws 2 and 11. Use when reviewing a Taskdeck PR.
---

# Taskdeck PR Review Lenses

**Pipeline, severity bar, comment triage, and merge gate: the global `review-and-ship` skill and
global laws 2 and 11 (see the `AGENTS.md` Review Policy pointer).** This skill adds only what is
specific to Taskdeck — do not restate review doctrine here.

## Read first

1. `docs/STATUS.md` (the relevant section only)
2. `AGENTS.md`
3. `docs/GOLDEN_PRINCIPLES.md`
4. The linked issue and PR body
5. The PR diff

## Taskdeck lenses

- auth/authz and cross-user policy regressions (claims-first identity)
- proposal-first automation safety (GP-06)
- persistence/migration correctness (EF Core + SQLite idempotency)
- error contract stability (stable 401/403/404/409) and SignalR contracts
- missing tests or weak assertions
- frontend keyboard/accessibility/loading/error states
- docs/testing-guide drift
- CI workflow or project automation side effects

Do not spend review energy on unrelated style unless it creates risk.

## Sensitive-surface flag

Flag these surfaces in the review so the coordinator can decide whether the change warrants the
extra independent lens its tier allows:

- security/auth/session/token behavior
- migrations or data deletion/retention
- MCP or external agent write surfaces
- capture/review/proposal execution
- GitHub workflows/project automation
- broad frontend route or state flow

## Targeted verification

- Backend: `dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~RelevantTest"`
- Frontend: `cd frontend/taskdeck-web; npx vitest --run -t "relevant test"`

## Tooling

Use GitHub MCP for issue/PR metadata and comments when available; `gh api` REST as fallback.

Do not merge PRs. Do not change repo settings, secrets, protections, environments, or workflow
permissions.
