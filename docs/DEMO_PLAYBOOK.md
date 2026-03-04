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
- Legacy local fallback for UI: `http://localhost:4173`

3. Seed baseline demo data

```bash
cd frontend/taskdeck-web
npm run demo:seed
```

The seeder creates demo users, boards, Inbox items, proposals, queue activity, notifications, and Ops logs.

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

Compatibility note:

- If your branch does not expose JSON-runner flags yet, run the scenario command without those flags.

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

If your branch supports loop modes, you can also run:

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

## Constraints

Treat these as advanced or diagnostic surfaces in MVP demos:

- Ops
- Activity
- Access
- Archive

Primary narrative remains capture-to-proposal with explicit review.
