# Start Here

This is the first doc to read if you are new to Taskdeck.

Taskdeck is a local-first, capture-first execution workspace.
Its main idea is simple:

1. capture something quickly
2. turn it into a reviewed proposal
3. apply it explicitly
4. work the resulting board/cards

A fuller mental model is:

- `Capture`: save messy input fast
- `Structure`: triage it into proposals and board context
- `Review`: inspect what would change before apply
- `Execute`: work from boards/cards instead of from hidden automation
- `Observe`: use notifications/activity when you need evidence and history

## What Taskdeck Is Good At Right Now

Taskdeck is strongest today as:

- a safe execution workspace for developers and builders
- a place to capture rough work without losing it
- a review-first board system where automation proposes changes instead of mutating silently

Taskdeck is not yet a polished novice-first product shell.
The current shipped UI still starts from `Boards`, and the planned `Home` and `Today` surfaces are roadmap work, not current product routes.

## Key Terms

Use this vocabulary as the productization work lands:

- `Project`: the product-facing name for the board context. Today the UI still says `Boards`.
- `Inbox`: the low-friction intake surface for messy notes, bugs, and follow-ups.
- `Review`: the product-facing review surface. Today it lives under `Automations -> Proposals`.
- `Today`: the planned daily-agenda surface. It is not shipped yet.
- `Agents`: the planned supervised assistant surfaces. They are not part of the normal shipped starting path yet.

## Fastest Path To First Value

If you want the shortest real workflow:

1. start the backend and frontend
2. create or open a board
3. create one Inbox item or quick capture
4. start triage
5. open `Automations -> Proposals`
6. review, approve, and execute
7. return to the board and keep working there

That is the current golden path.

## Quick Local Start

Backend:

```bash
dotnet restore backend/Taskdeck.sln
dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj
```

Frontend:

```bash
cd frontend/taskdeck-web
npm install
npm run dev
```

Default URLs:

- API: `http://localhost:5000`
- Swagger: `http://localhost:5000/swagger`
- Frontend: `http://localhost:5173`

## If You Want A Better First Run

Seed the demo workspace so the app starts populated instead of mostly empty:

```bash
cd frontend/taskdeck-web
npm run demo:seed
```

Use this when:

- you are evaluating the product
- you want to understand event-driven surfaces faster
- you need a believable walkthrough state

If you want the full seeded demo/operator path, then read [DEMO_PLAYBOOK.md](product/DEMO_PLAYBOOK.md).

## Page Map

Core surfaces:

- `Boards`
- `Inbox`
- `Automations -> Proposals`
- quick capture
- starter packs
- `Chat` when you want conversational board-scoped help

Trust surfaces:

- `Notifications`
- `Activity`
- comments and mentions

Advanced/operator surfaces:

- `Queue`
- `Ops`
- `Access`
- `Archive`

Rule of thumb:

- if you are unsure where to begin, stay in `Boards`, `Inbox`, and `Automations -> Proposals`
- treat `Queue` and `Ops` as advanced tools, not the normal first-run path

## What To Click First

If the app is empty:

1. create a board
2. optionally apply a starter pack
3. create one capture or Inbox item
4. run triage
5. open the proposal
6. execute it

If the app is seeded:

1. open the demo board
2. open `Inbox`
3. follow a capture item into a proposal
4. execute the proposal
5. go back to the board and inspect the result

## If You Only Remember One Thing

Do not overthink the taxonomy on day one.

Use Taskdeck like this:

- capture now
- triage later
- review before apply
- work from the board

## Next Docs

- [USER_MANUAL.md](USER_MANUAL.md) for the current shipped product reference
- [DOGFOODING_GUIDE.md](product/DOGFOODING_GUIDE.md) for daily internal use
- [DEMO_PLAYBOOK.md](product/DEMO_PLAYBOOK.md) for seeded demos and stakeholder walkthroughs
- [TESTING_GUIDE.md](TESTING_GUIDE.md) for verification commands and test policy
