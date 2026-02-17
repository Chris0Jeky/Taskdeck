# Taskdeck

Taskdeck is a local-first Kanban and execution system for developers.
It combines a .NET 8 backend, Vue 3 frontend, and automation workflows designed for safe, review-first operations.

## What It Does

- Boards, columns, cards, and labels with WIP-aware flow management
- Archive and restore operations
- Workspace activity and operational surfaces
- Proposal-first automation flows (review before apply)
- Local-first persistence via SQLite

## Tech Stack

- Backend: .NET 8, ASP.NET Core Web API, EF Core, SQLite
- Frontend: Vue 3, TypeScript, Pinia, Vite
- Testing: xUnit, Vitest, Playwright

## Repository Layout

- Backend solution: `backend/Taskdeck.sln`
- Backend source: `backend/src`
- Backend tests: `backend/tests`
- Frontend app: `frontend/taskdeck-web`
- Active docs: `docs/`
- Historical docs: `docs/archive/`

## Quick Start

Prerequisites:
- .NET 8 SDK
- Node.js 20+ and npm

Backend:

```bash
dotnet restore backend/Taskdeck.sln
dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj
```

Frontend:

```bash
cd frontend/taskdeck-web
npm install
npm run dev
```

Default URLs:
- API: `http://localhost:5000`
- Swagger: `http://localhost:5000/swagger`
- Frontend: `http://localhost:5173`

## Test Commands

Backend:

```bash
dotnet test backend/Taskdeck.sln -c Release -m:1
```

Frontend unit + type + build:

```bash
cd frontend/taskdeck-web
npx vitest --run --reporter=verbose
npm run typecheck
npm run build
```

Frontend E2E:

```bash
cd frontend/taskdeck-web
TASKDECK_E2E_DB=taskdeck.e2e.local.db npx playwright test --reporter=line
```

For latest verified totals and CI parity, see `docs/STATUS.md` and `docs/TESTING_GUIDE.md`.

## Architecture

- `Taskdeck.Domain`: core entities and rules
- `Taskdeck.Application`: use cases/services
- `Taskdeck.Infrastructure`: persistence and adapters
- `Taskdeck.Api`: HTTP endpoints and integration layer

## Roadmap and Current Status

Start here:
- `docs/STATUS.md` for current shipped reality
- `docs/IMPLEMENTATION_MASTERPLAN.md` for delivery sequencing
- `docs/INDEX.md` for documentation map

## Contributing

- Open or pick a GitHub issue before larger changes.
- Keep PRs scoped and include verification evidence.
- For contribution guidance and repo rules, see `AGENTS.md`.
