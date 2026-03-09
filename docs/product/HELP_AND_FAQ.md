# Help And FAQ

This is the help-center baseline for the shipped product shell.

Use [FIRST_RUN_WORKFLOWS.md](FIRST_RUN_WORKFLOWS.md) for step-by-step journeys.
Use [../USER_MANUAL.md](../USER_MANUAL.md) for the broader shipped-product reference.

## Home

What this page is for:

- resetting the product loop
- seeing the current workload at a glance
- restarting setup when the flow feels unclear

When should I use it?

- at the start of the day
- after signing in
- when you are not sure whether to go to `Today`, `Inbox`, `Review`, or a board

What do I do if it is empty?

- create the first board
- replay setup if onboarding was dismissed
- add one capture so the loop has something to process

Common mistakes:

- treating `Home` like a dashboard you can ignore after first use
- jumping straight into advanced pages before the core loop is clear

## Today

What this page is for:

- shaping the day
- checking review work before board work
- seeing overdue, due-today, and blocked cards in one place

When should I use it?

- when work already exists and you need the next concrete action
- when you want to resume the onboarding path

What do I do if it is empty?

- finish the setup path so the workspace has a board
- triage something in `Inbox`
- return to `Home` if you still need the broad reset view

Common mistakes:

- using `Today` as a replacement for board execution instead of a daily guide
- ignoring the review queue section and jumping straight to card work

## Inbox

What this page is for:

- storing messy notes, follow-ups, and rough plans
- turning capture into something reviewable

When should I use it?

- when the work is not structured enough to become a board change yet
- when you need to save context immediately

What do I do if it is empty?

- use quick capture or create one Inbox item
- come back after triage creates proposals if you expected follow-through

Common mistakes:

- over-organizing before capturing
- expecting Inbox itself to be the approval step instead of `Review`

## Review

What this page is for:

- deciding proposed changes before they touch a board
- approving, rejecting, or executing work prepared by the system

When should I use it?

- after triage runs
- when `Home` or `Today` says proposals are waiting
- when you need the trust boundary

What do I do if it is empty?

- open `Inbox` and triage something first
- return to `Home` or `Today` if you are checking the rest of the loop

Common mistakes:

- expecting proposals to apply themselves
- using advanced `Queue` as the normal approval path

## Boards

What this page is for:

- visible execution
- card movement, editing, and collaboration
- continuing the work after review

When should I use it?

- once the work is ready to be acted on
- when you need to update card state, add comments, or track blockers

What do I do if it is empty?

- create the first board
- apply a starter pack if you want a faster starting shape

Common mistakes:

- trying to use boards as the only intake surface
- expecting boards to explain pending proposals without going through `Review`

## Notifications

What this page is for:

- seeing mentions and proposal outcome updates

When should I use it?

- when you are following up on collaboration or asynchronous review outcomes

What do I do if it is empty?

- that usually means nothing has mentioned you yet and no proposal outcome targeted you

Common mistakes:

- treating notifications as the main place to manage work instead of a follow-up signal

## Chat

What this page is for:

- board-scoped conversational help
- manual operator-style automation follow-up

When should I use it?

- when you specifically want a conversational flow
- when `Inbox` and `Review` are not enough for the job

What do I do if it feels too advanced?

- return to `Inbox` for normal intake
- return to `Review` for the normal proposal path

Common mistakes:

- using `Chat` as the default starting path for ordinary work

## Activity

What this page is for:

- inspecting what already happened
- checking board, entity, or user history

When should I use it?

- when you need evidence, traceability, or audit-style context

What do I do if it is empty?

- widen the filters
- confirm the relevant board already has meaningful activity

Common mistakes:

- going to Activity when what you really need is a pending decision in `Review`

## Ops

What this page is for:

- diagnostics
- logs
- endpoint and CLI exploration

When should I use it?

- when you are operating or troubleshooting the system

What do I do if it feels too technical?

- leave it alone for normal product use
- return to `Home`, `Today`, `Inbox`, `Review`, or a board

Common mistakes:

- assuming Ops is part of the ordinary first-run path

## Access And Archive

Use `Access` when:

- you are managing board sharing and roles

Use `Archive` when:

- you need to restore or inspect archived boards

Common mistake:

- treating either of these as part of normal daily capture or review work

## FAQ

### Why do I land on Home instead of Boards?

Because the shipped shell now starts from the product loop, not from the implementation-shaped board list. `Home` is meant to reduce route-hunting.

### Is Review the same as the old proposals page?

Yes. `Review` is the user-facing route and language for the proposals workflow. Legacy automation routes still exist for compatibility, but the intended normal path is `Review`.

### Why does the UI still say Boards if the docs talk about projects?

The shipped route label is still `Boards`. The docs use `project` as the product mental model so the help-center can grow cleanly without pretending a renamed route already shipped.

### What is the difference between Guided and Workbench?

`Guided` keeps the core loop prominent. `Workbench` keeps more tools visible all the time. They are presentation preferences, not permission boundaries.

### What does Agent mode do today?

It preserves the same shipped review-first loop and prepares the mental model for later agent work. It does not mean standalone `Agents`, `Runs`, or `Knowledge` pages are already available.

### Why is a page empty right after I sign in?

Most empty states mean the loop has not started yet. Create a board, capture one item, triage it, and then revisit the page.

### Why did nothing change after capture?

Capture stores the input. A board change happens only after triage creates a proposal and you explicitly execute it from `Review`.

### When should I use Queue?

Only when you intentionally want a manual, power-user instruction path. For ordinary work, start with `Inbox`, then go to `Review`.
