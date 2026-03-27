# Taskdeck Demo Playbook

Taskdeck has a lot of capability under the hood. If you click around a fresh instance, some pages look empty because they are event-driven and only populate after specific flows.

This playbook gives you:

1. A one-command seed so the UI starts populated.
2. Scenario harness commands for repeatable demos.
3. A short stakeholder flow and an opt-in recorder.

Use [START_HERE.md](../START_HERE.md) first if you are trying to understand the product.
This playbook is for seeded demos, stakeholder walkthroughs, and regression/operator use, not the main onboarding path.

Core story:

Capture -> Triage -> Proposal -> Apply -> Board

Saul-facing recording contract:
- `docs/product/SAUL_DEMO_REHEARSAL_CONTRACT.md`

## Quick Start

1. Start backend

```bash
cd backend/src/Taskdeck.Api
dotnet run
```

2. Start frontend

```bash
cd frontend/taskdeck-web
npm install
npm run dev
```

Default URLs:

- API: `http://localhost:5000/api`
- UI: `http://localhost:5173`
- Local fallback ports for UI: `http://localhost:4173`, `http://localhost:5001`

Health-check endpoints (note: these are **not** under the `/api` prefix):

- `http://localhost:5000/health/live` — liveness probe
- `http://localhost:5000/health/ready` — readiness probe

3. Seed baseline demo data

```bash
cd frontend/taskdeck-web
npm run demo:seed
```

The seeder creates demo users, boards, Inbox items, proposals, queue activity, notifications, and Ops logs.
On reruns against the canonical demo account, it now reuses the seeded artifacts it can identify instead of appending a fresh copy of every capture, queue sample, comment, chat session, and Ops evidence item.

Use `npm run demo:seed -- --reset` to delete all demo boards before seeding (clean start).
Use `npm run demo:seed -- --help` for full usage information.

### Database location

The canonical dev database is `backend/src/Taskdeck.Api/taskdeck.db` (SQLite, created by EF Core migration on first backend startup). The connection string is `Data Source=taskdeck.db` in `appsettings.json`, resolved relative to the backend's working directory.

To reset the database without `--reset` (which only deletes demo boards via the API):

```bash
cd frontend/taskdeck-web
npm run demo:reset-db          # delete canonical dev DB
npm run demo:reset-db -- --all # also delete e2e/demo/ci DB files
```

Then restart the backend — EF Core will recreate the DB from migrations.

Other DB files in the repo are per-purpose:
- `taskdeck.e2e*.db` — E2E test databases (Playwright)
- `taskdeck.demo*.db` — demo director/CI databases
- `backend/tests/**/taskdeck.db` — backend test databases (created by test runs)
- `taskdeck.db` at repo root — created when the backend is started from the repo root (e.g. `dotnet run --project backend/src/Taskdeck.Api/...`). Safe to delete when the backend is stopped and your active dev DB is `backend/src/Taskdeck.Api/taskdeck.db`.

## Runtime Preconditions

- Demo scripts are local-safe by default. They target `http://localhost:5000/api` unless you override `TASKDECK_API_BASE_URL` or `TASKDECK_E2E_API_BASE_URL`.
- Non-local API targets are rejected unless you explicitly set `TASKDECK_DEMO_ALLOW_NON_LOCAL_API=1`.
- UI links and Playwright bootstrap default to `http://localhost:5173`; local fallback ports `4173` and `5001` are also supported.
- Demo harness credentials default to `demo` / `demo123` and `collab` / `demo123` unless you override the `TASKDECK_DEMO_*` / `TASKDECK_COLLAB_*` environment variables.
- Full Playwright-backed demos (`demo:director` or the opt-in stakeholder recorder) now auto-enable a live provider when LLM steps are enabled and a usable key is present.
- Gemini is preferred for full demos when `GEMINI_API_KEY`, `TASKDECK_DEMO_GEMINI_API_KEY`, or `Llm__Gemini__ApiKey` is set. Use `TASKDECK_DEMO_LLM_PROVIDER=OpenAI` to force OpenAI instead.
- Demo-specific live keys now take effect even when the base development environment is pinned to `Llm__Provider=Mock`; use `TASKDECK_DEMO_LLM_PROVIDER=Mock` or `TASKDECK_DEMO_DISABLE_LIVE_LLM=1` to force mock instead.
- When a full demo injects live-provider overrides, Playwright also disables existing-server reuse by default so the intended backend process is launched instead of silently inheriting a stale mock server.
- `taskdeck-chat` autopilot and scenario steps marked `requiresLlm: true` still need a usable live-provider key. Use `--skip-llm` for deterministic local or CI runs.
- `demo:director` and the stakeholder recorder require Playwright Chromium (`npx playwright install chromium`) and write access to `frontend/taskdeck-web/demo-artifacts/`.
- `demo:director:smoke` also owns a dedicated Playwright/demo database (`frontend/taskdeck-web/taskdeck.demo.ci.db`) and forces fresh backend/frontend startup so repeated runs do not inherit local `taskdeck.e2e.db` state.
- In fresh-server mode, the director keeps `http://localhost:5000/api` when it is free and otherwise auto-selects a free local API port before starting the backend.
- Unknown scenario IDs now fail fast during director/recorder setup so autopilot and walkthrough selection do not silently target the engineering board by fallback.
- Director-specific flags must appear before `--`; anything after `--` is forwarded to Playwright unchanged. Unknown director flags now fail fast instead of being silently forwarded.

## Scenario Harness

Scenario reference: [SCENARIOS.md](SCENARIOS.md)

List scenarios:

```bash
cd frontend/taskdeck-web
npm run demo:run -- --list
```

Run scenarios:

```bash
npm run demo:run -- engineering-sprint
npm run demo:run -- support-triage
npm run demo:run -- content-calendar
npm run demo:run -- --clean engineering-sprint
npm run demo:run -- --clean --dry-run engineering-sprint
```

JSON-runner flags:

```bash
# skip default LLM-dependent steps and any step marked requiresLlm: true
npm run demo:run -- support-triage --skip-llm

# keep running after a failed step
npm run demo:run -- engineering-sprint --continue-on-error
```

Autopilot simulation:

```bash
npm run demo:autopilot -- --turns 5 --brain heuristic
```

Deterministic autopilot simulation (seeded):

```bash
npm run demo:autopilot -- --turns 5 --brain heuristic --rng-seed 42
```

Optional chat-driven autopilot (requires live provider setup):

```bash
npm run demo:autopilot -- --turns 5 --brain taskdeck-chat
```

Loop-specific autopilot runs:

```bash
npm run demo:autopilot -- --loop queue
npm run demo:autopilot -- --loop capture
npm run demo:autopilot -- --loop mixed
```

## 5-Minute Stakeholder Flow

Saul-facing default (recording path):

1. Home
- Confirm the product teaches `Inbox -> Review -> Board`.
- Open the `DEMO: Client Onboarding Demo` board path.

2. Inbox/Capture
- Show ACME capture lineage and the proposal handoff action.

3. Review
- Confirm review-first trust cues (`nothing changes until approval`).
- Show the proposal in business wording and apply deliberately.

4. Board
- Show the clean onboarding reveal on `DEMO: Client Onboarding Demo`.

Extended walkthrough (optional):

1. Boards
- Open `DEMO: Client Onboarding Demo`.
- Explain reviewed proposals are the mutation gate.

2. Inbox
- Show ignored and triaged items.
- Follow provenance from capture item to proposal.

3. Automations -> Proposals
- Show pending/applied proposals.
- Explain review-first safety and explicit operations.

4. Notifications
- Show mention and proposal outcome notifications.

5. Activity and Ops (optional)
- Show audit/activity events.
- Show seeded Ops runs/log entries.

## MVP Dogfooding Loop

1. Capture in Inbox.
2. Start triage.
3. Review in Proposals.
4. Approve and execute.
5. Continue board execution.

## Why Some Pages Start Empty

These surfaces are event-driven:

- `Activity` needs audit events.
- `Notifications` needs mentions/proposal outcomes.
- `Ops -> Logs` needs Ops runs.
- `Access` needs board-specific entries.

Use `npm run demo:seed` and/or `npm run demo:run` before a manual walkthrough.

## Feature Flags for Demos

`Activity`, `Ops`, `Access`, and `Archive` are default-off on first run.
Enable them in `Settings -> Feature Flags` when needed for walkthrough coverage.

## API Walkthrough (No UI)

Use:

- `demo/http/taskdeck-demo.http`

It is designed for VS Code REST Client and exercises register/login, board creation, capture triage, queue, proposals, and Ops templates.

## Stakeholder Recorder (Opt-In Playwright)

Spec:

- `frontend/taskdeck-web/tests/e2e/stakeholder-demo.spec.ts`

Skipped by default. Run only when explicitly requested.

PowerShell:

```powershell
$env:TASKDECK_RUN_DEMO='1'
cd frontend/taskdeck-web
npx playwright test tests/e2e/stakeholder-demo.spec.ts --headed
```

Bash:

```bash
TASKDECK_RUN_DEMO=1 npx playwright test tests/e2e/stakeholder-demo.spec.ts --headed
```

CI policy:

- `stakeholder-demo.spec.ts` remains opt-in only. Default Playwright CI lanes set `TASKDECK_RUN_DEMO=0` and do not execute the recorder.
- Use the deterministic smoke command below for explicit regression proof instead of adding the full recorder to required CI.

## Demo Director (Scenario -> Autopilot -> Recorder -> Artifacts)

For a one-command, repeatable stakeholder run (including artifacts), use:

```bash
cd frontend/taskdeck-web

# Full run with deterministic autopilot seed
npm run demo:director -- --scenario engineering-sprint --turns 18 --brain heuristic --loop mixed --rng-seed demo-1

# Saul-facing deterministic rehearsal artifacts (no autoplay turns)
npm run demo:director -- --output-dir ./demo-artifacts/saul-rehearsal --e2e-db ./taskdeck.demo.saul.db --reset-e2e-db --fresh-servers --scenario client-onboarding --skip-llm --turns 0 --rng-seed saul-rehearsal

# CI-style deterministic run without LLM-required steps
npm run demo:director -- --scenario engineering-sprint --turns 12 --skip-llm --rng-seed ci-1

# Headed run if you want to watch the clickthrough
npm run demo:director -- --scenario engineering-sprint --turns 10 --headed

# Forward Playwright flags after `--`
npm run demo:director -- --scenario engineering-sprint --turns 10 -- --project=chromium

# Deterministic smoke path used for explicit CI/manual regression proof
npm run demo:director:smoke
```

Artifacts are written to:

```text
frontend/taskdeck-web/demo-artifacts/run-<timestamp>/
  README.md
  run-summary.json
  snapshot.json
  trace.ndjson
  logs/
  screenshots/
  playwright/
```

`trace.ndjson` contains structured scenario/autopilot events and is useful for debugging failed demo runs.

`demo:director:smoke` writes to `frontend/taskdeck-web/demo-artifacts/ci-smoke/`, resets `frontend/taskdeck-web/taskdeck.demo.ci.db`, auto-selects a free local API port when `5000` is occupied, and disables Playwright server reuse so artifact upload paths and seeded board state stay stable across reruns.

If startup still fails because you forced conflicting overrides, the director now prints a remediation hint that points to `TASKDECK_E2E_API_BASE_URL` and `TASKDECK_E2E_FRONTEND_PORT`.

When LLM steps are enabled, the full director flow will automatically pass live-provider settings to the backend web server when a usable demo key is present. Smoke runs still stay deterministic because `--skip-llm` suppresses that auto-enable path.

If you override the board name (`--autopilot-board` or equivalent env), the recorder walkthrough now follows that same selected board instead of falling back to the scenario default board during the UI clickthrough.

## Demo CI Policy

- Required CI and nightly Playwright lanes stay focused on baseline product regressions and explicitly keep the stakeholder recorder off.
- `ci-extended.yml` exposes `demo-director-smoke` as an opt-in lane via `workflow_dispatch` or a PR labeled `automation` when the PR touches `.github/workflows/**`, `backend/**`, `frontend/**`, `deploy/**`, or `scripts/**`.
- Full demo walkthrough recording stays manual/headed by default; use `TASKDECK_RUN_DEMO=1` only when you intentionally want the recorder.

## Constraints

Treat these as advanced or diagnostic surfaces in MVP demos:

- Ops
- Activity
- Access
- Archive

Primary narrative remains capture-to-proposal with explicit review.
