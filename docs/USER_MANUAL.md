# Taskdeck User Manual

If you are new to Taskdeck, read [START_HERE.md](START_HERE.md) first.
This manual is the reference for the current shipped product shape.

## What Taskdeck Is

Taskdeck is a capture-first, review-first execution workspace.
Its main loop is:

1. capture something quickly
2. shape it into a proposed change
3. review the proposal
4. apply it explicitly
5. continue the work on a board
6. inspect history or notifications only when you need evidence

Current product shape:

- the default route is now `Home`
- `Today` is the daily agenda surface
- `Review` is the normal automation surface
- `Boards` remains the visible work surface
- advanced or operator tools are shipped, but they are not the normal first-run path

## Current Golden Path

The fastest current path to value is:

1. land on `Home`
2. create a board from setup if you do not have one yet
3. capture rough input into quick capture or `Inbox`
4. start triage
5. open `Review`
6. review, approve, and execute the proposal
7. return to the board and work the cards

That is the current product loop.

`Today` supports the same loop by making the next daily action visible when you already have work underway.

## Navigation By Workspace Mode

Workspace modes are display preferences, not permission boundaries.

Guided mode keeps the normal loop prominent:

- primary: `Home`, `Today`, `Review`, `Boards`, `Inbox`
- secondary: `Notifications`, `Chat`, `Activity`, `Ops`, `Settings`, `Preferences`, `Access`, `Archive`

Workbench mode keeps the broader operator toolset close in the main nav, but feature-flagged advanced pages can still require toggles in `Settings`:

- primary: `Home`, `Today`, `Review`, `Boards`, `Inbox`, `Notifications`, `Chat`, `Settings`, `Preferences`
- advanced feature-flagged surfaces that may still need explicit toggles: `Activity`, `Ops`, `Access`, `Archive`

Agent mode currently ships the same pages as guided mode plus the same secondary workbench tools:

- primary: `Home`, `Today`, `Review`, `Boards`, `Inbox`
- secondary: `Notifications`, `Chat`, `Activity`, `Ops`, `Settings`, `Preferences`, `Access`, `Archive`

Important truth:

- `Agent` mode exists as a workspace preference today
- dedicated `Agents`, `Runs`, `Knowledge`, and `Integrations` routes do not ship yet

## Surface Reference

### Home

What it is for:

- resetting the loop
- seeing the current workload clearly
- replaying onboarding or setup when the path is unclear

Use it when:

- you first sign in
- you are not sure where to begin
- you want recent boards and recommended actions in one place

### Today

What it is for:

- deciding the next daily action
- reviewing proposals before diving into board work
- surfacing overdue, due-today, and blocked cards

Use it when:

- you already have work in motion
- you want the daily agenda instead of the broad reset view

### Boards

What it is for:

- visible execution
- editing cards and columns
- comments, labels, due dates, and blockers

Use it when:

- work is ready to be acted on
- you need to move cards forward or collaborate on details

Product-language note:

- the route label is still `Boards`
- the docs use `project` as the higher-level mental model when helpful

### Inbox

What it is for:

- messy intake
- storing work before it is structured enough for the board
- turning captures into reviewable proposals

Use it when:

- the input is rough
- you need to save context now and decide shape later

### Review

What it is for:

- approving or rejecting proposed changes
- keeping automation review-first and explicit

Use it when:

- triage has produced a proposal
- `Home` or `Today` says review work is waiting

Compatibility note:

- `Review` is the current user-facing path for the older proposals route
- legacy automation paths still redirect for compatibility

### Notifications

What it is for:

- mentions
- proposal outcomes
- follow-up signals that matter to a user directly

### Settings And Preferences

Settings covers:

- profile details
- notification preferences
- feature-flagged advanced surfaces

Use preferences when:

- you need to tune notification behavior or workspace posture

### Access

Use `Access` when:

- you are managing board membership and roles

This is an advanced management surface, not part of the normal capture path.

### Archive

Use `Archive` when:

- you need to restore or inspect archived boards

### Chat

Use `Chat` when:

- you want board-scoped conversational help
- you intentionally want a more manual operator-style automation flow

### Activity

Use `Activity` when:

- you need history, provenance, or audit-style context

### Ops

Use `Ops` when:

- you are diagnosing the system
- you need logs, endpoint exploration, or CLI tooling

## Boards, Cards, And Starter Packs

Boards:

- contain columns, cards, labels, comments, and board settings
- can be archived and restored
- are where work should feel visible and actionable

Cards support:

- title and description
- due date
- labels
- blocked state and blocked reason
- threaded comments and mentions

Starter packs:

- scaffold columns, labels, and optional seed cards
- are safe to reapply because apply behavior is idempotent and conflict-aware
- currently support fast-start shapes such as `Engineering sprint`, `Support triage`, and `Content calendar`

## Inbox, Triage, And Review

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

- happens in `Review`
- is the primary trust boundary for board mutation
- should answer what changes, where, and why

Current review model:

1. proposal is generated
2. user reviews operations and summary
3. user approves or rejects
4. user executes explicitly

## Daily Rhythm

Morning:

- open `Home`
- open `Today`
- review pending proposals before diving into card work

During work:

- capture follow-ups immediately instead of holding them in your head
- use comments to preserve reasoning on cards
- return to the board when proposals create or update work

End of day:

- move cards forward honestly
- capture loose ends into `Inbox`
- avoid leaving important context only in local notes or memory

## Normal User Surfaces vs Advanced Surfaces

Normal user surfaces:

- `Home`
- `Today`
- `Inbox`
- `Review`
- `Boards`

Advanced or operator surfaces:

- `Chat`
- `Activity`
- `Notifications`
- `Ops`
- `Access`
- `Archive`

Rule of thumb:

- if you are new, stay in the normal user surfaces until the loop is clear
- only drop into advanced surfaces when you specifically need diagnostics, evidence, or manual control

## Workflow Guides And Help

Use these docs together:

- [product/FIRST_RUN_WORKFLOWS.md](product/FIRST_RUN_WORKFLOWS.md)
  - concise step-by-step guides for first-run and daily flows
- [product/HELP_AND_FAQ.md](product/HELP_AND_FAQ.md)
  - page-level help, empty-state recovery, and common confusion points
- [manual/README.md](manual/README.md)
  - manual structure and in-app help mapping

## Manual Chapters

This user manual is chaptered under `docs/manual`. The primary chapters are:

- [manual/01_start_here.md](manual/01_start_here.md)
  - product framing, first-value path, and glossary
- [manual/02_home_and_today.md](manual/02_home_and_today.md)
  - `Home`, `Today`, and the daily rhythm
- [manual/03_projects_and_cards.md](manual/03_projects_and_cards.md)
  - boards, cards, starter packs, and execution flow
- [manual/04_inbox_and_review.md](manual/04_inbox_and_review.md)
  - capture, triage, review, and trust model guidance
- [manual/05_advanced_automation.md](manual/05_advanced_automation.md)
  - advanced/operator surfaces such as `Chat`, `Queue`, `Ops`, and `Archive`
- [manual/06_agents.md](manual/06_agents.md)
  - future `Agents` and `Runs` placeholder guidance
- [manual/07_integrations_and_knowledge.md](manual/07_integrations_and_knowledge.md)
  - future `Integrations` and `Knowledge` placeholder guidance
- [manual/08_recipes.md](manual/08_recipes.md)
  - short repeatable workflows
- [manual/09_troubleshooting.md](manual/09_troubleshooting.md)
  - common questions, empty states, and recovery paths

## Demo And Testing Workflows

From `frontend/taskdeck-web`:

- `npm run demo:seed` seeds a reusable baseline workspace
- `npm run demo:run -- --list` lists scenarios
- `npm run demo:run -- engineering-sprint` runs one scenario
- `npm run demo:autopilot -- --turns 5 --brain heuristic` simulates activity
- `npm run demo:director:smoke` runs the deterministic smoke or demo regression path

For direct API walkthroughs:

- use `demo/http/taskdeck-demo.http` with the VS Code REST Client

For the full demo or operator path:

- see [product/DEMO_PLAYBOOK.md](product/DEMO_PLAYBOOK.md)
- see [product/SCENARIOS.md](product/SCENARIOS.md)

## Troubleshooting Basics

If you do not know where to start:

- go back to `Home`
- replay setup if needed
- use `Today` for the next daily action
- use `Inbox` if the work is still a note
- use `Review` if the work is waiting for approval

If `Review` is empty:

- triage something from `Inbox` first

If `Boards` is empty:

- create a board or use the setup flow from `Home` or `Today`

If `Notifications` or `Activity` is empty:

- those surfaces usually need real work history before they become useful

If `Queue` or `Ops` feels too technical:

- that is expected for ordinary daily use
- return to `Inbox`, `Review`, or the board

## Current Constraints

- `Agent` mode exists, but dedicated `Agents`, `Runs`, `Knowledge`, and `Integrations` routes are still future work
- some advanced flows still expose more system detail than a novice-ready product should
- automation parsing remains pattern-based and board-centric
- review-first behavior is intentional; destructive autonomy is out of scope
