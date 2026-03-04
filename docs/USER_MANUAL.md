# Taskdeck User Manual

This manual covers daily usage across Boards, Inbox, Automations, Ops, Activity, and Notifications.

## Core Concepts

Boards
- A board contains columns, cards, labels, and comments.
- Boards can be archived and restored.

Columns
- Represent workflow stages (for example `Backlog -> In Progress -> Done`).

Cards
- Work items with title, description, labels, due date, blocked state/reason, and comments.

Inbox (Capture)
- Stores raw inputs.
- Items can be ignored or triaged into proposals.

Automation Proposals
- Taskdeck is proposal-first.
1. Proposal is generated.
2. User reviews operations.
3. User approves.
4. User executes.

## Getting Started

1. Register/login.
2. Create a board in `Boards`.
3. Optionally apply starter packs from board settings.
4. Capture tasks in `Inbox`.
5. Triage and execute reviewed proposals.

## Boards

Create/open boards
1. Open `Boards`.
2. Click `+ New Board`.
3. Open a board card.

Board settings
- Name and description
- Archive/unarchive
- Starter packs
- Access control

Starter packs
- Scaffold columns, labels, and optional seed cards.
- Safe to reapply due to dedupe/idempotency behavior.

## Cards

Create cards
- Use `Add Card` in a column.

Edit cards
- Title, description, due date, labels, blocked reason.

Move cards
- Drag-and-drop between columns.
- Or use Queue/Chat proposals.

Comments and mentions
- `@username` mentions generate notifications for mentioned users.

## Inbox

Use Inbox for:
- meeting notes
- bugs
- follow-ups
- ideas

Actions:
- Ignore: remove noise/duplicates.
- Start Triage: produce a reviewed proposal.

## Automations

Proposals
- Central review surface.
- Inspect operations and approve/reject/execute.

Queue
- Advanced instruction ingestion.
- Keep `requestType` as `instruction`.
- Provide `Board ID` for board-scoped operations.

Examples:
- `create card "Write demo script"`
- `rename board to "Sprint 14"`
- `move column "Backlog" to position 1`
- `move card <cardId> to column "Done"`

Chat
- Conversational workflow.
- Board-scoped chat can request proposal generation.

## Ops

Ops includes:
- CLI Runner (`/workspace/ops/cli`)
- Endpoint Explorer
- Logs

Use Ops for diagnostics and operational workflows.

## Activity and Notifications

Activity
- Audit-style history for user and board events.

Notifications
- User-targeted events, including mentions and proposal outcomes.

## Settings

Profile
- Account identity details.

Feature flags
- Enable advanced surfaces (`Activity`, `Ops`, `Access`, `Archive`) when needed.

Access
- Board membership and role assignment.

## Demo and Testing Workflows

From `frontend/taskdeck-web`:
- `npm run demo:seed` seeds a full baseline workspace.
- `npm run demo:run -- --list` lists scenario modules.
- `npm run demo:run -- engineering-sprint` runs one scenario.
- `npm run demo:autopilot -- --turns 5 --brain heuristic` simulates realistic activity.

For direct API walkthroughs:
- Use `demo/http/taskdeck-demo.http` with VS Code REST Client.

## Current Constraints

- Automation parsing is pattern-based and board-centric.
- Some advanced surfaces are hidden by default feature flags on first run.
- Review-first behavior is intentional; destructive autonomy is out of scope.

