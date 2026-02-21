# Testing Guide

This is the active testing guide for Taskdeck.

Last Updated: 2026-02-21
Companion Active Docs:
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/TESTING_GUIDE.md`
- `docs/MANUAL_TEST_CHECKLIST.md`

## Current Verified Totals (2026-02-20)

- Backend: 589/589 passing
  - Domain: 93
  - Application: 336
  - API integration: 152
  - CLI contract: 4
  - Architecture boundaries: 4
- Frontend unit: 292/292 passing
- Frontend E2E (smoke + automation/ops + starter-pack fixtures): 19/19 passing
- Combined automated total: 900/900 passing

## Backend Commands

Run full backend verification (recommended):

```bash
dotnet test backend/Taskdeck.sln -c Release -m:1
```

Run project-split backend verification:

```bash
dotnet test backend/tests/Taskdeck.Domain.Tests/Taskdeck.Domain.Tests.csproj -c Release
dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release
dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release
dotnet test backend/tests/Taskdeck.Cli.Tests/Taskdeck.Cli.Tests.csproj -c Release
dotnet test backend/tests/Taskdeck.Architecture.Tests/Taskdeck.Architecture.Tests.csproj -c Release
```

Note:
- If `Debug` runs fail with file-lock errors, stop running `Taskdeck.Api` processes or use `-c Release`.

## Frontend Unit + Build

```bash
cd frontend/taskdeck-web
npx vitest --run --reporter=verbose
npm run typecheck
npm run build
```

## Frontend E2E

Install browser once:

```bash
cd frontend/taskdeck-web
npx playwright install chromium
```

Run E2E suite:

```bash
cd frontend/taskdeck-web
npx playwright test --reporter=line
```

## Container Baseline Validation

```bash
TASKDECK_JWT_SECRET=local-test-secret docker compose -f deploy/docker-compose.yml --profile baseline config
docker build -f deploy/docker/backend.Dockerfile -t taskdeck-api:local .
docker build --build-arg VITE_API_BASE_URL=/api -f deploy/docker/frontend.Dockerfile -t taskdeck-web:local .
```

Deployment script smoke path (PowerShell):

```powershell
powershell -File ./scripts/deploy/Start-TaskdeckStack.ps1
powershell -File ./scripts/deploy/Smoke-TestTaskdeckStack.ps1 -Port 8080  # if TASKDECK_PROXY_PORT differs, set -Port to match
powershell -File ./scripts/deploy/Stop-TaskdeckStack.ps1
```

## MCP Operations Validation

```powershell
docker mcp server ls
powershell -File ./scripts/mcp/Test-DockerMcpProfile.ps1
```

Optional servers (`postman`, `dockerhub`) warning mode:

```powershell
powershell -File ./scripts/mcp/Test-DockerMcpProfile.ps1 -IncludeOptional
```

Optional servers strict mode (fail-fast on missing prereqs/runtime failures):

```powershell
powershell -File ./scripts/mcp/Test-DockerMcpProfile.ps1 -IncludeOptional -FailOnOptionalErrors
```

CI-friendly variants:

```powershell
powershell -File ./scripts/mcp/Test-DockerMcpProfile.ps1 -CiMode
powershell -File ./scripts/mcp/Test-DockerMcpProfile.ps1 -IncludeOptional -SkipOptionalWhenMissingPrereqs -CiMode
powershell -File ./scripts/mcp/Test-DockerMcpProfile.ps1 -IncludeOptional -FailOnOptionalErrors -CiMode
```

## CI Gates

Workflow: `.github/workflows/ci.yml`

- `docs-governance`
  - Enforces required active docs and docs index invariants
- `backend-architecture`
  - Enforces architecture boundaries in CI
- `backend-unit`
  - Domain + Application + CLI contract tests
  - Ubuntu and Windows matrix
- `api-integration`
  - API integration tests
  - Ubuntu and Windows matrix
- `frontend-unit`
  - Vitest + typecheck + build
  - Ubuntu and Windows matrix
- `container-images`
  - Validates compose rendering
  - Builds backend/frontend container images
  - Exports compressed image artifacts plus SHA256 checksums
- `e2e-smoke`
  - Playwright smoke + automation/ops + fixture bootstrap flow
  - Ubuntu only
  - Depends on all prior gates

## Coverage Map

- Domain invariants:
  - `backend/tests/Taskdeck.Domain.Tests`
- Application services:
  - `backend/tests/Taskdeck.Application.Tests`
  - Includes board/card/column/label/auth/authorization/board-access/export-import/history/queue plus automation/archive/chat/ops/log services
  - Includes database export/import guardrail coverage (sandbox gating, payload validation, file replacement)
  - Includes starter-pack manifest parsing/validation, first-party catalog validity, and apply-planning coverage
- HTTP contracts and behavior mappings:
  - `backend/tests/Taskdeck.Api.Tests`
  - Includes core + automation/archive/chat/ops/log/health controllers
  - Includes `ResultExtensions` mapping tests for standardized API error/status behavior
- CLI contracts:
  - `backend/tests/Taskdeck.Cli.Tests`
- Architecture boundaries:
  - `backend/tests/Taskdeck.Architecture.Tests`
- Frontend unit behavior:
  - `frontend/taskdeck-web/src/tests`
  - Components, stores, API modules, composables, utilities
  - Includes shared utility tests for `queryBuilder` and `errorMessage`
- End-to-end journeys:
  - `frontend/taskdeck-web/tests/e2e`
  - Includes deterministic starter-pack fixture bootstrap coverage for `small`, `medium`, and `edge` manifest scenarios
  - Includes unauthenticated SignalR negotiate rejection coverage aligned with the runtime client handshake path

## Manual Verification

Use `docs/MANUAL_TEST_CHECKLIST.md` for action-by-action manual validation.
Use `docs/OBSERVABILITY_BASELINE.md` for telemetry dashboard/alert baseline and observability smoke validation.

## Development Sandbox Mode

For local development only, authorization bypass can be enabled via:
- `backend/src/Taskdeck.Api/appsettings.Development.json`
- `DevelopmentSandbox.Enabled = true`

Safety boundary:
- Sandbox bypass is forced off outside Development environment.
- Validation and data integrity rules still apply.
