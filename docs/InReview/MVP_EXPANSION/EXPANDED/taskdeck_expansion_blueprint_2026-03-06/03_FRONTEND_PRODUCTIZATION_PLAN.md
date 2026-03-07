# Frontend Productization Plan

This document is intentionally concrete.
It is written to fit the current Vue 3 / TypeScript / Pinia codebase.

## Core frontend principle

Do not rip out the current frontend.
Layer the product UX on top of it.

## Files to change first

### Existing high-leverage files

- `frontend/taskdeck-web/src/router/index.ts`
- `frontend/taskdeck-web/src/components/shell/AppShell.vue`
- `frontend/taskdeck-web/src/store/featureFlagStore.ts`
- `frontend/taskdeck-web/src/store/sessionStore.ts`
- `frontend/taskdeck-web/src/views/BoardsListView.vue`
- `frontend/taskdeck-web/src/views/BoardView.vue`
- `frontend/taskdeck-web/src/views/InboxView.vue`
- `frontend/taskdeck-web/src/views/AutomationQueueView.vue`
- `frontend/taskdeck-web/src/views/NotificationInboxView.vue`
- `frontend/taskdeck-web/src/views/ActivityView.vue`

### New views to add

- `HomeView.vue`
- `TodayView.vue`
- `ReviewView.vue`
- `WorkspaceModeSettingsView.vue` or section in profile/preferences
- later: `AgentsView.vue`, `AgentRunView.vue`, `KnowledgeView.vue`, `IntegrationsView.vue`

## 1. Add workspace mode

### Why

Feature flags are not enough.
You now need a **presentation mode** that changes navigation and page emphasis.

### Add a new store

Suggested store:

- `src/store/workspaceModeStore.ts`

State:

- `mode: 'guided' | 'workbench' | 'agent'`
- persisted to localStorage
- optionally loaded from server profile/preferences

### Expected effects

- nav items shown/hidden
- default route after login
- home cards shown
- advanced surfaces collapsed under “More” in guided mode

### Suggested snippet

See `snippets/frontend/workspaceModeStore.ts`.

## 2. Add Home view

### Why

Boards list is not a product homepage.
It is a resource listing.

### Home view responsibilities

- first-run guidance
- daily summary
- CTA launcher
- project resume point
- review counts
- inbox counts

### Data requirements

Create a small backend summary endpoint instead of fanning out too much from the client:

- `/api/workspace/home`

It should return:

- recent boards
- counts: inbox new, inbox triaging, proposals pending review, blocked cards, due today
- optional active board suggestion
- first-run booleans or “has used capture / has created board / has executed proposal”

### UI structure

- `hero`
- `start-here`
- `needs-attention`
- `continue-working`
- `how-it-works`

### Suggested snippet

See `snippets/frontend/HomeView.vue`.

## 3. Add Today view

### Why

Without a daily view, Taskdeck feels like a system you maintain rather than a system that helps you work.

### What Today should show

- cards due today / overdue
- blocked cards
- cards assigned to me if assignments later exist
- recently updated cards I touched
- proposals pending review
- inbox items pending triage

### Interaction rules

- each block must have at least one CTA
- each block should deep-link to the relevant board or review item

### Query strategy

Prefer one aggregated endpoint:

- `/api/workspace/today`

This is better than many unrelated client fetches.

## 4. Replace Automations landing with Review view

### Why

Queue is not the right “front door” to automation.
The review surface is.

### Recommended change

- keep the current proposals route
- add a user-facing alias: `/workspace/review`
- make nav label `Review`
- move Queue under an advanced sub-tab / “More” section

### Proposed tabs in Review

- Pending
- Approved
- Applied
- Failed
- Sources (`Inbox`, `Chat`, `Queue`, `Agent`)

## 5. Add board action rail

### Why

The board must become the center of execution.

### Add these actions to board header

- `Capture here`
- `Ask assistant`
- `Review proposals`
- `Add card`
- `More`

### Behavior

- `Capture here` opens capture modal pre-scoped to board
- `Ask assistant` opens chat modal pre-scoped to board
- `Review proposals` deep-links to `/workspace/review?boardId=<id>`
- `Add card` preserves current board context and first active column

### Needed UI work

- small shared board context bar component
- search-based board selector for advanced pages

## 6. Proposal readability improvements

### Problem

Proposal cards are often technically correct but not product-legible.

### Add a `ProposalSummaryCard` component

It should compute a plain-language summary:

Examples:

- “Create 3 cards in Backlog”
- “Move 1 card to Done”
- “Rename board to Sprint 14”

### The card should show

- summary line
- board/project name
- source chip
- risk chip
- operation count
- affected entities chips
- created time
- actions

### Suggested snippet

See `snippets/frontend/ProposalSummaryCard.vue`.

## 7. Fix common discoverability gaps

### Queue view

Add:

- explanation banner
- board picker instead of board ID text box
- examples drop-down
- “generated proposal will appear in Review” callout

### Activity view

Add:

- preset buttons (`Current board`, `My actions today`, `Recent workspace activity`)
- explanation empty state

### Notifications

Add:

- grouped sections by type
- mark all as read
- link previews (“from board X”, “from card Y”)

### Access

Replace free-form board id entry with:

- board picker
- current role summary
- member role table

## 8. Make global search real

Today the command palette is mostly navigation plus a capture action.
Turn it into search.

### Search targets

- boards
- cards
- capture items
- proposals
- chat sessions
- notifications

### First implementation

No vector search needed.
Start with:

- client-side recent items
- backend FTS or “contains” queries for boards/cards/captures

### UX behavior

- typing `#` can bias to boards
- typing `!` can bias to review/proposals
- typing `/` can bias to actions

## 9. Onboarding and help surfaces

### Add a `Start here` checklist

Persist per user.

Checklist items:

- create first project
- capture first note
- execute first proposal
- move first card
- enable advanced surfaces (optional)

### Add inline “What is this?” blocks

On pages that are concept-heavy, include a dismissible explanatory block.

## 10. Accessibility and polish

### Minimum polish bar

- keyboard focus visible everywhere
- every modal has focus trap
- every listbox has correct aria state
- toasts do not swallow critical errors silently
- destructive actions require explicit confirmation
- no page depends on hover-only affordances

### Visual polish priorities

- unify primary action placement
- consistent page-title + subtitle layout
- tighter empty state cards
- readable spacing in card/proposal lists
- standardize chips/badges across inbox/review/notifications

## Suggested frontend implementation order

1. workspace mode store
2. Home view + route
3. Review alias + proposal summary card
4. Today view
5. board action rail
6. board/queue/chat selectors instead of raw IDs
7. global search upgrade
8. onboarding checklist + help blocks
9. agent-facing views later
