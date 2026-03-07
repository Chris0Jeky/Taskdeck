# Taskdeck User Manual

If you are new to Taskdeck, read [START_HERE.md](START_HERE.md) first.
This manual is the reference for the current shipped product shape.

## What Taskdeck Is

Taskdeck is a capture-first execution workspace.
Its main loop is:

1. capture something quickly
2. structure or triage it into a proposal
3. review the proposal
4. apply it explicitly
5. work the resulting board/cards
6. observe what happened when you need trust, provenance, or history

Current product shape:
- the shipped shell still starts from `Boards`
- the current UI is closer to a workbench than a novice-first guided product
- planned `Home` and `Today` surfaces are roadmap work, not current UI

## Current Golden Path

The fastest current path to value is:

1. create or open a board
2. capture rough input into `Inbox`
3. start triage on that Inbox item
4. open `Automations -> Proposals`
5. review, approve, and execute the proposal
6. return to the board and work the cards

This is the core loop Taskdeck already supports well.

## Current Navigation Map

Core product surfaces:
- `Boards`
- `Inbox`
- `Automations -> Proposals`
- quick capture
- starter packs
- `Chat` when you want conversational board-scoped help

Trust and validation surfaces:
- `Notifications`
- `Activity`
- comments and mentions

Advanced/operator surfaces:
- `Queue`
- `Ops`
- `Access`
- `Archive`

Rule of thumb:
- if you are new, stay in `Boards`, `Inbox`, and `Automations -> Proposals`
- treat `Queue` and `Ops` as advanced surfaces unless you explicitly need them

## Planned Shell Direction (Not Yet Shipped)

The expanded MVP blueprint now fixes the intended shell contract even though the current UI has not shipped it yet.

Guided mode primary navigation:
- `Home`
- `Today`
- `Inbox`
- `Projects`
- `Review`
- `Settings`

Guided mode secondary navigation:
- `Notifications`
- `Archive`
- `Help`

Workbench mode primary navigation:
- `Home`
- `Projects`
- `Inbox`
- `Review`
- `Automations`
- `Activity`
- `Notifications`
- `Settings`

Workbench mode secondary navigation:
- `Ops`
- `Access`
- `Archive`
- `Integrations`

Agent mode primary navigation:
- `Home`
- `Agents`
- `Runs`
- `Knowledge`
- `Inbox`
- `Projects`
- `Review`
- `Integrations`
- `Settings`

Agent mode secondary navigation:
- `Activity`
- `Ops`
- `Archive`

These modes are intended to be display/routing preferences, not security boundaries.

Suggested vocabulary mapping:

| Internal concept | Guided label | Workbench label | Agent label |
|---|---|---|---|
| Board | Project | Board / Project | Project |
| Automation Proposal | Review item | Proposal | Proposal |
| LLM Queue | Advanced intake | Queue | Queue |
| Chat session | Assistant chat | Chat | Agent chat |
| Command run | Diagnostics | Ops run | Tool run |
| Capture item | Inbox item | Capture | Capture |

## Manual Structure

This file remains the single-file shipped-product reference.
The planned chapter split and in-app help mapping now live in [manual/README.md](manual/README.md) so the root docs stay focused.

## Choose The Right Surface

Use `Boards` when:
- you are doing the actual work
- you need to move cards, update details, or collaborate through comments

Use `Inbox` when:
- the input is messy
- you want to save it now and structure it later
- you do not want to decide the board/card shape yet

Use `Review` (`Automations -> Proposals` today) when:
- you need the review boundary
- you want to inspect what Taskdeck is about to change

Use `Chat` when:
- you want a conversational workflow
- you want board-scoped assistance without dropping to the raw Queue path

Use `Queue` only when:
- you already know the explicit instruction flow
- you are doing a power-user, debugging, or demo-operator task

Use `Ops` when:
- you are diagnosing behavior
- you need logs, endpoint exploration, or operator tooling

## Getting Started

1. Register or log in.
2. Open `Boards`.
3. Create a board.
4. Optionally apply a starter pack from board settings.
5. Create one Inbox item and run the golden path above.

If you want a richer first run, seed the demo workspace from `frontend/taskdeck-web`:

```bash
npm run demo:seed
```

## Daily Use Rhythm

Morning:
- triage Inbox
- review pending proposals
- choose 1 to 3 cards for active work

During work:
- capture follow-ups immediately instead of holding them in your head
- use comments to preserve context on cards
- return to the board when proposals create or update work

End of day:
- move cards forward honestly
- capture loose ends into Inbox
- avoid leaving important context only in local notes or memory

## Boards And Cards

Boards:
- contain columns, cards, labels, comments, and board settings
- can be archived and restored
- are where work should feel visible and actionable

Board settings include:
- name and description
- archive/restore
- starter packs
- access control

Cards support:
- title and description
- due date
- labels
- blocked state and blocked reason
- threaded comments and mentions

`@username` mentions create notifications for the mentioned user.

Starter packs:
- scaffold columns, labels, and optional seed cards
- are safe to reapply because apply behavior is idempotent and conflict-aware

## Inbox And Review

Use `Inbox` for:
- notes
- bugs
- follow-ups
- rough plans
- ideas you do not want to lose

Inbox actions:
- `Ignore` for noise or duplicates
- `Start Triage` to request a reviewed proposal

Proposal review:
- happens in `Automations -> Proposals`
- is the primary trust boundary for board mutation
- should answer: what changes, where, and why

Current review model:
1. proposal is generated
2. user reviews operations
3. user approves or rejects
4. user executes explicitly

## Advanced Automation And Operator Surfaces

### Chat

Use Chat when:
- you want to discuss a task or plan conversationally
- you want board-scoped assistance
- you want checklist/bootstrap behavior for a board-scoped flow

### Queue

Queue is an advanced instruction ingestion surface.
It is not the recommended first-run path.

Use Queue when:
- you are intentionally issuing direct instructions
- you want a narrow, explicit automation request

Current limitation:
- Queue is still intentionally more system-shaped than Inbox or Chat
- use board selectors/context when available and treat any raw-ID requirement as an advanced workaround, not the normal user journey

### Ops

Ops includes:
- CLI runner
- endpoint explorer
- logs

Use Ops for diagnostics and operational workflows, not for ordinary daily task capture.

## Trust And Visibility

### Notifications

Notifications surface:
- mentions
- proposal outcomes
- other user-targeted events

### Activity

Activity provides:
- audit-style history
- board/entity/user exploration
- visibility into what changed and when

These surfaces support trust.
They should explain the system, not replace the main board workflow.

## Settings And Advanced Management

Profile:
- account identity details

Feature flags:
- enable advanced surfaces such as `Activity`, `Ops`, `Access`, and `Archive` when needed

Access:
- board membership and role assignment

Archive:
- archived-board management and restore flows

## Demo And Testing Workflows

From `frontend/taskdeck-web`:
- `npm run demo:seed` seeds a reusable baseline workspace
- `npm run demo:run -- --list` lists scenarios
- `npm run demo:run -- engineering-sprint` runs one scenario
- `npm run demo:autopilot -- --turns 5 --brain heuristic` simulates activity
- `npm run demo:director:smoke` runs the deterministic smoke/demo regression path

For direct API walkthroughs:
- use `demo/http/taskdeck-demo.http` with the VS Code REST Client

For the full demo/operator path:
- see [DEMO_PLAYBOOK.md](product/DEMO_PLAYBOOK.md)
- see [SCENARIOS.md](product/SCENARIOS.md)

## Troubleshooting

If `Notifications` is empty:
- you may not have any mentions or proposal-outcome events yet

If `Activity` is empty:
- you may not have enough board/entity events yet for the selected scope

If triage does not produce a proposal:
- check provider setup and current model/runtime configuration
- confirm the capture item is still in a triageable state

If Queue feels too technical:
- use `Inbox` or `Chat` instead unless you explicitly need the advanced instruction flow

If you are unsure where to start:
- create one board
- create one Inbox item
- run the review/apply loop once

If you expected `Home` or `Today`:
- those are planned roadmap surfaces, not current shipped routes
- today the intended starting point is still `Boards` plus the Inbox/review loop

## Current Constraints

- the current shell still starts from `Boards`; dedicated `Home` and `Today` pages are not shipped yet
- some advanced flows still expose internal scope concepts more than a novice-ready product should
- automation parsing remains pattern-based and board-centric
- review-first behavior is intentional; destructive autonomy is out of scope
