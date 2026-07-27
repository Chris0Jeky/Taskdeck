---
name: taskdeck-pr-review-loop
description: "Taskdeck-specific review lenses for a PR — what to look at in this codebase. The pipeline itself is the global review-and-ship skill."
user-invocable: true
---

# Taskdeck PR Review Lenses

**Pipeline, severity bar, comment triage, and merge gate: the global `review-and-ship` skill and
global laws 2 and 11.** This skill adds only what is specific to Taskdeck — do not restate
review doctrine here.

## Read First

Orient via `autodoc/AGENT_INDEX.md` (the seam map) — find your area in its seams table and jump to
the entry point. Read only the relevant section of `docs/STATUS.md` (source of truth; ~1.3k lines
— never read end-to-end); don't bulk-read `docs/IMPLEMENTATION_MASTERPLAN.md`. Root
`CLAUDE.md`/`AGENTS.md` auto-load — don't re-read them.

## Taskdeck Lenses

- security/authz gaps (cross-user data exposure, claims-first identity bypass)
- agent safety violations (GP-06: no `approve_proposal` or direct board mutation by agents)
- egress envelope enforcement gaps
- migration/data-loss risk (EF Core + SQLite idempotency)
- race conditions, especially in quota and concurrent-run tracking
- fail-open patterns where fail-closed is needed
- clean architecture violations (Domain referencing Infrastructure)
- HTTP semantics (stable 401/403/404/409) and SignalR contract correctness
- frontend: Vue 3 composition API, Pinia store boundaries, Tailwind conventions
- behavior changes shipping without tests; weak assertions
- docs drift in `docs/STATUS.md` / `docs/IMPLEMENTATION_MASTERPLAN.md`
- CI, scripts, or project-automation breakage

## Sensitive-Surface Flag

Flag these surfaces so the coordinator can decide whether an additional independent lens beyond
the tier-required pass is warranted:

- security/auth/session/token behavior
- migrations or data deletion/retention
- MCP or external-agent write surfaces
- capture/review/proposal execution
- GitHub workflows/project automation
- broad frontend route or state flow

## Targeted Verification

- Backend: `dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~RelevantTest"`
- Frontend: `cd frontend/taskdeck-web; npx vitest --run -t "relevant test"`
