# Golden Paths and UX Shape

## The product needs one undeniable path

Today, Taskdeck contains many capabilities.
The problem is not capability.
The problem is that the path is not obvious.

The main golden path should be:

1. Capture
2. Triage / propose
3. Review
4. Execute
5. Work today

Everything else should reinforce this path.

## Golden path 1 — first 2 minutes

### Goal

A new user creates value in under 2 minutes without docs.

### Flow

1. Land on `Home`.
2. See:
   - “Taskdeck turns quick captures into reviewable project updates.”
3. Buttons:
   - `Create Project`
   - `Capture a Note`
   - `See Sample Workspace` (demo/dev only)
4. User clicks `Create Project`.
5. A lightweight wizard asks:
   - project name
   - project type (`general`, `engineering sprint`, `content calendar`, `support triage`)
6. Project is created, starter pack applied automatically if selected.
7. Home updates with:
   - `Capture something into this project`
8. User captures a note.
9. The UI guides them to `Review` when proposal is ready.
10. They approve and execute.
11. They land in the project board and see the card created.

### Required UI primitives

- `Home` page
- first-run wizard
- project type quick-start templates
- global capture modal
- proposal-ready toast / home card
- board landing success state

## Golden path 2 — daily individual use

### Goal

The user can run their day from Taskdeck in 5–10 minutes of setup/maintenance.

### Flow

Morning:

1. Open `Today`.
2. See:
   - due today
   - blocked items
   - proposals waiting review
   - inbox items not triaged
3. Click `Review proposals`.
4. Apply the useful ones.
5. Go to current project board.
6. Work from that board.
7. During the day, use quick capture.
8. End the day by moving cards and capturing follow-ups.

### Today page should include

- `Needs review` count
- `Inbox needs triage` count
- `Due today`
- `Blocked`
- `Recently touched`
- `Resume where you left off`

## Golden path 3 — project creation from messy notes

### Goal

Turn unstructured text into structured work without making the user design the structure manually.

### Flow

1. User pastes notes/checklist/transcript into capture.
2. Capture is stored immediately.
3. User clicks `Start triage` or auto-triage is suggested.
4. Review shows:
   - created cards
   - updated cards
   - renamed columns/boards if relevant
5. User sees a human-readable summary first, then diff details.
6. User approves and executes.

### What makes this polished

- proposal summary language must be plain English
- affected board/project must be obvious
- there should be “Open board” and “Open affected card” actions
- there should be an explanation when triage fails

## Golden path 4 — guided autonomous assistance

### Goal

An agent helps without becoming scary.

### Flow

1. User creates an agent from a template:
   - `Inbox triage assistant`
   - `Sprint assistant`
   - `Research assistant`
2. User sets:
   - scope (board or workspace)
   - budget / frequency
   - allowed tools
   - review policy
3. User clicks `Run once`.
4. A run appears with statuses:
   - observing
   - gathering context
   - planning
   - waiting for review
   - applying (if allowed)
   - complete / failed
5. The run outputs one or more proposals or summary artifacts.
6. User reviews and applies.

### Design rule

An agent run must feel like a visible assistant doing work in a room with the lights on.
Not like a hidden daemon.

## Golden path 5 — stakeholder demo

### Goal

A stakeholder sees the product story in 5 minutes.

### Flow

1. Open Home / sample workspace.
2. Show Inbox with captures.
3. Show Review with proposal.
4. Execute proposal.
5. Show Project board update.
6. Show Today.
7. Show Notifications/Activity/Run trace as supporting evidence.

## UX shape rules

### Rule 1: replace blind empty states with action states

Instead of:

- “No items.”

Use:

- what this page is for
- why it is empty
- how to populate it
- 1–3 concrete actions

### Rule 2: every advanced page needs a plain-language top box

For example, Queue should start with:

> “Queue is an advanced intake surface for explicit instructions. Most users should use Inbox or Chat instead.”

And Activity should start with:

> “Activity shows a timeline of board and workspace events. It becomes useful after you start applying proposals and editing cards.”

### Rule 3: the board must become the main working surface again

The board should not feel like a passive result of automations.
It should be where the user works.

Add to board header:

- `Capture here`
- `Ask assistant`
- `Review proposals` (filtered to this board)
- `Add card`

### Rule 4: avoid orphan surfaces

Any surface that exists should be reachable from the current context.

Examples:

- from board -> open board-scoped review
- from inbox item -> open linked proposal
- from proposal -> open affected board/card
- from notification -> deep-link to source entity
- from agent run -> open created proposal

### Rule 5: remove raw IDs from user journeys

IDs can exist for copy/share/debug.
They should not be a required input in common UX.

Replace with:

- searchable board selector
- searchable card selector
- context-aware default scope

## Home page spec

Sections:

1. **Welcome / thesis**
   - one sentence about what Taskdeck is
2. **Start here**
   - create project
   - capture something
   - open sample workspace
3. **Needs attention**
   - proposals waiting
   - inbox items
   - blocked cards
4. **Continue working**
   - recent boards/projects
5. **Learn Taskdeck**
   - 3 short cards: capture, review, work

## Review page spec

Sections:

1. proposal summary cards
2. filters: board, risk, source, status
3. diff panel
4. action bar
5. provenance/evidence

Proposal cards should show:

- title / summary
- board/project name
- source (`capture`, `queue`, `chat`, `agent run`)
- risk level
- operation count
- created time
- actions: `Open diff`, `Approve`, `Reject`, `Open board`

## Today page spec

Today is the bridge from “interesting infrastructure” to “daily utility”.

It should aggregate across boards and show only actionable work:

- due today
- blocked
- overdue
- recently updated by me
- proposals to review
- inbox to triage

This is the page that turns Taskdeck into a productivity app, not just a project shell.
