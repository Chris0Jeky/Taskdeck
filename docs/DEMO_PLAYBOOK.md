# Taskdeck Demo Playbook

Taskdeck has a lot of capability under the hood. If you click around a fresh instance, some pages look empty because they are event-driven and only populate after specific flows.

This playbook gives you:

1. A one-command seed so the UI starts populated.
2. Scenario harness commands for repeatable demos.
3. A short stakeholder flow and an opt-in recorder.

Core story:

Capture -> Triage -> Proposal -> Apply -> Board

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

3. Seed baseline demo data

```bash
cd frontend/taskdeck-web
npm run demo:seed
```

The seeder creates demo users, boards, Inbox items, proposals, queue activity, notifications, and Ops logs.

## Runtime Preconditions

- Demo scripts are local-safe by default. They target `http://localhost:5000/api` unless you override `TASKDECK_API_BASE_URL` or `TASKDECK_E2E_API_BASE_URL`.
- Non-local API targets are rejected unless you explicitly set `TASKDECK_DEMO_ALLOW_NON_LOCAL_API=1`.
- UI links and Playwright bootstrap default to `http://localhost:5173`; local fallback ports `4173` and `5001` are also supported.
- Demo harness credentials default to `demo` / `demo123` and `collab` / `demo123` unless you override the `TASKDECK_DEMO_*` / `TASKDECK_COLLAB_*` environment variables.
- `taskdeck-chat` autopilot and scenario steps marked `requiresLlm: true` need live provider configuration. Use `--skip-llm` for deterministic local or CI runs.
- `demo:director` and the stakeholder recorder require Playwright Chromium (`npx playwright install chromium`) and write access to `frontend/taskdeck-web/demo-artifacts/`.
- `demo:director:smoke` also owns a dedicated Playwright/demo database (`frontend/taskdeck-web/taskdeck.demo.ci.db`) and forces fresh backend/frontend startup so repeated runs do not inherit local `taskdeck.e2e.db` state.
- In fresh-server mode, the director keeps `http://localhost:5000/api` when it is free and otherwise auto-selects a free local API port before starting the backend.

## Scenario Harness

Scenario reference: [docs/SCENARIOS.md](SCENARIOS.md)

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
```

JSON-runner flags:

```bash
# skip steps marked requiresLlm: true
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

1. Boards
- Open `DEMO: Capture Loop`.
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

# CI-style deterministic run without LLM-required steps
npm run demo:director -- --scenario engineering-sprint --turns 12 --skip-llm --rng-seed ci-1

# Headed run if you want to watch the clickthrough
npm run demo:director -- --scenario engineering-sprint --turns 10 --headed

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

## Demo CI Policy

- Required CI and nightly Playwright lanes stay focused on baseline product regressions and explicitly keep the stakeholder recorder off.
- `ci-extended.yml` exposes `demo-director-smoke` as an opt-in lane via `workflow_dispatch` or a PR labeled `automation`.
- Full demo walkthrough recording stays manual/headed by default; use `TASKDECK_RUN_DEMO=1` only when you intentionally want the recorder.

## Constraints

Treat these as advanced or diagnostic surfaces in MVP demos:

- Ops
- Activity
- Access
- Archive

Primary narrative remains capture-to-proposal with explicit review.
