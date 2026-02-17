# Taskdeck

Taskdeck is a local-first Kanban system for developers, with a .NET 8 API, Vue 3 frontend, and proposal-first automation workflows.

## Source of Truth

Start with:
- `docs/STATUS.md` for current shipped reality and verified totals.
- `docs/IMPLEMENTATION_MASTERPLAN.md` for execution priority and roadmap.
- `docs/TESTING_GUIDE.md` for canonical test commands and CI parity.
- `docs/INDEX.md` for doc classification (authoritative vs operational vs archive).

If any other document conflicts with these, trust `docs/STATUS.md`.

## Repository Layout

- Backend solution: `backend/Taskdeck.sln`
- Backend source: `backend/src`
- Backend tests: `backend/tests`
- Frontend app: `frontend/taskdeck-web`
- Active docs: `docs/`
- Historical docs: `docs/archive/`

## Local Run

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

## Verification

Backend:

```bash
dotnet test backend/Taskdeck.sln -c Release
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

For latest verified pass counts, see `docs/STATUS.md` and `docs/TESTING_GUIDE.md`.

## Working Model

- Security-first and claims-first identity posture.
- Proposal-first automation: plan/review before mutations.
- Docs governance and architecture checks are CI-gated.
- Work is tracked through GitHub Issues plus Project workflows.
