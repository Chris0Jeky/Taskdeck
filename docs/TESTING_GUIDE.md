# Testing Guide

This is the active testing guide for Taskdeck.

Last Updated: 2026-02-25
Companion Active Docs:
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/TESTING_GUIDE.md`
- `docs/MANUAL_TEST_CHECKLIST.md`
- `docs/GOLDEN_PRINCIPLES.md`

## Current Verified Totals (2026-02-25)

- Backend: 887/887 passing
  - Domain: 122
  - Application: 491
  - API integration: 262
  - CLI contract: 4
  - Architecture boundaries: 8
- Frontend unit: 378/378 passing
- Frontend E2E (smoke + automation/ops + capture loop + starter-pack fixtures + concurrency harness): 23/23 passing
- Combined automated total: 1288/1288 passing

Verification note:
- backend totals were re-verified on 2026-02-25 via `dotnet test backend/Taskdeck.sln -c Release -m:1`
- frontend unit/build totals were re-verified on 2026-02-25 via `npm run lint`, `npm run test:coverage`, `npm run typecheck`, and `npm run build`
- frontend E2E totals were re-verified on 2026-02-25 via fallback frontend-port workflow (`TASKDECK_E2E_FRONTEND_PORT=5001`, `TASKDECK_E2E_API_CORS_ORIGINS=http://localhost:5001,http://localhost:5173,http://localhost:5174`) with `23/23` passing
- default local E2E startup on `localhost:5173` may still fail on restricted hosts (`listen EACCES`); use documented fallback workflow

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
npm run lint
npm run test:coverage
npm run typecheck
npm run build
```

Frontend lint suppression guidance:
- Prefer fixing lint violations over suppressing them.
- Keep suppressions as narrow as possible (`eslint-disable-next-line` with reason).
- Avoid file-wide disables unless absolutely required and documented with a follow-up issue.

Frontend coverage threshold policy:
- Coverage thresholds are enforced via `frontend/taskdeck-web/vitest.config.ts` and are part of the required CI gate.
- Global thresholds protect against broad regressions; per-surface thresholds protect high-signal areas (`src/api`, `src/store`, `src/composables`, `src/utils`, `src/components/board`).
- Ratchet rule: thresholds may stay flat or increase, but must not decrease.
- Threshold breach behavior can be validated locally with an override command, for example:
  - `cd frontend/taskdeck-web && npx vitest run --coverage --coverage.thresholds.lines=99 --coverage.thresholds.statements=99 --coverage.thresholds.functions=99 --coverage.thresholds.branches=99`

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

Fallback (alternate frontend port, keep default command unchanged):

PowerShell:

```powershell
cd frontend/taskdeck-web
$env:TASKDECK_E2E_FRONTEND_PORT='5001'
$env:TASKDECK_E2E_API_CORS_ORIGINS='http://localhost:5001,http://localhost:5173,http://localhost:5174'
npx playwright test --reporter=line
```

Bash:

```bash
cd frontend/taskdeck-web
TASKDECK_E2E_FRONTEND_PORT=5001 TASKDECK_E2E_API_CORS_ORIGINS='http://localhost:5001,http://localhost:5173,http://localhost:5174' npx playwright test --reporter=line
```

Optional E2E env overrides (Playwright config):
- `TASKDECK_E2E_FRONTEND_HOST` (default `localhost`)
- `TASKDECK_E2E_FRONTEND_PORT` (default `5173`)
- `TASKDECK_E2E_FRONTEND_BASE_URL` (default `http://{host}:{port}`)
- `TASKDECK_E2E_API_BASE_URL` (default `http://localhost:5000/api`)
- `TASKDECK_E2E_API_CORS_ORIGINS` (comma-separated origin list passed to backend process as `Cors__DevelopmentAllowedOrigins__{index}`)

Troubleshooting note (Windows local environments):
- if Playwright startup fails with `listen EACCES` for frontend port `5173`, the local host may block that port for user-space listeners.
- using a temporary alternate frontend port also requires matching backend CORS origin configuration; otherwise API preflight requests fail before E2E tests execute.
- investigation details and reproduction commands are documented in `docs/analysis/2026-02-25_frontend-gate-port-bind-and-cors-blockers.md`.

Run concurrency harness spec only:

```bash
cd frontend/taskdeck-web
npm run test:e2e:concurrency
```

## Load Harness (k6 + Playwright Concurrency)

Run local k6 board-heavy profile (backend API must be reachable at `K6_BASE_URL`):

```bash
docker run --rm --network host \
  -e K6_BASE_URL=http://127.0.0.1:5000/api \
  -e K6_VUS=20 \
  -e K6_DURATION=90s \
  -e K6_USER_POOL=6 \
  -v "$PWD:/work" \
  -w /work \
  grafana/k6:0.49.0 \
  run tests/load/k6/board-heavy-load.js \
  --summary-export frontend/taskdeck-web/test-results/load/k6-summary.json
```

Notes:
- tune `K6_VUS`, `K6_DURATION`, and `K6_USER_POOL` per machine capacity.
- script thresholds fail on sustained latency/error budget breaches and emit actionable status/body diagnostics.

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

Required workflow: `.github/workflows/ci-required.yml`

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
  - Lint + coverage-threshold Vitest + typecheck + build
  - Ubuntu and Windows matrix
  - Uploads JUnit + coverage artifacts (`test-results/`, `coverage/`) for triage
- `container-images`
  - Validates compose rendering
  - Builds backend/frontend container images
  - Exports compressed image artifacts plus SHA256 checksums
- `e2e-smoke`
  - Playwright smoke + automation/ops + fixture bootstrap flow
  - Ubuntu only
  - Depends on all prior gates

Extended workflow: `.github/workflows/ci-extended.yml`

- `workflow-lint`
  - Actionlint validation for `.github/workflows/**` drift
- `dependency-review`
  - PR dependency change risk signal (`actions/dependency-review-action`)
- `backend-solution` + `e2e-smoke` + `load-concurrency-harness`
  - opt-in on PRs labeled `testing` or manual `workflow_dispatch` (runs Playwright smoke suite via `reusable-e2e-smoke.yml`)
  - load harness lane runs k6 board-heavy profile plus Playwright multi-session concurrency spec via `reusable-load-concurrency-harness.yml`

Nightly workflow: `.github/workflows/ci-nightly.yml`

- scheduled/manual backend solution regression (`dotnet test backend/Taskdeck.sln -c Release -m:1`)
- scheduled/manual E2E smoke suite (`reusable-e2e-smoke.yml`)
- scheduled/manual load-concurrency harness (`reusable-load-concurrency-harness.yml`)
- scheduled/manual container image regression

Nightly quality workflow: `.github/workflows/nightly-quality.yml`

- scheduled/manual reporting lane for quality telemetry (non-blocking for required PR CI checks)
- backend coverage artifacts:
  - Domain coverage (`Taskdeck.Domain.Tests` with XPlat Code Coverage)
  - Application coverage (`Taskdeck.Application.Tests` with XPlat Code Coverage)
- frontend coverage artifacts:
  - `npm run test:coverage` output (`coverage/` + `test-results/`)
- dependency/security signal artifacts:
  - `dotnet list package --vulnerable --include-transitive` output + exit code
  - `npm audit --audit-level=high --json` output + exit code

Triage usage:
- check workflow step summary first for signal exit codes
- inspect uploaded artifacts to differentiate command failures from dependency findings
- treat this lane as reporting-first; promote to stricter gating only through a dedicated follow-up issue/decision

Release/security workflow: `.github/workflows/release-security.yml`

- release/tag/manual dependency inventory artifact generation
- backend/frontend vulnerability signal capture
- reusable container artifact/checksum lane for release-ready outputs

## Testing Harness Improvement Wave (2026-02-23)

Tracking issues:
- wave tracker: `#254`
- net-new execution: `#255` to `#260`

Already-covered pack scenarios (no duplicate implementation issue required):
- WIP limit enforcement already covered across application/API/E2E.
- sandbox-gated database import/export rejection outside Development already covered.
- starter-pack idempotency/conflict safety already covered.

Knowledge transfer applied to existing seeds:
- `#89`: targeted property/fuzz pilot surfaces (manifest/query/import-export boundaries)
- `#90`: non-blocking scheduled mutation-lane posture
- `#106`: dependency/security signal command baseline (`dotnet list package --vulnerable`, `npm audit`)
- `#168`: CI topology routing for OpenAPI/nightly-quality lanes

Wave-1 execution intent:
- remove deterministic test flake vectors first (`#255`)
- add persistence and contract regression lock-in next (`#256`, `#257`)
- then add harness-level guardrails (`#258`, `#259`, `#260`) with non-blocking rollout where appropriate

Useful local checks for this wave:

```bash
rg -n "Thread\\.Sleep|new Promise\\(.*setTimeout" backend/tests frontend/taskdeck-web/tests/e2e
dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release --filter "FullyQualifiedName~ApiErrorContractApiTests"
(cd frontend/taskdeck-web && npx playwright test tests/e2e/smoke.spec.ts tests/e2e/automation-ops.spec.ts tests/e2e/capture-loop.spec.ts --reporter=line)
node scripts/check-golden-principles.mjs
node scripts/check-docs-governance.mjs
```

OpenAPI guardrail local checks (`#258`):

```powershell
./scripts/ci/generate-openapi-artifact.ps1 -OutputPath "artifacts/openapi/taskdeck-api.json"
./scripts/ci/validate-openapi.ps1 -SpecPath "artifacts/openapi/taskdeck-api.json"
```

Malformed-output simulation (deterministic parse failure check):

```powershell
"not-json" | Set-Content -Path artifacts/openapi/invalid-openapi.json
./scripts/ci/validate-openapi.ps1 -SpecPath "artifacts/openapi/invalid-openapi.json"
```

Follow-up intentionally deferred from this issue:
- snapshot/diff enforcement against a checked-in OpenAPI baseline remains a future enhancement
- current guardrail scope is generation + parse/shape validation + CI artifact publication

## Outreach CRM Deferred Wave (Planning, 2026-02-23)

Tracking issues:
- wave tracker: `#262`
- deferred execution: `#263` to `#268`

Reuse links (no duplicate implementation issue):
- `#75` delivered import-adapter foundation for outreach CSV mapping/dedupe profile
- `#77` analytics model/dashboards for future outreach scoreboard metrics
- `#175` first-party starter-pack catalog expansion for outreach blueprint inclusion

Planned quality expectations when implementation starts:
- YAML front-matter parser round-trip stability tests (contact fields + timeline preservation)
- cadence scheduling determinism + throughput-control guardrail tests
- API/UX regression for contact logging and dashboard action loops
- E2E coverage for outreach loop: import/apply -> contact update -> cadence proposal -> dashboard action flow

## Coverage Map

- Domain invariants:
  - `backend/tests/Taskdeck.Domain.Tests`
- Application services:
  - `backend/tests/Taskdeck.Application.Tests`
  - Includes board/card/column/label/auth/authorization/board-access/export-import/history/queue plus automation/archive/chat/ops/log services
  - Includes database export/import guardrail coverage (sandbox gating, payload validation, file replacement)
  - Includes external import-adapter parsing and board upsert orchestration coverage (CSV/outreach profile, dedupe policy, rollback safety path)
  - Includes starter-pack manifest parsing/validation, first-party catalog validity, and apply-planning coverage
- HTTP contracts and behavior mappings:
  - `backend/tests/Taskdeck.Api.Tests`
  - Includes core + automation/archive/chat/ops/log/health controllers
  - Includes board-scoped external import endpoint coverage (authz, malformed input, duplicate handling, apply/update flow, rollback safety)
  - Includes outbound webhook API and worker coverage (`OutboundWebhooksApiTests`, `OutboundWebhookDeliveryWorkerTests`) for claim/reload handling, cancellation requeue, and non-success HTTP retry/dead-letter branches
  - Includes `ResultExtensions` mapping tests for standardized API error/status behavior
- CLI contracts:
  - `backend/tests/Taskdeck.Cli.Tests`
- Architecture boundaries:
  - `backend/tests/Taskdeck.Architecture.Tests`
  - Enforces project-reference boundaries between Domain/Application/Infrastructure/API projects
  - Enforces source-layer purity via forbidden namespace imports in Domain and Application source trees
  - Enforces API controller boundary invariants:
    - only `AuthController` and `HealthController` may inherit `ControllerBase` directly
    - protected controllers must declare `[Authorize]`
  - Failure remediation:
    - move forbidden dependencies to the correct layer abstraction/interface
    - route protected HTTP surface through `AuthenticatedControllerBase`
    - add/restore `[Authorize]` on protected controller classes
- Frontend unit behavior:
  - `frontend/taskdeck-web/src/tests`
  - Components, stores, API modules, composables, utilities
  - Includes shared utility tests for `queryBuilder` and `errorMessage`
- End-to-end journeys:
  - `frontend/taskdeck-web/tests/e2e`
  - Includes deterministic starter-pack fixture bootstrap coverage for `small`, `medium`, and `edge` manifest scenarios
  - Includes unauthenticated SignalR negotiate rejection coverage aligned with the runtime client handshake path
  - Includes dedicated multi-session concurrency regression coverage (`tests/e2e/concurrency.spec.ts`)
- Load and concurrency API profile:
  - `tests/load/k6/board-heavy-load.js`
  - Includes seeded-user board-heavy read/write load mix and threshold-based regression diagnostics

## Manual Verification

Use `docs/MANUAL_TEST_CHECKLIST.md` for action-by-action manual validation.
Use `docs/OBSERVABILITY_BASELINE.md` for telemetry dashboard/alert baseline and observability smoke validation.

## Thesis Alignment Validation (Capture Realignment)

This section defines validation expectations for the capture-first direction.

Current state:
- capture MVP loop is shipped end-to-end (`#200` to `#211`)
- capture loop assertions below are required baseline checks for regression safety

Required assertions:
- capture action is fast and deterministic (target under 10 seconds to persisted artifact in normal local conditions)
- triage path stays proposal-first (no direct board mutation from model output)
- provenance links are visible from proposal/card surfaces back to capture source
- error and auth contracts remain stable (`ApiErrorResponse`, `401/403/404` policy)

Recommended execution pairing:
- automated: API + frontend unit + E2E capture loop (`#210` delivered, retained as active regression path)
- manual: capture friction/trust checks in `docs/MANUAL_TEST_CHECKLIST.md`

## Development Sandbox Mode

For local development only, authorization bypass can be enabled via:
- `backend/src/Taskdeck.Api/appsettings.Development.json`
- `DevelopmentSandbox.Enabled = true`

Safety boundary:
- Sandbox bypass is forced off outside Development environment.
- Validation and data integrity rules still apply.
