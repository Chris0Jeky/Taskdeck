# Taskdeck

Taskdeck is a local-first execution workspace for developers.
It is being shaped around one thesis: capture should be near-zero friction, and automation should stay safe via review-first proposals.

## Product Thesis

Taskdeck focuses on the failure mode that kills most personal task systems: maintenance overhead.

- Fast capture should feel easier than postponing.
- The board should stay organized through proposals, not silent mutations.
- Trust comes from provenance, auditability, and explicit approval.

Current direction:
- capture/inbox pipeline wave is active in backlog (`#199` to `#213`)
- proposal-first automation remains the default and non-negotiable
- no destructive/autonomous apply behavior is enabled by default

## What It Does

- Boards, columns, cards, and labels with WIP-aware flow management
- Archive and restore operations
- Workspace activity and operational surfaces
- Proposal-first automation flows (review before apply)
- Local-first persistence via SQLite

## Core Loop (Direction)

North-star loop:
1. Capture messy input quickly.
2. Triage into structured board changes.
3. Generate a proposal diff.
4. Review and apply explicitly.

What is shipped today:
- proposal-first automation, chat/bootstrap, archive, ops/logs, notifications, realtime collaboration baseline

What is being delivered in the capture wave:
- inbox route and capture modal
- capture triage pipeline to proposal
- capture provenance visibility and end-to-end regression coverage

## Direction Success Criteria (Next 8-12 Weeks)

- capture remains low-friction (target: under 10 seconds from intent to saved artifact)
- review-first proposal loop is practical (target: capture to applicable proposal around 60 seconds)
- automation trust is preserved (no silent/destructive apply behavior by default)
- dogfooding retention improves (consistent weekly use without maintenance fatigue)

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
- Node.js 24.x (minimum 24.13.1 LTS) and npm

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

## LLM Provider Setup

Current shipped runtime:
- `Mock` provider is default.
- `OpenAI` is supported behind explicit config gates.
- `Gemini` runtime support is tracked as `#232` and not shipped yet.

Use `docs/LLM_PROVIDER_SETUP_GUIDE.md` for:
- current demo setup with OpenAI
- provider-agnostic runtime plan (`OpenAI` + `Gemini`)
- security/reliability constraints for live-provider usage

## Container Baseline

From repository root:

```bash
docker build -f deploy/docker/backend.Dockerfile -t taskdeck-api:local .
docker build -f deploy/docker/frontend.Dockerfile -t taskdeck-web:local .
cp deploy/.env.example deploy/.env   # PowerShell: Copy-Item deploy/.env.example deploy/.env
# Set a strong TASKDECK_JWT_SECRET value in deploy/.env before starting.
docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile baseline up -d --build
```

PowerShell shortcuts:

```powershell
powershell -File ./scripts/deploy/Upgrade-DockerDesktop.ps1
powershell -File ./scripts/deploy/Check-ContainerHost.ps1
powershell -File ./scripts/deploy/Build-TaskdeckImages.ps1
powershell -File ./scripts/deploy/Start-TaskdeckStack.ps1 -Build
powershell -File ./scripts/deploy/Smoke-TestTaskdeckStack.ps1
powershell -File ./scripts/deploy/Stop-TaskdeckStack.ps1
```

Container entrypoint URL:
- Reverse proxy: `http://localhost:8080`

For TLS assumptions, forwarded-header posture, staging bootstrap steps, and artifact packaging, see `docs/DEPLOYMENT_CONTAINERS.md`.

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

Rebranding/thesis alignment inputs:
- `docs/InReview/HUMAN/01_PRODUCT_THESIS.md`
- `docs/InReview/HUMAN/03_EXECUTION_ROADMAP.md`

## Contributing

- Open or pick a GitHub issue before larger changes.
- Keep PRs scoped and include verification evidence.
- For contribution guidance and repo rules, see `AGENTS.md`.
