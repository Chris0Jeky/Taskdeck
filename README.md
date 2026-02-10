# Taskdeck

Taskdeck is a personal Kanban and to-do manager for developers.
It is local-first (SQLite), keyboard-friendly, and built with a clean layered backend architecture.

## Core Features

- Boards, columns, cards, and labels
- WIP limits and blocked-card workflows
- Card and column drag-and-drop
- Filtering (text, labels, due date windows, blocked-only)
- Keyboard shortcuts and shortcut help modal
- Toast notifications for CRUD and move operations

## Tech Stack

### Backend

- .NET 8
- ASP.NET Core Web API
- Entity Framework Core
- SQLite
- xUnit + FluentAssertions

### Frontend

- Vue 3 + TypeScript
- Vite
- Pinia
- Vue Router
- Tailwind CSS
- Vitest + Vue Test Utils

## Getting Started

### Prerequisites

- .NET 8 SDK
- Node.js 20+ and npm

### Backend

```bash
cd backend
dotnet restore
dotnet ef database update -p src/Taskdeck.Infrastructure/Taskdeck.Infrastructure.csproj -s src/Taskdeck.Api/Taskdeck.Api.csproj
dotnet run --project src/Taskdeck.Api/Taskdeck.Api.csproj
```

API base URL: `http://localhost:5000`
Swagger: `http://localhost:5000/swagger`

### Frontend

```bash
cd frontend/taskdeck-web
npm install
npm run dev
```

Frontend URL: `http://localhost:5173`

## Testing

### Backend

```bash
dotnet test backend/Taskdeck.sln
```

### Frontend

```bash
cd frontend/taskdeck-web
npm run test -- --run
```

### E2E Smoke

```bash
cd frontend/taskdeck-web
npx playwright test
```

See `docs/TESTING_GUIDE.md` for full details and troubleshooting.

## Reconciled Current Status

As of 2026-02-11:

- Backend tests: 164/164 passing
- Frontend unit tests: 115/115 passing
- Frontend E2E smoke tests: 8/8 passing
- Total automated: 287/287 passing

Phase progress (original roadmap aligned):

1. Phase 1 (Core Data Model and API): 100%
2. Phase 2 (Basic Web UI): 100%
3. Phase 3 (UX Improvements): 100%
4. Phase 4 (Advanced Features): 60%

## CLI (Phase 4 Bootstrap)

Initial CLI commands are available in `backend/src/Taskdeck.Cli`.

```bash
dotnet run --project backend/src/Taskdeck.Cli/Taskdeck.Cli.csproj help
dotnet run --project backend/src/Taskdeck.Cli/Taskdeck.Cli.csproj boards list
dotnet run --project backend/src/Taskdeck.Cli/Taskdeck.Cli.csproj boards list --json
dotnet run --project backend/src/Taskdeck.Cli/Taskdeck.Cli.csproj boards update --board <id> --name "Renamed Board"
dotnet run --project backend/src/Taskdeck.Cli/Taskdeck.Cli.csproj columns create --board <id> --name "In Progress" --wip 3
dotnet run --project backend/src/Taskdeck.Cli/Taskdeck.Cli.csproj cards list --board <id>
dotnet run --project backend/src/Taskdeck.Cli/Taskdeck.Cli.csproj cards list --board <id> --json
```

## Documentation

- `docs/STATUS.md` (single source of truth)
- `docs/IMPLEMENTATION_MASTERPLAN.md` (active roadmap)
- `docs/TESTING_GUIDE.md` (active testing guide)
- `docs/MANUAL_TEST_CHECKLIST.md` (manual validation script with expected outcomes)
- `docs/INDEX.md` (documentation index)
- `docs/archive/README.md` (historical docs map)
- `filesAndResources/taskdeck_technical_design_document.md` (original design document)

## Notes

- Historical session summaries and superseded plans were moved under `docs/archive/`.
- If any old note conflicts with `docs/STATUS.md`, trust `docs/STATUS.md`.
