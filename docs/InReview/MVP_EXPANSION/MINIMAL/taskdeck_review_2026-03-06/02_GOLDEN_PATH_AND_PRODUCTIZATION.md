# Golden path and productization plan

## The surface taxonomy you should adopt

Right now Taskdeck has many pages, but not all pages are equal in product importance.

You should explicitly think in three layers.

## Layer 1: core product surfaces
These define the MVP.

- Boards
- Board view
- Inbox
- Proposals
- Starter Packs
- Quick Capture modal
- optionally Chat, if framed carefully

If a user never opens any other page, the product should still feel usable.

## Layer 2: supporting trust surfaces
These help explain and validate the core loop.

- Notifications
- Activity
- comments and mentions
- presence/conflict hints

These are important, but secondary.

## Layer 3: operator/dev surfaces
These are valuable, but they are not the first-run product.

- Queue
- Ops
- Access
- Archive
- Export/Import
- direct endpoint explorer

These should not dominate the first impression.

## The current state

You have already moved in the right direction by:

- hiding some advanced surfaces by default through feature flags
- defaulting Automations to Proposals instead of Queue
- documenting the main flow
- creating seed/demo flows

But the UI itself still mostly behaves like a collection of pages rather than a guided system.

## The golden path I would implement

## Path A: first-time user golden path
This is the one that matters most.

### Step 1: land on a start screen, not a raw board list
After login, do not drop the user directly into a plain "My Boards" grid with no guidance.

Give them one of these:

- a dedicated `/workspace/home` route, or
- a hero/onboarding panel at the top of `BoardsListView`

That screen should answer:

- what Taskdeck is
- what the main loop is
- what I should do right now

### Step 2: present exactly three primary actions
The first-run screen should have three large calls to action:

- Quick Capture
- Create Board
- Load Demo Workspace

That is enough.

### Step 3: immediately show causality
After the first capture, route the user to Inbox with the created item selected.
When triage completes, show "Open Proposal".
After execute, show "Open Board".

Right now these links exist in pieces.
They need to become the obvious happy path.

## Path B: daily user golden path
For real use, the flow should be:

1. capture quickly from anywhere
2. process Inbox to zero
3. review proposals
4. execute accepted changes
5. work active board
6. use comments/mentions for context
7. occasionally inspect activity/notifications

That means the app should support these shortcuts in a first-class way:

- global quick capture
- Inbox badge/count
- proposal count in nav
- board-scoped actions from the current board
- one-click return to the active board after proposal execution

## Path C: stakeholder golden path
A demo should be even tighter than normal use.

Suggested order:

1. Quick Capture
2. Inbox selected item
3. Start triage
4. Open proposal
5. Approve + execute
6. open the board where the cards appeared
7. mention/collaboration proof
8. notification/activity proof
9. ops only if needed

Your current stakeholder flow is good breadth coverage, but I would tighten it around the causal chain above.

## Concrete UI changes I would make next

## 1. Add a start surface
Minimal version:
add a banner to `BoardsListView` when the user has zero or very few boards.

Better version:
add `/workspace/home` with:

- thesis statement
- Quick Capture button
- Create Board button
- Load Demo Workspace button
- "How Taskdeck works" 4-step explainer
- counts: Inbox, proposals needing review, unread notifications

## 2. Add board-scoped automation affordances
The current board page should become the main execution hub.

Add buttons like:

- Capture into this board
- Review proposals for this board
- Open chat for this board
- Add from automation
- View board activity

These should prefill the relevant context.
Users should not have to manually carry board identity between screens.

## 3. Replace raw board ID inputs with board pickers
This is one of the highest-leverage usability changes.

In the Queue composer, entering a GUID is still a developer affordance, not a product affordance.

Use:

- board picker by name
- board ID hidden as implementation detail
- prefill current board when coming from a board route

This alone would make Queue feel far less like scaffolding.

## 4. Make proposal cards more readable
Current proposals are functional, but still quite "system-shaped".

Improve them with:

- operation summaries rendered as bullets, not only raw diff text
- affected board/card links
- provenance summary in plain language
- risk explanation in human language
- strong primary CTA based on state:
  - pending -> Approve
  - approved -> Execute
  - applied -> Open Board

## 5. Improve empty states with next actions
The current app still has too many "No X found" states.

Each empty state should say:

- what this page is for
- why it is empty
- what to click next

Examples:

### Notifications empty state
"No notifications yet. Mentions and proposal outcomes appear here. Add a comment with @username or execute a proposal to see examples."

### Activity empty state
"No activity yet. Board changes, proposal execution, and comments create audit history. Open a demo board or make a change."

### Queue empty state
"Queue is the advanced instruction surface. Most users should start with Inbox or Chat."

## 6. Make quick capture board-aware
Current quick capture is workspace-global. That is good for friction reduction, but incomplete for real use.

I would support both:

- global capture with no board
- board-scoped capture from inside a board

That lets the product support both spontaneous capture and intentional project work.

## 7. Add a "Today" or "Focus" view later, not now
This is valuable, but not the next step.

Do it after the golden path is obvious.
Otherwise you risk adding another page before the core story is settled.

## What makes the product useful already

Even now, Taskdeck can already be useful for a solo developer if you frame it correctly.

It is already good for:

- collecting follow-ups while coding
- converting messy notes into structured cards
- reviewing suggested mutations before applying them
- running board-based workflows with comments/mentions
- preparing reproducible demo/test worlds

It is not yet equally good for:

- polished team onboarding
- novice-first self-serve use
- broad autonomous agent project management
- rich analytics/prioritization workflows

## Product position I would keep

Keep the message tight:

Taskdeck is a **safe execution workspace**.
It is not trying to automate everything.
It is trying to make capture cheap and automation trustworthy.

That is much stronger than trying to be "a generic AI task manager".
