# Taskdeck User Manual

If you are new to Taskdeck, read [START_HERE.md](START_HERE.md) first.
This manual is the reference for the shipped product shell.

## Product Shape

Taskdeck is a review-first workspace for turning rough input into visible board work.

The main loop is:

1. capture something quickly
2. shape it into a proposal
3. review the proposal
4. apply it explicitly
5. continue the work on a board

The shipped top-level normal-user shell is:

- `Home`
- `Today`
- `Inbox`
- `Review`
- `Boards`

The current route label is still `Boards`, even when some product copy uses "project" to describe the same work context.

## Workspace Modes

Taskdeck supports three workspace modes:

- `Guided`
  - emphasizes `Home`, `Today`, `Inbox`, `Review`, and `Boards`
- `Workbench`
  - keeps more tools visible for hands-on and diagnostic workflows
- `Agent`
  - preserves the same review-first path while later agent breadth is still future work

Workspace modes change how navigation is presented. They do not grant extra permissions.

## First-Run Guide

Use this path if you want the fastest clean introduction to the product:

1. Stay in `Guided` mode.
2. Open `Home`.
3. Create one capture with quick capture or by going to `Inbox`.
4. In `Inbox`, choose `Start Triage`.
5. Open `Review`.
6. Inspect the proposal, approve it, and execute it.
7. Open `Boards` to continue the work on the resulting cards.

If there are no boards yet:

1. open `Boards`
2. create a board
3. optionally apply a starter pack
4. return to `Home` or `Today`
5. continue the same loop

## Page Guide

### Home

When should I use this page?

Use `Home` when you want the quickest summary of what needs attention and where to go next.

`Home` is best for:
- re-entering the product after a break
- checking whether setup is complete
- jumping into `Today`, `Inbox`, `Review`, or `Boards` without route-hunting

What you should expect:
- workspace summary cards
- recommended next actions
- replayable setup guidance
- recent board context

Common mistakes:
- treating `Home` like a passive dashboard instead of a routing surface
- staying on `Home` when you already know you need `Inbox`, `Review`, or `Boards`

### Today

When should I use this page?

Use `Today` when you want a daily agenda instead of a broad summary.

`Today` is best for:
- deciding whether proposals need review first
- seeing due-today, overdue, and blocked work
- returning to the onboarding path if you need the guided loop again

What you should expect:
- agenda groups for `Review`, `Inbox`, and board work
- setup replay and dismiss controls
- next-step shortcuts back into the main loop

Common mistakes:
- using `Today` as a substitute for board detail work
- skipping `Review` when there are pending proposals waiting

### Inbox

When should I use this page?

Use `Inbox` when the input is still rough.

Examples:
- notes
- bugs
- pasted plans
- follow-ups
- ideas you do not want to lose

Recommended flow:

1. capture first
2. open the item
3. choose `Start Triage`
4. move to `Review` once a proposal exists

Common mistakes:
- trying to fully structure the work before capturing it
- using `Queue` instead of `Inbox` when you do not need the advanced manual path

### Review

When should I use this page?

Use `Review` whenever Taskdeck has prepared a change and you need to decide whether it should touch a board.

`Review` is the normal-user trust gate:
- it shows proposed operations before apply
- it keeps approval and rejection explicit
- it keeps board follow-through visible

Recommended flow:

1. open a pending proposal
2. read the summary and affected board context
3. approve or reject
4. execute approved work explicitly
5. continue on the linked board

Common mistakes:
- treating `Queue` as the main proposal surface
- assuming a proposal will apply itself without an explicit execution step

### Boards

When should I use this page?

Use `Boards` for the work itself.

Boards are where:
- approved changes land
- cards move across columns
- due dates, labels, and blocked states stay visible
- comments and mentions preserve collaboration context

Starter packs help when you want a board to feel useful immediately.

Common mistakes:
- trying to keep important context only in local notes instead of on cards
- bypassing `Review` when a proposal-driven change would be safer and clearer

### Notifications

When should I use this page?

Use `Notifications` when you need user-targeted updates such as mentions or proposal outcomes.

Common mistakes:
- expecting `Notifications` to replace the board workflow
- assuming an empty inbox means something is broken when no triggering events have happened yet

## Step-by-Step Workflows

### Workflow: Capture Something And Turn It Into Board Work

1. Create a capture from quick capture or `Inbox`.
2. Open the new Inbox item.
3. Choose `Start Triage`.
4. Wait for the proposal to appear in `Review`.
5. Open the proposal and inspect the summary.
6. Approve it if the change is correct.
7. Execute it.
8. Open the linked board and continue the work there.

### Workflow: Reset The Day

1. Open `Home`.
2. Switch to `Today`.
3. Handle pending proposals in `Review` first.
4. Triage fresh captures from `Inbox`.
5. Return to `Boards` for active work.
6. Use `Notifications` if you need event follow-up.

### Workflow: Start A New Board Cleanly

1. Open `Boards`.
2. Create a board.
3. Apply a starter pack if you want default columns or labels.
4. Return to `Home` or `Today`.
5. Capture or triage work into that board through the normal loop.

## Advanced And Operator Surfaces

These pages are real, but they are not the recommended first-run path.

### Chat

Use `Chat` when you want a conversational, board-scoped workflow and you intentionally need the more manual automation path.

### Activity

Use `Activity` to inspect what already happened across boards, entities, or users.
It is a trust and history surface, not the main starting point.

### Ops

Use `Ops` for diagnostics:
- CLI runs
- endpoint exploration
- logs

This is an operator surface, not an everyday capture surface.

### Access

Use `Access` to manage board membership and role assignment.

### Archive

Use `Archive` to review, restore, or reveal archived boards.

## FAQ

### Where should I start if I am new?

Start with `Home`, then `Inbox`, then `Review`, then `Boards`.
That path teaches the product without requiring you to learn the advanced surfaces first.

### What is the difference between Inbox and Review?

- `Inbox` stores raw input
- `Review` stores proposed changes waiting for a decision

### What is the difference between Boards and Projects?

The shipped navigation label is `Boards`.
"Project" is product-facing language for the same board-centered work context.

### Do proposals apply automatically?

No.
Taskdeck is intentionally review-first. A proposal must be reviewed, approved, and then executed explicitly.

### When should I use Queue?

Only when you intentionally want the more manual instruction path.
For ordinary use, prefer `Inbox` and `Review`.

### When should I use Chat?

Use `Chat` when you specifically want a conversational board-scoped workflow.
It is a secondary path, not the default first-run route.

### Why are some pages missing from my main navigation?

In `Guided` mode, advanced tools are intentionally de-emphasized.
Switch to `Workbench` mode if you want more surfaces visible at once.

### Does Agent mode mean agents are fully shipped?

No.
`Agent` is a navigation posture that keeps the future surface area visible in a limited way without pretending the full later-wave architecture is already delivered.

## Troubleshooting

### Home feels empty

You probably do not have a board yet.
Create one in `Boards`, optionally apply a starter pack, and return.

### Inbox is empty

Create one capture first.
The page is expected to be quiet until something has been captured.

### Review is empty

That usually means no proposal has been generated yet.
Go back to `Inbox`, start triage on an item, then return to `Review`.

### Boards feels too blank to start

Create a board and apply a starter pack.
Then use the normal capture-to-review path so work lands there quickly.

### I expected Queue or Chat to be the main entry path

They are not.
The intended normal-user path is `Home -> Inbox -> Review -> Boards`, with `Today` acting as the daily agenda surface.

### Why do I need review before apply?

Because Taskdeck is designed to keep automation inspectable.
Review is the boundary that keeps board changes explicit and trust-preserving.

### Why do in-app help callouts keep appearing?

The main product pages include replayable, dismissible help blocks.
Dismiss them when you no longer need them, and replay setup from `Home` or `Today` when the loop becomes unclear again.

## Demo And Seeded Workspace

If you want a richer first run with believable sample data:

```bash
cd frontend/taskdeck-web
npm run demo:seed
```

Use the seeded workspace when:
- you are evaluating the product quickly
- you want to follow the workflow without creating everything from scratch
- you need sample boards, captures, and proposals

For full seeded walkthroughs and scripted demos, see [product/DEMO_PLAYBOOK.md](product/DEMO_PLAYBOOK.md).

## See Also

- [START_HERE.md](START_HERE.md)
- [manual/README.md](manual/README.md)
- [product/DOGFOODING_GUIDE.md](product/DOGFOODING_GUIDE.md)
- [product/DEMO_PLAYBOOK.md](product/DEMO_PLAYBOOK.md)
- [product/SCENARIOS.md](product/SCENARIOS.md)
