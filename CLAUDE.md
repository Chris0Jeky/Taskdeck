# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What Is Taskdeck

A local-first execution workspace for developers. Core thesis: near-zero-friction capture with review-first (proposal-based) automation -- no silent or destructive mutations. Local persistence via SQLite.

## Required Reading Before Changes

1. `docs/STATUS.md` -- source of truth for current shipped state (always read first)
2. `docs/IMPLEMENTATION_MASTERPLAN.md` -- delivery history, planned work, roadmap sequencing, and strategic intentions
3. `docs/GOLDEN_PRINCIPLES.md` -- stable invariants and guardrails
4. `AGENTS.md` -- full contributor protocol, definition of done, output expectations

Precedence when instructions conflict: `docs/STATUS.md` > `AGENTS.md` > this file.

## Essential Commands

### Backend (.NET 8)

```bash
dotnet restore backend/Taskdeck.sln
dotnet build backend/Taskdeck.sln -c Release
dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj
dotnet test backend/Taskdeck.sln -c Release -m:1
```

Run a single backend test class:
```bash
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~MyTestClassName"
```

### Frontend (Vue 3 + Vite, Node 24.x)

```bash
cd frontend/taskdeck-web
npm install
npm run dev          # dev server on :5173
npm run typecheck    # vue-tsc type checking
npm run build        # typecheck + vite build
npx vitest --run --reporter=verbose   # unit tests
npx vitest --run -t "test name"       # single test by name
npm run lint         # eslint
```

E2E (Playwright):
```bash
cd frontend/taskdeck-web
TASKDECK_E2E_DB=taskdeck.e2e.local.db npx playwright test --reporter=line
npx playwright test tests/e2e/some-spec.spec.ts   # single E2E file
```

### Docker (from repo root)

```bash
docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile baseline up -d --build
```

### Default URLs

- API: `http://localhost:5000` | Swagger: `http://localhost:5000/swagger` | Frontend: `http://localhost:5173`

## Architecture

### Backend -- Clean Architecture layers in `backend/src/`

- **Taskdeck.Domain**: Core entities and business rules. No infrastructure dependencies -- keep it pure.
- **Taskdeck.Application**: Use cases and services. Depends only on Domain.
- **Taskdeck.Infrastructure**: Persistence (EF Core + SQLite), external adapters. Implements interfaces defined in Application/Domain.
- **Taskdeck.Api**: ASP.NET Core HTTP endpoints, integration layer, auth, SignalR hubs. Wires everything up via DI.
- **Taskdeck.Cli**: CLI entry point (separate from API).

Tests mirror this layout in `backend/tests/` with an additional `Taskdeck.Architecture.Tests` project for structural enforcement.

### Frontend -- `frontend/taskdeck-web/src/`

- **views/**: Route-level pages (BoardView, InboxView, ReviewView, TodayView, HomeView, etc.)
- **store/**: Pinia stores -- boardStore, captureStore, queueStore, sessionStore, workspaceStore, notificationStore, etc.
- **api/**: HTTP client modules for backend communication
- **composables/**: Shared Vue composition functions
- **components/**: Reusable UI components
- **router/**: Vue Router configuration

Uses Tailwind CSS, TypeScript, and Vue 3 composition API (`<script setup>`).

### Key Data Flow

1. User captures input → captureStore → backend inbox API
2. System generates a proposal (structured board change)
3. User reviews proposal in ReviewView
4. Explicit approval applies changes to board via boardStore

### Realtime

SignalR (`@microsoft/signalr`) provides realtime board collaboration.

### LLM Providers

Mock provider is default. OpenAI and Gemini supported behind config gates. See `docs/platform/LLM_PROVIDER_SETUP_GUIDE.md`.

## Work Protocol

- Before edits: write a short plan (files, approach, risks, tests).
- Keep diffs small and scoped; avoid large mixed refactors.
- After edits: run required checks and report results.
- For product-facing slices, ensure scope aligns with the thesis (reduce maintenance overhead/capture friction, preserve review-first trust).

## Definition of Done

- Behavior changes ship with tests (unit/integration/E2E as appropriate).
- Handle error cases explicitly; do not swallow failures.
- Update docs when reality changes: `docs/STATUS.md` for current state, `docs/IMPLEMENTATION_MASTERPLAN.md` for delivery history and planned work.
- HTTP semantics: use stable codes (401/403/404/409). Claims-first identity.

## Coding Conventions

- **Backend**: C# conventions, 4-space indent, PascalCase for public members, camelCase for locals. Respect layer boundaries (Domain must not reference Infrastructure).
- **Frontend**: TypeScript + Vue SFCs in PascalCase. Use `<script setup>` and composition API. Meaningful names over abbreviations.
- **Commits**: Present-tense, small, focused. One commit per file when spanning multiple files. File move/rename batches are fine as single commits.

## Testing Guidelines

- Mirror production namespaces in test namespaces and file names.
- Backend tests: project-per-layer in `backend/tests/` (Domain.Tests, Application.Tests, Api.Tests, Architecture.Tests).
- Frontend: vitest for unit tests, Playwright for E2E. See `docs/TESTING_GUIDE.md`.

## CI

Reusable GitHub Actions workflows under `.github/workflows/`. `ci-required.yml` is the gate for PRs. Nightly extended checks in `ci-nightly.yml`.

## Architecture Decision Records (ADRs)

ADRs live in `docs/decisions/`. See `docs/decisions/README.md` for the template and conventions.

**When to create an ADR**: Write one when a decision chooses between competing approaches, establishes a project-wide constraint, has hard-to-reverse consequences, or would surprise a future contributor. This includes technology selections, data model choices, security posture changes, automation safety boundaries, and strategic product pivots.

**How to create an ADR**: Use the next available number (`ADR-NNNN`), follow the template (Context, Decision, Alternatives, Consequences, References), and add the entry to `docs/decisions/INDEX.md`. Mark status as `Proposed` until ratified, then `Accepted`.

**Do not skip ADRs** for decisions that affect architecture, security posture, or cross-cutting conventions -- even when the change is small, the reasoning matters for future contributors who weren't in the conversation.

## Key Docs

- `docs/STATUS.md` -- current shipped reality (what is true now)
- `docs/IMPLEMENTATION_MASTERPLAN.md` -- delivery history, roadmap, and planned work (what was done and what comes next)
- `docs/GOLDEN_PRINCIPLES.md` -- stable invariants
- `docs/decisions/INDEX.md` -- architecture decision records
- `docs/TESTING_GUIDE.md` -- test operations reference
- `docs/ISSUE_EXECUTION_GUIDE.md` -- dependency-aware issue execution order
- `docs/MCP_TOOLING_GUIDE.md` -- MCP tool selection rules
- `AGENTS.md` -- full contributor protocol

## Worktree Isolation for Parallel Agents

When launching subagents with `isolation: "worktree"`, follow the protocol in `docs/WORKTREE_AGENT_PROTOCOL.md`. Key rules:
- NEVER include absolute paths to the main checkout in worktree agent prompts
- First agent action: run the inline worktree guard from the protocol
- All file paths must use the exported `$WT_PROJECT_DIR` variable
- Shell state does not persist between Bash tool calls -- agents must use absolute paths
- After agents complete, verify main checkout is still clean on the default branch

## Windows Notes

- Run `bash scripts/check-git-env.sh` to validate git resolution and index.lock state before a work session.
- If `git` resolves to Cygwin or fails with signal errors, use `C:\Program Files\Git\cmd\git.exe` explicitly (or add `C:\Program Files\Git\cmd` to the front of `PATH`).
- Do not chain commands with `&&` in PowerShell; use `;` and check `$LASTEXITCODE`.
- If `.git/index.lock` blocks commits, check for active git processes before removing it. The `check-git-env.sh` script automates this check.