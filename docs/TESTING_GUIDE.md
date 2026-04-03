# Testing Guide

This is the active testing guide for Taskdeck.

Last Updated: 2026-04-03
Companion Active Docs:
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/TESTING_GUIDE.md`
- `docs/MANUAL_TEST_CHECKLIST.md`
- `docs/GOLDEN_PRINCIPLES.md`

## Current Verified Totals (2026-04-02)

- Backend: 2053/2053 passing
  - Domain: 357
  - Application: 1260
  - API integration: 424
  - CLI contract: 4
  - Architecture boundaries: 8
- Frontend unit: 1496/1496 passing (134 test files)
- Frontend E2E (smoke + automation/ops + capture loop + starter-pack fixtures + concurrency harness): default required lane passing
- Combined automated total: 3549+ passing (backend 2053 + frontend unit 1496 + E2E)

Verification note:
- backend totals were re-verified on 2026-04-02 via `dotnet test backend/Taskdeck.sln -c Release -m:1`
- frontend unit totals were re-verified on 2026-04-02 via `npx vitest --run`
- significant test growth since 2026-03-29 due to: LLM tool-calling orchestrator and read tools (PR #669, 67 new backend tests), GDPR data portability and account deletion (PR #666, 25 new backend tests), board metrics dashboard (PR #667, 12 backend + frontend tests), MCP prototype board resources (PR #664, 7 new backend tests), GitHub OAuth frontend integration (PR #668, 5 new frontend tests), and UX-19 review card improvements

## Product-Coherence Testing Priorities (2026-03-07)

Testing priorities have shifted from "does the harness exist?" toward "does the product remain understandable under change?"

Near-horizon priorities:

- protect the current golden path: capture -> triage -> review -> execute -> board
- keep the deterministic first-run Playwright guardrail aligned to the shipped `Home -> capture -> review -> execute -> board` loop (`#328`, delivered)
- add explicit coverage for action-oriented empty states and board-centered context travel as those surfaces land
- keep stakeholder/demo recording opt-in; it supports product evidence, but it is not the primary product smoke

High-signal additions and delivered guardrails:

- `Home` view state coverage
- `Today` view state coverage
- workspace mode navigation rendering
- proposal summary card coverage
- board action rail coverage
- first-run golden-path Playwright smoke coverage, now delivered as the required regression guardrail in `#328`

Telemetry and release-gate follow-through from the expanded blueprint:

- product telemetry/event taxonomy remains tracked in `#341` with reuse of `#77`, while `#328` now provides the delivered first-run guardrail baseline
- keep event names privacy-safe and product-shaped (for example `home_loaded`, `today_loaded`, `capture_created`, `proposal_opened`, `proposal_approved`, `board_action_capture_here_clicked`, `workspace_mode_changed`, `agent_run_started`, `agent_run_completed`, `agent_run_failed`)
- treat launch framing as evidence gates, not marketing labels:
  - `R1` novice-first beta -> coherent `Home -> capture -> review -> execute -> board` path
  - `R2` agent foundation alpha -> inspectable runs, policies, and bounded templates
  - `R3` knowledge/integrations alpha -> durable searchable context plus supervised connector flows

## Codex Coverage Wave (TST-CODEX-01 to TST-CODEX-15, delivered 2026-03-28)

A dedicated test-coverage wave designed for token-efficient agents (Codex, lightweight LLM runners). Each task is self-contained with pattern files, source paths, and verify commands in `docs/codex-tasks/`.

Tracked issues: `#415` to `#429`. PRs: `#436` to `#448`. All delivered and merged 2026-03-28 after adversarial review pass with fixes for tautological assertions, missing guard branches, and edge-case gaps.

| Tier | Tasks | Scope | Issues |
|------|-------|-------|--------|
| 1 — Frontend API | labelsApi, columnsApi, usersApi | Mock HTTP, verify URL/payload | `#415`-`#417` |
| 2 — Frontend Composables | useErrorMapper, useEscapeToClose, useShortcutContext | Pure function + lifecycle tests | `#418`-`#420` |
| 3 — Frontend Stores | auditStore, queueStore (real coverage, not demo) | Pinia store with mocked API | `#421`-`#422` |
| 4 — Backend Domain | CardComment, Notification, AutomationProposal, LlmUsageRecord | Entity construction + invariants | `#423`-`#426` |
| 5 — Backend Services | OutboundWebhookSignature (expand), WorkerHeartbeatRegistry, CompositeBoardRealtimeNotifier | Service tests with mocking | `#427`-`#429` |

Remaining coverage gaps (post-wave, now tracked in TST-32 to TST-57 wave `#721`):
- Frontend: 1 API module untested (captureApi), remaining composables/stores have baseline coverage → tracked in `#711`, `#716`
- Backend: Infrastructure repositories partially covered (7 classes, 77 tests in `#699`/`#730`; remaining repos untested); remaining domain entities untested → tracked in `#701`; 1 of 5 workers untested → tracked in `#700`

## LLM Tool-Calling Coverage (PR #669, delivered 2026-04-01)

Tracking issue: `#649` (Phase 1 of `#647`)

New test coverage:
- `ToolCallingChatOrchestratorTests`: multi-turn loop, timeout, max-round enforcement
- `ReadToolSchemasTests`: schema generation for all 5 read tools
- `MockLlmProviderToolCallingTests` / `MockToolCallDispatcherTests` / `MockToolResultsTests`: mock provider tool-calling dispatch and result formatting
- `OpenAiToolCallingParseTests` / `GeminiToolCallingParseTests`: provider-specific tool-call response parsing

Manual validation recommended: send "What cards are in my Backlog?" via chat with Mock provider and verify dynamic tool-calling response.

## MCP Server Coverage (PR #664, delivered 2026-04-01)

Tracking issue: `#652` (Phase 1 of `#648`)

New test coverage:
- `McpBoardResourcesTests`: `taskdeck://boards` resource listing, phantom-user fallback, multi-user board scoping

Manual validation recommended: configure `mcp.example.json` in Claude Code / Cursor and ask "What boards do I have?" to verify resource delivery.

## GDPR Data Portability Coverage (PR #666, delivered 2026-04-01)

Tracking issue: `#83`

New test coverage:
- `DataExportServiceTests` (10 tests): user-scoped data export completeness, versioned payload shape, cross-user isolation
- `AccountDeletionServiceTests` (15 tests): password re-auth, confirmation phrase enforcement, PII anonymization, audit ref cleanup, deactivated-user login rejection

## Board Metrics Coverage (PR #667, delivered 2026-04-01)

Tracking issue: `#77`

New test coverage:
- `BoardMetricsServiceTests` (12 backend tests): board-scoped metric aggregation, date range filtering, label grouping
- `metricsApi.spec.ts` (4 frontend tests): API client mock verification

## GitHub OAuth Frontend Coverage (PR #668, delivered 2026-04-01)

Tracking issue: `#539`

New test coverage:
- `authApi.spec.ts` (3 tests): `getProviders` and `exchangeOAuthCode` API calls
- `sessionStore.spec.ts` (2 tests): OAuth code exchange store action

## Rigorous Test Expansion Wave (TST-32 to TST-57, seeded 2026-04-03)

Tracker issue: `#721`. Seeded from a systematic codebase audit across backend, frontend, and cross-cutting integration boundaries.

Security finding during audit: `#722` (SEC-20) — `ChangePassword` endpoint does not verify caller identity.

### Wave Scope

22 issues spanning integration tests, edge cases, adversarial inputs, failure modes, and cross-user data isolation. Focus is on integration seams (where services interact) rather than adding more isolated unit tests.

| Priority | Issues | Theme |
|----------|--------|-------|
| I | `#703` | Capture → triage → proposal → review → board end-to-end golden path |
| II | ~~`#699`~~, `#700`, `#702`, `#704`, `#705`, `#707`, `#723`, `#725` | Infrastructure repos (~~`#699` delivered~~), untested worker, controller gaps, data isolation, concurrency, auth, OAuth, frontend HTTP interceptor |
| III | `#701`, `#706`, `#708`, `#709`, `#710`, `#711`, `#712`, `#713`, `#714`, `#715`, `#716`, `#718`, `#719`, `#720`, `#726` | Domain state machines, SignalR, proposal lifecycle, LLM tool-calling, webhooks, frontend stores/views, export/import, error contracts, archive, metrics, notifications, resilience |
| IV | `#717` | Property-based and adversarial input tests (extends `#89`) |

### Key Gaps Identified

- **Infrastructure repositories**: 25+ repositories with raw SQL, GUID formatting, and in-memory pagination — partially addressed: 7 classes now have 77 integration tests against real SQLite (`#699`/`#730`); remaining repositories still untested
- **`LlmQueueToProposalWorker`**: most complex worker (fair-batch interleaving, optimistic concurrency, retry/backoff) with zero tests; every capture flows through it
- **Cross-user data isolation**: after `#508` (queue data leak), no systematic integration test proves data isolation across all surfaces
- **Frontend HTTP interceptor and router auth guard**: crossed by every request, zero test coverage
- **Golden path**: no single integration test exercises capture → proposal → review → board as a connected pipeline

### Relationship to Existing Test Issues

- Extends `#254` (testing harness improvement wave, delivered)
- Extends `#89` (property/fuzz pilot, delivered)
- Complements `#90` (mutation testing pilot)
- Complements `#91` (Testcontainers for isolation)
- Feeds into `#135` (integrated multi-component verification program)

### Delivered: Infrastructure Repository Integration Tests (`#699`/`#730`)

First delivery from the rigorous test expansion wave. 77 integration tests across 7 repository classes running against real SQLite (not mocks or in-memory substitutes).

Pattern:
- Each test class creates a fresh SQLite database via `DbContextOptionsBuilder<TaskdeckDbContext>` with a unique filename
- Tests exercise actual EF Core queries, GUID formatting, ordering, pagination, and filtering against real SQLite behavior
- Database is cleaned up after each test run

Key findings:
- Found and fixed a real `LlmQueueRepository` ordering bug where queue items were not returned in the expected FIFO order
- Confirmed correct behavior for raw SQL queries, in-memory pagination edge cases, and GUID string formatting across repositories

Coverage:
- 7 repository classes tested (including `LlmQueueRepository`, `BoardRepository`, `CardRepository`, and others)
- Tests validate query correctness, cross-user isolation, empty-result handling, and ordering guarantees

This establishes the pattern for testing remaining infrastructure repositories tracked in the wave (`#721`).

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
- If backend tests unexpectedly bind to a live LLM provider in local Development, force deterministic mock mode before running the suite:
  - PowerShell: `$env:Llm__EnableLiveProviders='false'; $env:Llm__AllowLiveProvidersInDevelopment='false'; $env:Llm__Provider='Mock'; dotnet test backend/Taskdeck.sln -c Release -m:1`

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

Frontend local dev server (manual workflows):

```bash
cd frontend/taskdeck-web
npm run dev
```

Notes:
- `npm run dev` now auto-resolves frontend port with fallback order `5173` -> `4173` -> `5001` when a port is restricted or unavailable.
- launcher now selects a bindable port first; occupied candidate ports (including existing Taskdeck listeners) are skipped for new Vite processes.
- launcher now applies strict-port startup semantics by default to avoid Vite auto-increment drift.
- explicit overrides remain supported (for example `npm run dev -- --host localhost --port 5001` or `TASKDECK_DEV_PORT=5001 npm run dev`).
- backend Development CORS defaults include localhost fallback ports (`4173`, `5001`) so login/API calls stay aligned when fallback startup is used.

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

Fallback (force an alternate frontend port):

PowerShell:

```powershell
cd frontend/taskdeck-web
$env:TASKDECK_E2E_FRONTEND_PORT='5001'
$env:TASKDECK_E2E_API_CORS_ORIGINS='http://localhost:5001'
npx playwright test --reporter=line
```

Bash:

```bash
cd frontend/taskdeck-web
TASKDECK_E2E_FRONTEND_PORT=5001 TASKDECK_E2E_API_CORS_ORIGINS='http://localhost:5001' npx playwright test --reporter=line
```

Optional E2E env overrides (Playwright config):
- `TASKDECK_E2E_FRONTEND_HOST` (default `localhost`)
- `TASKDECK_E2E_FRONTEND_PORT` (when unset, config auto-probes `5173`, then `4173`, then `5001`)
- `TASKDECK_E2E_FRONTEND_BASE_URL` (default `http://{host}:{port}`; must be `http://` with explicit port and no path/query/hash)
- `TASKDECK_E2E_API_BASE_URL` (default `http://localhost:5000/api`; must be `http://` with explicit port and API path)
- `TASKDECK_E2E_API_CORS_ORIGINS` (comma-separated additional origins merged with defaults: frontend origin plus `http://localhost:5174`; each value is passed to backend process as `Cors__DevelopmentAllowedOrigins__{index}`)
- `TASKDECK_E2E_REUSE_EXISTING_SERVER` (defaults to `true` locally and `false` in CI; full demo runs that inject live-provider backend overrides also switch reuse off by default so the intended backend process is actually launched; set `0` to force fresh backend/frontend startup or `1` to force reuse intentionally)

Override behavior notes:
- backend Playwright `webServer` readiness URL is derived from `TASKDECK_E2E_API_BASE_URL` as `{apiBaseUrl}/boards`
- backend Playwright process startup binds to the same API origin via `ASPNETCORE_URLS`
- backend Playwright startup now forces deterministic mock-provider mode by default; live-provider env is only injected for explicit demo runs (`TASKDECK_RUN_DEMO=1` / director path) when LLM steps are enabled

Troubleshooting note (Windows local environments):
- if Playwright startup fails with `listen EACCES` for the frontend port, keep `TASKDECK_E2E_FRONTEND_PORT` unset so auto-fallback can select the next bindable port.
- when auto-fallback is used, Playwright keeps runner/worker aligned by storing the first resolved fallback port in-process (`TASKDECK_E2E_RESOLVED_FRONTEND_PORT`) so worker-side config evaluation does not drift to a different fallback port after the frontend webServer starts.
- local reuse mode prefers identity-verified listeners; CI mode prefers bindable ports for first resolution.
- if you explicitly set `TASKDECK_E2E_FRONTEND_PORT`, use `TASKDECK_E2E_API_CORS_ORIGINS` when needed so API preflight requests stay aligned with the chosen frontend origin.
- investigation details and reproduction commands are documented in `docs/analysis/2026-02-25_frontend-gate-port-bind-and-cors-blockers.md`.

Run concurrency harness spec only:

```bash
cd frontend/taskdeck-web
npm run test:e2e:concurrency
```

Opt-in live-provider check (headed-friendly):

PowerShell:

```powershell
cd frontend/taskdeck-web
$env:TASKDECK_RUN_LIVE_LLM_TESTS='1'
npx playwright test tests/e2e/live-llm.spec.ts --headed --reporter=line
```

Headed manual-audit pack:

```powershell
cd frontend/taskdeck-web
npm run test:e2e:audit:headed
```

## Demo Tooling Policy

Default CI posture:

- Required Playwright regression lanes explicitly set `TASKDECK_RUN_DEMO=0`; the stakeholder recorder is never part of required CI.
- Load/concurrency Playwright coverage also keeps demo recording off by default so those lanes stay focused on product/runtime regressions.
- The deterministic demo regression command is `npm run demo:director:smoke`.
- Demo tooling remains supporting evidence for seeded workflows; it does not replace the required product smoke path.

Run the smoke path locally:

```bash
cd frontend/taskdeck-web
npm run demo:director:smoke
```

Policy notes:

- `demo:director:smoke` runs `engineering-sprint` with `--skip-llm`, zero autopilot turns, a fixed RNG seed, a stable artifact directory (`demo-artifacts/ci-smoke`), an isolated smoke DB (`taskdeck.demo.ci.db`), and fresh backend/frontend startup.
- when fresh-server mode cannot bind `http://localhost:5000/api`, the director automatically selects a free local API port; if explicit overrides still conflict, it prints a remediation hint for `TASKDECK_E2E_API_BASE_URL` / `TASKDECK_E2E_FRONTEND_PORT`.
- `ci-extended.yml` exposes a matching `demo-director-smoke` lane for explicit validation through `workflow_dispatch` or a PR labeled `automation` when the PR touches `.github/workflows/**`, `backend/**`, `frontend/**`, `deploy/**`, or `scripts/**`.
- `npm run demo:seed` is expected to be rerun-safe on the canonical demo account: seeded captures, queue examples, chat evidence, comments, and Ops logs should be reused when present instead of multiplying on every local/manual regression run.
- `demo:director` validates its own options before Playwright passthrough; keep director flags before `--` and pass raw Playwright arguments only after `--`.
- Full stakeholder walkthrough recording remains manual/headed via `TASKDECK_RUN_DEMO=1`.
- opt-in live-provider chat verification is now separate from demo mode: use `TASKDECK_RUN_LIVE_LLM_TESTS=1` when you want a real-provider probe without running the full stakeholder demo flow.

## Saul-Facing Rehearsal Contract

Canonical operator contract:
- `docs/product/SAUL_DEMO_REHEARSAL_CONTRACT.md`

Deterministic bootstrap for the Saul-facing story:

```bash
cd frontend/taskdeck-web
npm run demo:seed
npm run demo:run -- --clean --skip-llm client-onboarding
```

Deterministic artifact rehearsal bundle:

```bash
cd frontend/taskdeck-web
npm run demo:director -- --output-dir ./demo-artifacts/saul-rehearsal --e2e-db ./taskdeck.demo.saul.db --reset-e2e-db --fresh-servers --scenario client-onboarding --skip-llm --turns 0 --rng-seed saul-rehearsal
```

Acceptance focus for this rehearsal:
- prove `Home -> Inbox/Capture -> Review -> Board`
- prove review-first trust language is visible without narration
- prove ACME onboarding capture becomes clean board work after explicit approval

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

Deployment hardening matrix automation (PowerShell):

```powershell
powershell -File ./scripts/deploy/Verify-TaskdeckDeploymentHardening.ps1 -Port 8080
```

Hardening matrix pass/fail criteria:
- `docs/ops/DEPLOYMENT_HARDENING_MATRIX.md`

## Failure-Injection Drills

Repeatable failure-injection scenarios for deployment and MCP workflows:

```bash
bash scripts/drills/run-all-drills.sh        # local run
bash scripts/drills/run-all-drills.sh --ci    # CI-compatible with machine-readable output
```

Scenarios covered:
- Missing SQLite database at startup
- Locked SQLite database at startup
- Readiness-check timeout behavior
- MCP configuration validation / unknown-server handling
- Reverse-proxy misconfiguration regression

Drill documentation and recovery paths: `docs/ops/FAILURE_INJECTION_DRILLS.md`

## Terraform IaC Baseline Validation

Static validation (no cloud apply required):

```powershell
terraform fmt -check -recursive deploy/terraform/aws
powershell -File ./scripts/deploy/Test-TaskdeckTerraformBaseline.ps1
```

Real-environment drift check (requires environment-specific `terraform.tfvars`, backend config, and AWS credentials):

```powershell
powershell -File ./scripts/deploy/Invoke-TaskdeckTerraformDriftCheck.ps1 `
  -Environment staging `
  -VarFile deploy/terraform/aws/environments/staging/terraform.tfvars `
  -BackendConfigFile deploy/terraform/aws/environments/staging/backend.hcl `
  -RefreshOnly
```

Notes:
- `Test-TaskdeckTerraformBaseline.ps1` runs `terraform init -backend=false` and `terraform validate` for `dev`, `staging`, and `prod`.
- `Invoke-TaskdeckTerraformDriftCheck.ps1` relies on `terraform plan -detailed-exitcode`; `0` means no changes, `2` means drift for `-RefreshOnly` or planned changes for a non-refresh-only run, and any other exit is a failure.
- The Terraform baseline intentionally provisions the current single-node Docker deployment model; the JWT signing secret comes from a pre-created SecureString SSM parameter, and the SQLite path lives on a dedicated persistent EBS data volume so routine host replacement does not discard `/var/lib/taskdeck/taskdeck.db`.
- `staging` and `prod` default `protect_data_volume` to `true`; intentional destroys or migrations that must remove the data volume require a reviewed switch to the unprotected path plus a reviewed module-source change to relax/remove `prevent_destroy` before the destructive apply.
- Changing an existing environment from `protect_data_volume = false` to `true` also replaces the underlying EBS volume with a new protected one; treat that as a destructive migration and capture a backup or snapshot first.
- Staged rollout policy, managed DB, and full secret-rotation posture remain tracked in `#101`, `#84`, and `#110`.

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
- `demo-director-smoke`
  - opt-in on PRs labeled `automation` or manual `workflow_dispatch`; PR-triggered runs still require watched-path changes because `ci-extended.yml` does not include `docs/**`
  - runs the deterministic `demo:director:smoke` path via `reusable-demo-director-smoke.yml`

Nightly workflow: `.github/workflows/ci-nightly.yml`

- scheduled/manual backend solution regression (`dotnet test backend/Taskdeck.sln -c Release -m:1`)
- scheduled/manual E2E smoke suite (`reusable-e2e-smoke.yml`)
- scheduled/manual load-concurrency harness (`reusable-load-concurrency-harness.yml`)
- scheduled/manual container image regression
- `developer-portal`: builds API, fetches `/swagger/v1/swagger.json`, runs `@redocly/cli build-docs`, uploads `artifacts/developer-portal/` including docs from `docs/api/` (PR #658)

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
  - normalized dependency-security summary (`summary.md`, `summary.json`) linked to `docs/security/SECURITY_DEPENDENCY_VULNERABILITY_POLICY.md`

Triage usage:
- check workflow step summary first for signal exit codes
- inspect uploaded artifacts to differentiate command failures from dependency findings
- treat this lane as reporting-first; promote to stricter gating only through a dedicated follow-up issue/decision

Release/security workflow: `.github/workflows/release-security.yml`

- release/tag/manual dependency inventory artifact generation
- backend/frontend vulnerability signal capture
- manual strict-enforcement option that fails on unresolved high/critical findings, non-zero dependency scan exits, or unparseable scan outputs
- reusable container artifact/checksum lane for release-ready outputs

CI extended dependency-security lane:

- `.github/workflows/ci-extended.yml` now exposes an opt-in `Dependency Security Signals` job through manual dispatch or PRs labeled `security`
- this lane is reporting-first and uses the same normalized summary format as nightly/release flows

## Testing Harness Improvement Wave (Delivered 2026-02-24)

Tracking issues:
- wave tracker: `#254`
- delivered execution: `#255` to `#260`

Already-covered pack scenarios (no duplicate implementation issue required):
- WIP limit enforcement already covered across application/API/E2E.
- sandbox-gated database import/export rejection outside Development already covered.
- starter-pack idempotency/conflict safety already covered.

Knowledge transfer applied to existing seeds:
- `#89`: targeted property/fuzz pilot surfaces (manifest/query/import-export boundaries)
- `#90`: non-blocking scheduled mutation-lane posture
- `#106`: dependency/security signal command baseline (`dotnet list package --vulnerable`, `npm audit`)
- `#168`: CI topology routing for OpenAPI/nightly-quality lanes

Delivered outcomes:
- `#255` removed residual wall-clock flake vectors and centralized reusable E2E polling helpers
- `#256` locked drag/drop persistence after full reload into Playwright smoke coverage
- `#257` centralized representative `400/401/403/404/409` API error-contract assertions
- `#258` added OpenAPI generation + parse-validation artifacts in CI
- `#259` codified `docs/GOLDEN_PRINCIPLES.md` with lightweight mechanical enforcement
- `#260` added the non-blocking nightly-quality workflow for coverage and dependency/security signal artifacts

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
  - Includes LLM tool-calling orchestrator coverage (multi-turn loop, timeout, round limits) and read tool schema generation
  - Includes GDPR data export service (user-scoped completeness, versioned payload) and account deletion service (re-auth, confirmation phrase, PII anonymization)
  - Includes board metrics service coverage (aggregation, date range, label grouping)
  - Includes MCP board resource coverage (listing, phantom-user fallback, multi-user scoping)
- HTTP contracts and behavior mappings:
  - `backend/tests/Taskdeck.Api.Tests`
  - Includes core + automation/archive/chat/ops/log/health controllers
  - Includes rate-limit policy coverage (`RateLimitingApiTests`) for burst throttling, retry metadata contract, reset-window recovery, and cross-user boundary behavior
  - Includes security-header baseline coverage (`SecurityHeadersApiTests`) for success/auth-failure paths and HTTPS HSTS posture assertions
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
  - Includes GitHub OAuth API client and session store coverage (`authApi`, `sessionStore`)
  - Includes board metrics API client and store coverage (`metricsApi`, `metricsStore`)
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
Use `docs/ops/OBSERVABILITY_BASELINE.md` for telemetry dashboard/alert baseline and observability smoke validation.

Detailed step-indexed validation checklists:
- Slice A — workspace shell, board lifecycle, keyboard UX: `docs/testing/manual-validation-a-workspace-board-ux.md`
- Slice B — authz policy, cross-user isolation, API error contracts: `docs/testing/manual-validation-b-authz-contracts.md`

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

## Incident Rehearsals

Manual incident rehearsals complement automated tests by validating diagnosis and recovery workflows against realistic failure conditions. Rehearsals are scheduled monthly (lightweight, ~30 min) and quarterly (deep drill, ~2 hours).

Key resources:
- `docs/ops/INCIDENT_REHEARSAL_CADENCE.md` -- schedule, rotation, and process
- `docs/ops/rehearsal-scenarios/` -- scenario templates (health degradation, telemetry gaps, deployment failures)
- `docs/ops/EVIDENCE_TEMPLATE.md` -- evidence package format
- `docs/ops/REHEARSAL_BACKOFF_RULES.md` -- how rehearsal findings become tracked issues
- `docs/ops/rehearsals/` -- completed rehearsal evidence packages

Rehearsals are distinct from the automated failure-injection drill suite (`docs/ops/FAILURE_INJECTION_DRILLS.md`). Drills are scripted and CI-runnable; rehearsals are human-driven and focus on diagnosis speed, tooling gaps, and recovery muscle memory.

## Development Sandbox Mode

For local development only, authorization bypass can be enabled via:
- `backend/src/Taskdeck.Api/appsettings.Development.json`
- `DevelopmentSandbox.Enabled = true`

Safety boundary:
- Sandbox bypass is forced off outside Development environment.
- Validation and data integrity rules still apply.
