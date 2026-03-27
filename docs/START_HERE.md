# Start Here

This is the first doc to read if you are new to Taskdeck.

Taskdeck is a local-first execution workspace built around one simple loop:

1. capture something quickly
2. review the proposed change
3. apply it explicitly
4. continue the work on a board

The shipped product now teaches that loop from inside the workspace:

- `Home` is the default landing page
- `Today` keeps the daily agenda and first-run setup visible
- `Review` is the normal place to approve, reject, and execute proposed changes
- `Boards` remains the place where work lands and moves forward
- `Inbox` is where messy input becomes reviewable work

## What Taskdeck Is Good At Right Now

Taskdeck is strongest today as:

- a safe execution workspace for developers, operators, and small teams
- a place to capture rough work without losing it
- a review-first board system where automation proposes changes instead of mutating silently

Taskdeck is not yet:

- a broad autonomous project manager
- a finished `Agents` or `Integrations` workspace
- a replacement for human review

## Core Vocabulary

- `Home`: reset point for the product loop and the fastest place to see what needs attention
- `Today`: daily agenda for overdue work, blocked work, and review follow-through
- `Inbox`: low-friction intake for messy notes, bugs, and follow-ups
- `Review`: trust gate where proposed changes stop before they touch a board
- `Boards`: current shipped board surface. In product language, think of these as projects
- `Workbench tools`: advanced surfaces such as `Chat`, `Activity`, `Ops`, `Access`, and `Archive`
  - in Workbench mode, all shipped advanced surfaces appear in the nav by default
  - in Guided mode, some advanced surfaces remain opt-in via feature-flag toggles in `Settings`

## Pick A Workspace Mode

All three workspace modes keep the same review-first trust model. They change how prominent the advanced tools are.

- `Guided`
  - best for normal daily use
  - keeps `Home`, `Today`, `Review`, `Boards`, and `Inbox` front and center
- `Workbench`
  - best for power users and operators
  - shows the full shipped workspace in the nav, including `Chat`, `Notifications`, `Settings`, `Preferences`, `Activity`, `Ops`, `Access`, and `Archive`, without requiring feature flags for shipped surfaces
- `Agent`
  - preserves the same shipped product loop today
  - reserves the mental model for later `Agents` / `Runs` / `Knowledge` work without claiming those pages already exist

## First 10 Minutes

If you want the shortest path to first value:

1. Sign in and land on `Home`.
2. If you do not have a board yet, start setup from `Home` or `Today`.
3. Create a blank board or use a starter workflow such as `Engineering sprint`, `Support triage`, or `Content calendar`.
4. Drop one messy note into quick capture or `Inbox`.
5. Start triage, then open `Review`.
6. Approve and execute the proposal only after it looks right.
7. Continue the resulting work in the board.

That is the current golden path:

`Home -> Inbox/capture -> Review -> Board`

`Today` sits beside that path as the daily reset surface when you already have work underway.

## Which Page Should I Use?

Use `Home` when:

- you are unsure where to begin
- you want a quick summary of captures, pending review, and recent boards
- you want to restart setup or replay the guided onboarding steps

Use `Today` when:

- you want to shape the day
- you need to see overdue, due-today, or blocked work
- you want the next recommended action without route-hunting

Use `Inbox` when:

- the input is messy
- you want to save it now and structure it later
- you are not ready to decide the board or card shape yet

Use `Review` when:

- you need to inspect a proposed change before it touches a board
- you want the normal approval or rejection path

Use `Boards` when:

- you are doing the actual work
- you need to move cards, update details, or collaborate in comments

## If A Page Looks Empty

- `Home`: create the first board or replay setup
- `Today`: finish or replay the onboarding path so the loop has real work to show
- `Inbox`: add a capture or quick note
- `Review`: triage something first so a proposal exists to review
- `Boards`: create a board or use a starter pack

## Advanced Surfaces

The following are shipped, but they are not the normal first-run path:

- `Chat`
- `Activity`
- `Notifications`
- `Ops`
- `Access`
- `Archive`

If you are evaluating Taskdeck for the first time, stay in `Home`, `Today`, `Inbox`, `Review`, and `Boards` until the loop makes sense.

## Seeded Evaluation Path

If you want a populated workspace instead of starting mostly empty:

```bash
cd frontend/taskdeck-web
npm run demo:seed
```

Use this when:

- you are evaluating the product quickly
- you want event-driven pages such as notifications or activity to have realistic content
- you need a repeatable walkthrough state

For seeded demos and stakeholder walkthroughs, use [product/DEMO_PLAYBOOK.md](product/DEMO_PLAYBOOK.md).

## Next Docs

- [USER_MANUAL.md](USER_MANUAL.md) for the full shipped-product reference
- [product/FIRST_RUN_WORKFLOWS.md](product/FIRST_RUN_WORKFLOWS.md) for step-by-step user journeys
- [product/HELP_AND_FAQ.md](product/HELP_AND_FAQ.md) for page-level help and common confusion points
- [product/DOGFOODING_GUIDE.md](product/DOGFOODING_GUIDE.md) for daily internal use
- [product/DEMO_PLAYBOOK.md](product/DEMO_PLAYBOOK.md) for seeded demos and stakeholder walkthroughs
