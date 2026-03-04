# Taskdeck Demo Playbook

This playbook provides a practical demo flow for Taskdeck's capture-first, review-first model.

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

3. Seed baseline demo data

```bash
cd frontend/taskdeck-web
npm run demo:seed
```

The seeder creates demo users, demo boards, Inbox items, proposals, queue activity, notifications, and Ops logs.
During cleanup, the seeder first ensures/reuses canonical demo boards, then archives extra active `DEMO:*` boards outside the canonical set.

## Scenario Harness (Batch B)

List scenarios:

```bash
cd frontend/taskdeck-web
npm run demo:run -- --list
```

Run a scenario:

```bash
npm run demo:run -- engineering-sprint
npm run demo:run -- support-triage
npm run demo:run -- content-calendar
```

Autopilot simulation:

```bash
npm run demo:autopilot -- --turns 5 --brain heuristic
```

Deterministic autopilot simulation (seeded):

```bash
npm run demo:autopilot -- --turns 5 --brain heuristic --seed 42
```

Optional chat-driven autopilot (requires live provider setup):

```bash
npm run demo:autopilot -- --turns 5 --brain taskdeck-chat
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

Use `npm run demo:seed` and/or `npm run demo:run` before manual walkthrough.

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

Skipped by default. Run only when explicitly requested:

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

## Constraints

Treat these as advanced/diagnostic surfaces in MVP demos:
- Ops
- Activity
- Access
- Archive

Primary narrative remains capture-to-proposal with explicit review.

