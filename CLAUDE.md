# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What Is Taskdeck

A local-first execution workspace for developers. Core thesis: near-zero-friction capture with review-first (proposal-based) automation — no silent or destructive mutations. Local persistence via SQLite.

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

### Backend — Clean Architecture layers in `backend/src/`

- **Taskdeck.Domain**: Core entities and business rules. No infrastructure dependencies — keep it pure.
- **Taskdeck.Application**: Use cases and services. Depends only on Domain.
- **Taskdeck.Infrastructure**: Persistence (EF Core + SQLite), external adapters. Implements interfaces defined in Application/Domain.
- **Taskdeck.Api**: ASP.NET Core HTTP endpoints, integration layer, auth, SignalR hubs. Wires everything up via DI.
- **Taskdeck.Cli**: CLI entry point (separate from API).

Tests mirror this layout in `backend/tests/` with an additional `Taskdeck.Architecture.Tests` project for structural enforcement.

### Frontend — `frontend/taskdeck-web/src/`

- **views/**: Route-level pages (BoardView, InboxView, ReviewView, TodayView, HomeView, etc.)
- **store/**: Pinia stores — boardStore, captureStore, queueStore, sessionStore, workspaceStore, notificationStore, etc.
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

## Coding Conventions

- **Backend**: C# conventions, 4-space indent, PascalCase for public members, camelCase for locals. Respect layer boundaries (Domain must not reference Infrastructure).
- **Frontend**: TypeScript + Vue SFCs in PascalCase. Use `<script setup>` and composition API. Meaningful names over abbreviations.
- **Commits**: Present-tense, small, focused. One commit per file when spanning multiple files. File move/rename batches are fine as single commits.

## Key Docs

- `docs/STATUS.md` — source of truth for current state (read first when orienting)
- `docs/IMPLEMENTATION_MASTERPLAN.md` — delivery sequencing / roadmap
- `docs/GOLDEN_PRINCIPLES.md` — stable invariants and guardrails
- `docs/TESTING_GUIDE.md` — test operations reference
- `docs/ISSUE_EXECUTION_GUIDE.md` — dependency-aware issue execution order
- `AGENTS.md` — full contributor protocol (definition of done, work protocol, output expectations)

## CI

Reusable GitHub Actions workflows under `.github/workflows/`. `ci-required.yml` is the gate for PRs. Nightly extended checks in `ci-nightly.yml`.

## Windows Notes

- If `git` resolves to Cygwin or fails with signal errors, use `C:\Program Files\Git\cmd\git.exe` explicitly.
- Do not chain commands with `&&` in PowerShell; use `;` and check `$LASTEXITCODE`.
