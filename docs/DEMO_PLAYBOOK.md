# Taskdeck Demo Playbook

This playbook is for first-run demos and quick stakeholder walkthroughs.
It focuses on the current MVP loop:

Capture -> Triage -> Proposal -> Apply -> Board

## Quick Start

1. Start backend:

```bash
cd backend/src/Taskdeck.Api
dotnet run
```

2. Start frontend:

```bash
cd frontend/taskdeck-web
npm install
npm run dev
```

Default URLs:
- API: `http://localhost:5000/api`
- UI: `http://localhost:5173`

3. Seed demo data:

```bash
cd frontend/taskdeck-web
npm run demo:seed
```

The seeder creates demo users, demo boards, Inbox items, proposals, queue activity, notifications, and ops logs.
During cleanup, the seeder first ensures/reuses canonical demo boards, then archives extra active `DEMO:*` boards (soft-delete) that are outside the canonical set.

## 5-Minute Demo Flow

1. `Boards`
- Open `DEMO: Capture Loop` (final canonical name after seeding).
- Explain that board changes come from reviewed proposals.

2. `Inbox`
- Show ignored and triaged items.
- Open a triaged item and follow its provenance to a proposal.

3. `Automations -> Proposals`
- Show pending/applied proposals.
- Explain review-first execution.

4. `Notifications`
- Show mention and proposal outcome notifications.

5. `Activity` and `Ops` (optional)
- Show audit trail and seeded ops runs/logs.

## Why Some Pages Start Empty

Several pages are event-driven and only populate after actions occur:
- `Activity` needs audit events.
- `Notifications` needs mentions/proposal outcomes.
- `Ops -> Logs` needs ops runs.
- `Access` needs a board id and access entries.

Use `npm run demo:seed` before manual evaluation so these surfaces are populated.

## Current MVP Dogfooding Loop

1. Create or open a board.
2. Capture raw work in `Inbox`.
3. Run triage on capture items.
4. Review proposals in `Automations -> Proposals`.
5. Approve and apply changes.
6. Continue execution on the board.

## Constraints

Treat these as advanced surfaces for now:
- Ops
- Activity
- Access
- Archive

They are useful, but the core product narrative is the capture-to-proposal loop.
