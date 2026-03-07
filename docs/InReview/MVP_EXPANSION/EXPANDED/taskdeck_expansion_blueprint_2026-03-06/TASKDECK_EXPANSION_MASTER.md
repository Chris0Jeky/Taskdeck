# Taskdeck Expansion Master Package
This file concatenates the main blueprint docs for convenient reading.


---

# Taskdeck Master Blueprint

## Bottom line

Taskdeck should not become two separate products.
It should become **one core system with layered surfaces**.

You already have the hard part of the system:

- safe review-first mutation through proposals
- board execution model
- capture / triage pipeline
- deterministic demo and regression harness
- developer-oriented advanced surfaces

What is missing is **product legibility**.

The current repository is closer to “an execution engine with a UI” than “a polished daily-use product”.
That is fixable without abandoning the current architecture.

## The correct strategic shape

### Shape A — polished novice-first productivity app

Primary user promise:

> “Capture anything quickly, let Taskdeck suggest structure, review the plan, and keep moving.”

Primary characteristics:

- obvious first-run experience
- minimal navigation
- no raw IDs or hidden prerequisites in common flows
- default routes that teach the product
- action rails everywhere
- a strong “Today / Review / Inbox / Projects” loop

### Shape B — broad supervised agent workspace

Primary user promise:

> “Create scoped agents that operate on projects, documents, captures, and external inputs — but always with visible traces, policies, and approval rails.”

Primary characteristics:

- agents have identities, scopes, and policies
- agents do runs, not magic
- agents observe, gather context, plan, and then propose or act within policy
- traces, artifacts, costs, and events are inspectable
- autonomy is bounded and reversible where possible

## Why both shapes can share one core

The underlying primitives are already unusually good for this:

- **Capture** is the intake primitive.
- **Proposal** is the trust primitive.
- **Board/Card** is the execution primitive.
- **Audit/Notifications/Ops** are the observability primitives.
- **Starter packs / imports / webhooks / scenarios** are the scaffolding primitives.

Those primitives support both a human-first and agent-first product.
The mistake would be to fork the domain model too early.

## Product thesis to preserve

These should remain stable even while the surface changes:

1. **Capture must be cheaper than postponing.**
2. **Automation must remain proposal-first by default.**
3. **The user must be able to inspect why something happened.**
4. **Board execution must stay simple.**
5. **Advanced power must be progressively disclosed, not front-loaded.**

## Product shape to avoid

Do not drift into any of these:

- a generic “AI task manager” with unclear differentiator
- a chat-first app where the board is secondary
- an opaque agent system that mutates the workspace silently
- a power-user shell that requires internal IDs for common tasks
- a separate “agent database” disconnected from boards/inbox/proposals

## Recommended product modes

### 1) Guided mode (default)

For novices and first-run users.
Navigation should be:

- Home
- Today
- Inbox
- Projects
- Review
- Settings

Advanced surfaces are hidden or nested under “More”.
The app should explain itself from inside the UI.

### 2) Workbench mode

For current power users and dogfooding.
Navigation can include:

- Boards/Projects
- Inbox
- Review / Automations
- Activity
- Ops
- Integrations
- Notifications
- Settings

This is close to the current shell, but more coherent.

### 3) Agent mode

For supervised autonomous workflows.
Navigation should add:

- Agents
- Runs
- Knowledge
- Integrations
- Review
- Projects
- Inbox

The key is that Agent mode is still grounded in the same review-first substrate.

## The sequencing that makes sense

### Phase 1 — polish the human product

Before broad autonomy, make Taskdeck excellent at:

- first-run onboarding
- daily review
- board execution
- proposal readability
- board-scoped actions
- empty states that teach behavior
- in-app guidance

### Phase 2 — add the agent substrate

Add:

- agent profiles
- agent runs
- run events / traces
- policy bundles
- tool registry
- knowledge search
- schedules / triggers

### Phase 3 — add controlled autonomy

Add:

- narrow auto-apply rules for low-risk operations
- scheduled runs with budgets
- inbound integrations that create captures
- outbound integrations and sync

### Phase 4 — only then add “broad workspace” ambitions

Once the traces, policies, and knowledge layer are real, you can support:

- project assistants
- research assistants
- support triage assistants
- content planning assistants
- personal learning assistants

## Product north star after this package

A new user should be able to do this in under 2 minutes:

1. land on Home
2. understand what the product is for
3. capture one thing
4. review one proposal
5. execute it into a project board
6. see what to do next

And an advanced user should be able to do this in under 5 minutes:

1. create an agent
2. scope it to a board
3. run it on captures or a goal
4. inspect its trace and proposal
5. approve and apply

If those two loops work, the product is coherent.


---

# Product Structure and Positioning

## Recommendation: one core, three surfaces

Do not position Taskdeck as:

- “another kanban board”
- “another AI assistant chat app”
- “another productivity dashboard”

Position it as:

> **A capture-first execution workspace where humans and agents both work through reviewable plans.**

That positioning is specific enough to differentiate, and broad enough to support future agent expansion.

## Core domain objects you already have

These are the assets worth preserving:

- `Board`
- `Column`
- `Card`
- `Label`
- `LlmRequest` / queue item
- `CaptureItem` contract over queue payloads
- `AutomationProposal`
- `ChatSession`
- `Notification`
- `AuditLog`
- `CommandRun` / ops
- `ExternalImport`
- `OutboundWebhookSubscription`

## Product mental model

The best mental model for Taskdeck is not “board software”.
It is a pipeline:

1. **Capture** — messy, fast, low-friction intake
2. **Structure** — triage, imports, assistants, agent runs
3. **Review** — proposals, diffs, risk, affected entities
4. **Execute** — cards/boards/labels/comments/updates
5. **Observe** — activity, notifications, traces, logs

This gives you a much cleaner way to map UI and future features.

## Navigation architecture

### Guided mode

Primary nav:

- Home
- Today
- Inbox
- Projects
- Review
- Settings

Secondary nav or overflow:

- Notifications
- Archive
- Help

Advanced surfaces hidden:

- Ops
- Activity
- Access
- Integrations
- Agent Runs

### Workbench mode

Primary nav:

- Home
- Projects
- Inbox
- Review
- Automations
- Activity
- Notifications
- Settings

Secondary:

- Ops
- Access
- Archive
- Integrations

### Agent mode

Primary nav:

- Home
- Agents
- Runs
- Knowledge
- Inbox
- Projects
- Review
- Integrations
- Settings

Secondary:

- Activity
- Ops
- Archive

## Route structure proposal

Keep backward compatibility for existing routes, but add product-facing routes:

- `/workspace/home`
- `/workspace/today`
- `/workspace/projects` (alias of boards list)
- `/workspace/projects/:id` (alias of board)
- `/workspace/review`
- `/workspace/agents`
- `/workspace/agents/:id`
- `/workspace/runs`
- `/workspace/runs/:id`
- `/workspace/knowledge`
- `/workspace/integrations`

Existing routes like `/workspace/automations/proposals` should remain valid, but most users should not need to know them.

## Workspace modes

Add a persisted per-user preference:

- `guided`
- `workbench`
- `agent`

This is not a security boundary.
It is a display mode and routing/default surface selector.

Store it in two places:

1. local storage for instant UX
2. server-side user preferences for portability

## Default first-run experience

The current first-run experience begins with `Boards`.
That is too implementation-shaped.

The first-run experience should begin with `Home` and show:

- a plain-language statement of what Taskdeck does
- a “Create first project” CTA
- a “Capture something” CTA
- a “Run demo workspace” CTA (dev/demo mode only)
- a checklist of first value steps

## The right novice vocabulary

Avoid exposing too much internal language too early.
Suggested vocabulary mapping:

| Internal concept | Guided label | Workbench label | Agent label |
|---|---|---|---|
| Board | Project | Board / Project | Project |
| Automation Proposal | Review item | Proposal | Proposal |
| LLM Queue | Advanced intake | Queue | Queue |
| Chat session | Assistant chat | Chat | Agent chat |
| Command run | Diagnostics | Ops run | Tool run |
| Capture item | Inbox item | Capture | Capture |

This lets you preserve your domain model without forcing the language on novices.

## Feature boundary rules

### Guided mode must never require

- raw GUID entry
- understanding queue statuses
- understanding ops templates
- understanding route hashes
- multi-surface mental jumps to complete a basic flow

### Workbench mode may expose

- detailed proposal diffs
- queue statuses
- logs and activity
- advanced board settings

### Agent mode may expose

- agents
- runs
- traces
- policies
- schedules
- tool registries

But even Agent mode should not expose chain-of-thought or prompt spaghetti as a product feature.
Show structured trace summaries, actions, evidence, and outputs instead.

## Product hierarchy for common jobs

### Personal/productivity

- daily planning
- learning backlog
- content planning
- career/project tracking

### Team-lite / small studio

- editorial calendar
- engineering sprint
- support triage
- outreach CRM

### Agent-assisted

- inbox triage assistant
- release manager assistant
- support summarizer assistant
- research/project assistant
- content repurposing assistant

These are all still powered by the same pipeline.


---

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


---

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


---

# Backend and Domain Expansion Plan

This plan assumes the existing clean architecture should remain.
It proposes additive slices rather than rewrites.

## Existing backend strengths to preserve

- clear `Domain / Application / Infrastructure / Api` layering
- DTO and service conventions already in place
- proposal-first mutation path
- queue and capture infrastructure
- notification / audit / ops surfaces
- starter packs, imports, and webhooks as extensibility seeds

## Guiding backend rule

Add new capability as **new application services and bounded entities**, not as more incidental logic inside Chat or Queue.

## 1. Add workspace summary endpoints

### Why

The frontend needs product-shaped summary data, not only resource-shaped data.

### Add endpoints

- `GET /api/workspace/home`
- `GET /api/workspace/today`
- `GET /api/workspace/review/summary`
- `GET /api/boards/{boardId}/summary`

### Suggested DTOs

- `WorkspaceHomeDto`
- `TodayAgendaDto`
- `ReviewSummaryDto`
- `BoardSummaryDto`

### Why this matters

These endpoints let the UI become coherent without performing many client-side fetches and manual joins.

## 2. Keep board/card domain stable

Do not introduce a separate `Task` entity unless you discover a domain gap that cards cannot satisfy.

For now:

- board == project/context
- card == work item
- labels / due dates / blocked reason already cover the basics

You can add overlays like Today and Agents without changing that.

## 3. Add user preferences as first-class server state

### Problem

Feature flags and workspace mode currently look client-only.

### Add server-side preferences

A simple entity such as:

- `UserPreference`
  - `UserId`
  - `WorkspaceMode`
  - `ShowAdvancedSurfaces`
  - `OnboardingStateJson`
  - `DefaultBoardId`

This gives you portable behavior across devices and environments.

## 4. Add proposal summaries in application layer

### Problem

The frontend should not have to reverse-engineer proposal meaning from low-level operations every time.

### Add a service

- `IProposalSummaryService`
- `ProposalSummaryService`

It should produce:

- summary title
- summary sentence
- affected entity descriptors
- safe suggested next action

This can be computed from existing operations.

## 5. Add agent domain only after summary/home/today exist

Do not begin agent domain work before product-shaped summary endpoints exist.
Those endpoints will also be useful to agents.

## 6. Proposed agent domain model

### Minimal first slice

- `AgentProfile`
- `AgentRun`
- `AgentRunEvent`
- `AgentPolicy`

Optional later:

- `AgentArtifact`
- `AgentSchedule`
- `AgentMemoryEntry`
- `KnowledgeDocument`
- `KnowledgeChunk`
- `IntegrationConnection`

### Why this split works

You can launch useful supervised agents with just:

- profile
- run
- events
- policy

The rest can follow.

## 7. Agent design rules

### Rule A: runs, not magic

Agents should create runs with explicit statuses:

- queued
- observing
- gathering-context
- planning
- proposal-created
- waiting-for-review
- applying
- completed
- failed
- cancelled

### Rule B: structured traces, not raw chain-of-thought

Persist:

- event type
- timestamp
- summary
- referenced entities
- tool used
- result summary

Do not make internal reasoning transcripts a product surface.

### Rule C: proposal-first by default

Agent runs should usually produce proposals.
Only a narrow class of low-risk actions should ever auto-apply, and only under explicit policy.

### Rule D: scoped permissions

An agent must have:

- workspace scope or board scope
- tool allowlist
- budgets (`maxSteps`, `maxProposals`, `maxActions`, maybe `maxTokens`)
- schedule restrictions

## 8. Suggested backend entities

See snippets:

- `snippets/backend/AgentProfile.cs`
- `snippets/backend/AgentRun.cs`
- `snippets/backend/AgentRunService.cs`
- `snippets/backend/AgentsController.cs`

## 9. Knowledge layer recommendation

Because Taskdeck is local-first and SQLite-backed, the first knowledge system should not be an external vector DB.

### Start with

- `KnowledgeDocument`
- `KnowledgeChunk`
- SQLite FTS5-based full-text search
- deterministic metadata filters (`boardId`, `documentType`, `source`)

### Only later consider

- embedding search
- reranking
- external vector stores

This avoids heavy operational complexity while still unlocking useful agent context retrieval.

## 10. Tool registry recommendation

Right now capabilities are spread across:

- automation planner/executor
- chat
- queue
- ops CLI
- imports
- webhooks
- direct board/card services

Introduce an application-level registry:

- `ITaskdeckTool`
- `ITaskdeckToolRegistry`

Example tools:

- `create_card`
- `move_card`
- `rename_board`
- `apply_starter_pack`
- `create_capture`
- `triage_capture`
- `run_ops_template`
- `import_csv_contacts`

This unifies:

- agent runs
- chat proposal generation
- future MCP exposure
- testability

## 11. Policy engine recommendation

Reuse the proposal-first safety stance.
Add a dedicated policy service:

- `IAgentPolicyEvaluator`

It should answer questions like:

- is this tool allowed?
- does this action require review?
- can this risk level auto-apply?
- is schedule currently allowed?

## 12. Eventing and observability

For agent and product flows, publish structured audit/telemetry events such as:

- `workspace.home.viewed`
- `capture.created`
- `capture.triage.started`
- `proposal.summary.generated`
- `proposal.approved`
- `agent.run.started`
- `agent.run.step.completed`
- `agent.run.proposal.created`
- `agent.run.completed`

These should appear in telemetry and, where helpful, audit/history.

## 13. API design rule

If a frontend page needs 5+ calls and local joins to become useful, add an aggregated API.
That is exactly what `Home`, `Today`, and `Review` need.

## 14. Avoid backend anti-patterns

Do not:

- keep adding product logic into `AutomationQueueView`-style low-level pathways
- make Chat the only doorway for future agent work
- overload `LlmRequest` forever as the only asynchronous job model
- store raw prompts/responses everywhere without redaction policy

Do:

- introduce agent run entities separately
- keep queue for explicit instruction ingestion
- keep capture for messy intake
- keep proposals for safe mutation


---

# Agent Workspace Architecture

This is the main future-facing document in the bundle.
It explains how to grow Taskdeck into a broad autonomous workspace without breaking the current product.

## Agent workspace principle

Taskdeck should become a place where agents do visible, scoped work — not a place where chat guesses and mutates things off to the side.

## Canonical agent stack

### Layer 1 — Context

What the agent can see:

- boards/cards/labels/comments
- captures
- proposals
- notifications/activity (read-only in many cases)
- knowledge documents
- imports/webhook events
- previous run artifacts

### Layer 2 — Policy

What the agent is allowed to do:

- board scope / workspace scope
- allowed tools
- auto-apply rules
- max steps / budgets
- schedule windows
- privacy constraints

### Layer 3 — Runtime

How a run proceeds:

1. gather context
2. select actions
3. call tools or planner
4. create proposal / artifact / summary
5. wait for review or auto-apply if policy permits
6. finish with trace and outputs

### Layer 4 — Surfaces

How the user experiences it:

- agents page
- runs page
- run detail trace
- proposal linkage
- board-linked assistant panels
- inbox triage assistants

## Agent templates to support first

Do not start with “general autonomous super-agent”.
Start with narrow, legible assistants.

### 1. Inbox triage assistant

Goal:
- read new captures
- cluster or translate them into proposals
- surface ambiguous items for human review

### 2. Sprint assistant

Goal:
- summarize sprint board status
- propose moving stale cards / flag blocked work
- generate review proposals or planning notes

### 3. Research assistant

Goal:
- summarize imported notes or linked documents
- generate board-ready tasks or outline cards

### 4. Content assistant

Goal:
- turn rough notes into editorial cards
- suggest due dates, review states, and publish cadence

### 5. Support triage assistant

Goal:
- turn incoming support captures into categorized review proposals

These all map to current Taskdeck strengths.

## Agent data model

### AgentProfile

Fields:

- `Id`
- `UserId`
- `Name`
- `Description`
- `TemplateKey`
- `ScopeType` (`Workspace`, `Board`)
- `ScopeBoardId`
- `DefaultModel`
- `IsEnabled`
- `PolicyJson` or normalized `AgentPolicy` relation
- `CreatedAt`, `UpdatedAt`

### AgentPolicy

Fields:

- `AllowedToolKeys`
- `RequireReviewAboveRisk`
- `AllowAutoApplyLowRisk`
- `MaxStepsPerRun`
- `MaxProposalsPerRun`
- `MaxTokensPerRun`
- `QuietHoursJson`
- `AllowedTriggerTypes`

### AgentRun

Fields:

- `Id`
- `AgentProfileId`
- `UserId`
- `BoardId`
- `TriggerType` (`Manual`, `Schedule`, `Capture`, `Webhook`, `Import`)
- `Objective`
- `Status`
- `StartedAt`, `CompletedAt`
- `ProposalId` (nullable)
- `Summary`
- `FailureReason`
- `StepsExecuted`
- `TokensUsed`
- `ApproxCostUsd`

### AgentRunEvent

Fields:

- `Id`
- `AgentRunId`
- `EventType`
- `Summary`
- `ToolKey`
- `TargetEntityType`
- `TargetEntityId`
- `JsonPayload`
- `CreatedAt`

### AgentArtifact (later)

Fields:

- `Id`
- `AgentRunId`
- `ArtifactType` (`summary`, `report`, `draft`, `checklist`, `note`)
- `Title`
- `Content`
- `MetadataJson`

## Agent run lifecycle

```text
queued
  -> gathering_context
  -> planning
  -> tool_running (0..n times)
  -> proposal_created | artifact_created
  -> waiting_for_review | applying
  -> completed | failed | cancelled
```

### Important rule

Even if the underlying model uses many internal steps, the product surface should collapse those into a small, readable lifecycle.

## Tool model

Agents should act through a typed tool registry.

### Suggested tool categories

#### Project tools
- create board
- rename board
- archive/unarchive board
- apply starter pack

#### Card tools
- create card
- update card
- move card
- comment on card
- archive card

#### Intake tools
- create capture
- triage capture
- ignore capture

#### Review tools
- create proposal
- approve proposal (rare and policy-bound)
- execute proposal (rare and policy-bound)

#### Operations tools
- run ops template
- query logs (read-only)

#### Knowledge tools
- create note/document
- search knowledge
- attach note to board or run

#### Integration tools
- import csv profile
- emit outbound webhook event

## Run detail UI

A run detail page should show:

- status badge
- objective
- scope
- trigger
- started/completed time
- proposal linkage or artifact linkage
- event timeline
- tool calls with short summaries
- token/budget counters
- errors and retry actions

### What not to show

- raw hidden chain-of-thought
- full secret-bearing prompts
- noisy unstructured logs as the primary view

## Trigger model

### Manual trigger

User clicks `Run`.

### Capture trigger

A new capture item or triage result triggers an agent run.

### Scheduled trigger

Run on a schedule for hygiene tasks.

### Webhook trigger

External system sends an event, which becomes a run input.

### Import trigger

External import completes and triggers summarization or planning.

## Auto-apply policy

The safest path is:

- default: no auto-apply
- allowed later only for low-risk hygiene tools and only with explicit policy

Examples of maybe-acceptable later auto-apply:

- move stale cards between agreed columns
- add missing labels
- create summary note artifacts

Examples that should stay review-first:

- board rename
- destructive archive/delete-style actions
- bulk task generation from ambiguous inputs
- external sync writes to third-party systems

## Agent templates as starter packs

Use the starter-pack mindset for agents too.

Each template should include:

- template key
- description
- recommended scope
- recommended tools
- review policy defaults
- example run objective
- starter board/project blueprint where helpful

## Suggested first agent APIs

- `GET /api/agents`
- `POST /api/agents`
- `GET /api/agents/{id}`
- `PATCH /api/agents/{id}`
- `POST /api/agents/{id}/runs`
- `GET /api/agent-runs`
- `GET /api/agent-runs/{id}`
- `POST /api/agent-runs/{id}/cancel`
- `GET /api/agent-runs/{id}/events`

## Suggested implementation order

1. agent entities + migrations
2. create/list/update agent profiles
3. create manual agent runs
4. run event logging
5. run detail page
6. template agents
7. triggers/schedules
8. knowledge retrieval
9. controlled auto-apply rules

## Why this architecture fits Taskdeck

Because it does not replace your current loop.
It formalizes it.

Today:
- capture -> proposal -> board

Tomorrow:
- agent observes capture/board -> creates proposal/artifact -> user reviews -> board updates

That is the same trust model, just with a richer runtime around it.


---

# Integrations, Knowledge, and Autonomy

## Existing assets worth exploiting

Taskdeck already has several underused expansion points:

- external import adapters
- outbound webhooks
- chat
- queue
- ops templates
- scenario harness / demo director
- MCP tooling guidance

These are not random extras.
They are the beginnings of a real integration and agent platform.

## Integration strategy

Use a three-bucket model:

### 1. Inbound capture connectors

These produce capture items or documents.

Examples:

- browser clipper
- email forwarder
- markdown file drop
- GitHub issue ingestion
- meeting transcript import
- Slack/Discord note forward

### 2. Context connectors

These enrich the workspace but do not directly mutate boards.

Examples:

- docs/notes import
- repository summary import
- calendar/event context
- CRM/contact import

### 3. Outbound connectors

These emit events or sync approved changes.

Examples:

- outbound webhooks
- Slack/Discord notifications
- email digest summaries
- GitHub issue sync (later)

## Connector design rule

### Never let connectors bypass the review model by default

Inbound connectors should usually create:

- capture items
- documents
- agent triggers
- proposals

Not direct board mutation.

## Knowledge layer design

### What knowledge is for

Knowledge is not “stuff you imported because AI apps import stuff”.
It is for:

- project briefs
- meeting notes
- research notes
- docs snippets
- support patterns
- personal reference material

### First implementation recommendation

Start with a lightweight knowledge document model.

#### `KnowledgeDocument`
- `Id`
- `UserId`
- `BoardId?`
- `Title`
- `Content`
- `DocumentType`
- `Source`
- `SourceReference`
- `CreatedAt`, `UpdatedAt`

#### `KnowledgeChunk`
- `Id`
- `KnowledgeDocumentId`
- `ChunkIndex`
- `Text`
- `TextHash`
- `MetadataJson`

### Search implementation recommendation

First version:

- SQLite FTS5 over chunks or documents
- metadata filters
- score + excerpt return

Later optional version:

- embedding generation
- hybrid retrieve (FTS + vector)
- rerank

This keeps the first step local-first and operationally light.

## Example connectors to build first

### Browser clipper

User story:
- I clip an article/snippet/page and it arrives in Inbox or Knowledge.

Recommended behavior:
- clip -> capture or document
- agent/triage may generate review proposal later

### GitHub issue import

User story:
- import my own GitHub issues into a board or review queue.

Recommended behavior:
- import -> dry-run preview -> proposal or direct card creation only after confirmation

### Meeting notes import

User story:
- paste or upload notes/transcript and get board-ready tasks.

Recommended behavior:
- import -> knowledge doc + optional capture item -> triage proposal

### Email forward / address

User story:
- forward an email to Taskdeck and it shows up in Inbox.

Recommended behavior:
- email becomes a capture item with source metadata

## Autonomy policy model

Broad autonomous behavior needs visible policy controls.

### Policy dimensions

- scope
- allowed tools
- review thresholds
- trigger sources
- quiet hours
- token/cost budget
- run concurrency
- external-write permissions

### Default policy recommendation

- no external writes
- no auto-apply
- board or workspace scope must be explicit
- max 10 steps per run
- max 1 proposal per run initially

## Board-scoped assistant panels

One of the highest-value surfaces you can add is a board-scoped assistant panel.

### It should support

- summarize board state
- suggest next actions
- generate proposal from board context
- search related knowledge docs
- run narrow assistant templates

### It should not support initially

- uncontrolled general-purpose command execution
- hidden auto-apply behavior

## Knowledge and agent relationship

Agents need context, but context should be explicit and inspectable.

A good run should be able to show:

- documents consulted
- capture items referenced
- board entities referenced
- tool calls made

This becomes a trust and debugging feature.

## Using existing Ops and MCP surfaces

Ops and MCP are already part of the repo’s operating model.
Do not make them the main end-user abstraction.

Instead:

- expose them as advanced tooling
- let agents use selected tools from a registry
- keep human-facing agent/product surfaces simpler than the raw ops layer

## Scenarios to add

### Human-first scenarios

- first-run onboarding
- solo weekly planning
- sprint board bootstrap from checklist
- content batch planning from notes
- support issue capture and triage

### Agent scenarios

- inbox triage agent run
- scheduled sprint hygiene run
- research assistant run over imported notes
- content assistant run that creates a review proposal
- support summarizer run after import/webhook

See `snippets/scenarios/novice-first-first-run.json` for a starting shape.


---

# Testing, Metrics, and Operations

The product is now strong enough that quality work should shift from “does the code exist?” to “does the experience remain coherent under change?”.

## Recommended quality stack

### 1. Deterministic product smoke

Keep this as required:

- register/login
- create first project
- capture one item
- review one proposal
- execute it
- verify board result

This is the real P0 smoke test.

### 2. Scenario-driven acceptance tests

Keep expanding JSON scenarios and demo director.
Use them as acceptance fixtures.

### 3. Live-provider supervised tests

Keep these opt-in and non-blocking unless you have stable environment support.
Use them for:

- prompt/schema breakage detection
- agent run realism
- capture triage quality

### 4. Manual product walkthroughs

Run a short weekly dogfood protocol and record friction.

## Metrics that matter now

### Novice-first product metrics

- time to first value (from register/login to first board mutation)
- capture save time
- capture -> proposal latency
- proposal review -> execution latency
- proposal execution success rate
- inbox triage completion rate
- percent of sessions using Home or Today successfully

### Agent workspace metrics

- runs started / completed / failed
- proposal creation rate from runs
- average run steps
- average tokens/cost per run
- auto-apply rate by risk/tool
- human override/reject rate

### UX quality metrics

- page-level empty-state dwell time
- board picker usage vs raw ID fallbacks
- “failed with unreadable error” events
- navigation backtracks between pages in a short session

## Product telemetry suggestions

Emit events like:

- `home_loaded`
- `today_loaded`
- `capture_modal_opened`
- `capture_created`
- `capture_triage_clicked`
- `proposal_opened`
- `proposal_approved`
- `proposal_executed`
- `board_action_capture_here_clicked`
- `workspace_mode_changed`
- `agent_run_started`
- `agent_run_completed`
- `agent_run_failed`

Keep payloads privacy-safe and avoid raw text content.

## Launch criteria for a polished novice-first beta

You can call it polished enough for novice testing when:

1. Home exists and is the default landing page.
2. Today exists and is useful.
3. Review exists and proposals are readable.
4. No common flow requires raw IDs.
5. Every main page has a helpful empty state.
6. A new user can create first value in <2 minutes.
7. The daily dogfood loop is sustainable for a week.

## Launch criteria for agent workspace alpha

You can call it an agent workspace when:

1. agents can be created and scoped
2. runs are first-class and inspectable
3. runs can create proposals or artifacts
4. policies exist and are enforced
5. traces exist and are readable
6. at least 2 narrow assistant templates are useful in practice

## Suggested test matrix

### Frontend component/view tests

- Home view states
- Today view states
- Review view summary + actions
- workspace mode nav changes
- proposal summary card
- board action rail
- board picker/search selector
- onboarding checklist
- agent run detail timeline (later)

### Backend unit/application tests

- workspace summary query service
- proposal summary service
- agent policy evaluator
- agent run service state transitions
- knowledge search query service
- home/today aggregation edge cases

### API integration tests

- `/api/workspace/home`
- `/api/workspace/today`
- `/api/workspace/review/summary`
- `/api/agents/*`
- `/api/agent-runs/*`
- `/api/knowledge/*`

### Playwright acceptance flows

- first-run golden path
- daily review path
- board-scoped assistant path
- agent run creates proposal path
- knowledge import -> assistant -> proposal path

## Operational guidance

### Demo director becomes more than demo tooling

Treat it as:

- stakeholder recorder
- acceptance test artifact generator
- regression smoke evidence
- scenario benchmark harness

### Add a report mode

A future step should render a static HTML report from `run-summary.json`, `snapshot.json`, and screenshots.
That will give you a single artifact for demos, CI, and manual review.

## Performance hotspots to watch

- inbox lists over hundreds/thousands of items
- activity feed long histories
- proposal list filters and diff loading
- board lane rendering with many cards
- agent run timelines and events
- knowledge search result ranking

Use pagination, virtualization, and coarse summaries before chasing exotic optimizations.


---

# Seeded Issues Ready to Create

This file is written as ready-to-paste issue material.
Each issue includes scope, acceptance criteria, and verification hints.

## Suggested epics

- `EPIC A` — Novice-first shell and first-run productization
- `EPIC B` — Review/proposal UX and board-centered daily workflow
- `EPIC C` — Agent workspace foundation
- `EPIC D` — Knowledge and integrations
- `EPIC E` — Testing, docs, and help center maturity

---

## EPIC A — Novice-first shell and first-run productization

### Issue A1 — Add workspace mode preference (`guided`, `workbench`, `agent`)

**Why**
Taskdeck needs a first-class presentation mode instead of relying only on feature flags.

**Scope**
- add persisted workspace mode store on frontend
- add server-side user preference field/API
- route defaulting and nav filtering based on mode

**Acceptance criteria**
- user can switch between `guided`, `workbench`, and `agent`
- selection persists across refresh and login
- guided mode hides advanced surfaces by default
- existing advanced routes remain directly accessible for compatibility

**Verification**
- frontend unit tests for store and nav rendering
- API integration test for persisted preference
- manual test: switch mode, refresh, log out/in

---

### Issue A2 — Add `Home` route and workspace summary endpoint

**Why**
Boards list is not a product home.

**Scope**
- add `/workspace/home`
- add backend `GET /api/workspace/home`
- populate recent boards, counts, onboarding state, and key CTAs

**Acceptance criteria**
- post-login default route becomes `/workspace/home` in guided mode
- home shows inbox, review, blocked, and recent boards summary
- first-run users see a start-here state instead of an empty resource listing

**Verification**
- API integration coverage for summary endpoint
- view tests for first-run and populated states
- Playwright smoke path starts from Home

---

### Issue A3 — Add `Today` page and aggregated agenda endpoint

**Why**
Taskdeck needs a daily-use surface, not just project surfaces.

**Scope**
- add `/workspace/today`
- add backend `GET /api/workspace/today`
- show due, overdue, blocked, review-needed, and inbox-needed sections

**Acceptance criteria**
- today page loads in under one screen with clear sections
- every section deep-links to a next action
- empty state still explains purpose and next steps

**Verification**
- API tests for agenda summarization
- view tests for each section state
- manual dogfood pass for morning routine

---

### Issue A4 — Add first-run onboarding checklist and project creation wizard

**Why**
The app currently assumes too much context from the user.

**Scope**
- checklist state per user
- first-run project wizard
- starter-pack shortcuts by project type

**Acceptance criteria**
- new user can create a first project without opening board settings or advanced pages
- onboarding checklist progresses after key events
- checklist can be dismissed or reopened

**Verification**
- frontend unit tests for checklist state
- Playwright first-run flow

---

### Issue A5 — Replace blind empty states on main pages with action-oriented help blocks

**Why**
Current empty states often tell the user nothing useful.

**Scope**
- Home, Today, Inbox, Review, Activity, Notifications, Queue, Access, Archive
- reusable empty/help state component

**Acceptance criteria**
- every main page explains what it is for when empty
- every main page includes 1–3 concrete suggested next actions
- guided mode pages never show a dead-end empty surface

**Verification**
- component snapshots or view tests
- manual walkthrough from fresh DB

---

### Issue A6 — Add board picker/search selectors where raw board IDs are still required

**Why**
Raw IDs are acceptable for debugging, not for common UX.

**Scope**
- queue composer
- chat board targeting
- access page
- activity filters where applicable

**Acceptance criteria**
- users can choose board by searchable name in common flows
- copy-ID affordance remains available but optional
- invalid-ID error paths are no longer part of the happy path

**Verification**
- view tests for selector interactions
- manual queue/chat/access flows without using board IDs

---

## EPIC B — Review/proposal UX and board-centered workflow

### Issue B1 — Add `/workspace/review` alias and make Review the primary automation surface

**Why**
Queue is an implementation detail for most users.

**Scope**
- add review route alias
- nav label becomes `Review`
- proposals become the main landing surface

**Acceptance criteria**
- automation nav lands on review, not queue
- queue remains available as advanced sub-route
- users can complete proposal review without learning queue concepts

**Verification**
- router tests
- manual nav verification

---

### Issue B2 — Add proposal summary service and readable proposal cards

**Why**
Proposal diffs exist, but summaries are not product-grade yet.

**Scope**
- backend summary generation service
- frontend `ProposalSummaryCard`
- affected entity chips and board deep links

**Acceptance criteria**
- proposal cards show a readable summary sentence
- source, risk, and affected entities are visible
- users can open affected board/card directly from review

**Verification**
- backend unit tests for summary generation
- frontend component tests

---

### Issue B3 — Add board action rail (`Capture here`, `Ask assistant`, `Review proposals`, `Add card`)

**Why**
The board needs to feel like the work surface, not just the result surface.

**Scope**
- board header action rail
- pre-scoped capture/chat/review actions

**Acceptance criteria**
- capture created from board is automatically board-scoped
- review action opens board-filtered review list
- assistant chat opens board-scoped session by default

**Verification**
- manual board flow
- Playwright board-centered usage test

---

### Issue B4 — Add deep links from notifications, inbox, and runs into proposals and affected entities

**Why**
Related surfaces currently feel more disconnected than they should.

**Scope**
- notification source linking
- inbox proposal linking improvements
- future run-to-proposal linking

**Acceptance criteria**
- every notification type has a sensible deep link
- inbox items with proposal provenance link to review and board
- proposal cards link back to source when possible

**Verification**
- manual seeded scenario walkthrough
- frontend view tests for representative types

---

### Issue B5 — Add global search across boards, cards, captures, and proposals

**Why**
The command palette should become a real navigation and recovery tool.

**Scope**
- backend search endpoint or endpoints
- global palette grouping and keyboard navigation

**Acceptance criteria**
- users can find a board/card/capture/proposal from global search
- result groups are labeled clearly
- search remains keyboard-first

**Verification**
- view tests for command palette search
- API tests if search endpoint added

---

### Issue B6 — Add `Today`-driven “My work” and “blocked work” shortcuts from board cards and notifications

**Why**
Daily utility depends on fast movement between summary pages and board work.

**Scope**
- today chips/links
- notification actions
- board badges where helpful

**Acceptance criteria**
- user can go from Today to exact work context with one click
- blocked and due items are easy to reach

**Verification**
- manual navigation check
- view tests

---

## EPIC C — Agent workspace foundation

### Issue C1 — Add agent profile domain model and CRUD API

**Why**
Agents need a first-class identity and scope.

**Scope**
- add `AgentProfile` entity
- CRUD endpoints
- scope (`workspace` or `board`) and template key

**Acceptance criteria**
- user can create, list, update, disable agents
- agent can be board-scoped or workspace-scoped
- policy defaults can be stored per agent

**Verification**
- application tests
- API integration tests

---

### Issue C2 — Add agent run entity and manual run execution API

**Why**
Agent activity should be visible as runs, not hidden in chat or queue state.

**Scope**
- add `AgentRun` entity and statuses
- add `POST /api/agents/{id}/runs`
- return run status and summary

**Acceptance criteria**
- user can start a run manually
- run lifecycle persists and is queryable
- failed runs record readable failure reason

**Verification**
- unit/application tests for state transitions
- API integration tests

---

### Issue C3 — Add agent run events / trace timeline

**Why**
Trust requires inspectable traces.

**Scope**
- `AgentRunEvent` entity
- run event API
- frontend run detail timeline later can depend on this

**Acceptance criteria**
- run records context gathering, tool usage, proposal creation, completion/failure
- timeline is ordered and queryable
- sensitive internal reasoning is not exposed by default

**Verification**
- application tests for event capture
- API integration tests

---

### Issue C4 — Add tool registry abstraction for agent/chat/runtime use

**Why**
Capabilities are currently spread across services with no unifying runtime abstraction.

**Scope**
- `ITaskdeckTool`
- `ITaskdeckToolRegistry`
- initial board/card/capture/proposal tools

**Acceptance criteria**
- agent runtime can invoke tools through the registry
- tool metadata includes key, description, and risk classification
- registry is testable without UI/runtime bootstrapping

**Verification**
- unit tests for registry resolution and invocation

---

### Issue C5 — Add agent policy evaluator and review thresholds

**Why**
Agent autonomy without policies will become unmanageable.

**Scope**
- policy model or JSON contract
- evaluation service
- thresholds for tool allowlist and review requirement

**Acceptance criteria**
- runtime can answer “is tool allowed?” and “must this be review-first?”
- low-risk auto-apply remains off by default
- policy decisions are traceable in run events

**Verification**
- unit tests for policy evaluation matrix

---

### Issue C6 — Add first narrow agent template: Inbox triage assistant

**Why**
Start with a useful bounded assistant aligned with current strengths.

**Scope**
- template metadata
- run logic that reads captures and produces proposals
- no broad autonomous workspace claims yet

**Acceptance criteria**
- user can create an inbox triage agent from template
- a run can create a proposal from new captures
- run is inspectable and proposal-linked

**Verification**
- scenario-based acceptance run
- optional live-provider smoke test

---

### Issue C7 — Add agent views (`Agents`, `Runs`, `Run detail`) in Agent mode

**Why**
The domain model needs a usable product surface.

**Scope**
- list/create/edit agents
- list runs
- run detail timeline

**Acceptance criteria**
- guided/workbench mode can hide these
- agent mode exposes them clearly
- run detail shows status, scope, proposal/artifact linkage, and events

**Verification**
- view tests and Playwright run-detail path

---

## EPIC D — Knowledge and integrations

### Issue D1 — Add knowledge document model and board/workspace notes UI

**Why**
Agents and humans both need durable context beyond cards.

**Scope**
- `KnowledgeDocument` entity
- create/list/read basic notes
- board-scoped and workspace-scoped docs

**Acceptance criteria**
- user can create notes/docs attached to a board or workspace
- docs are searchable later
- docs can be referenced by future agent runs

**Verification**
- API integration tests
- frontend note CRUD tests

---

### Issue D2 — Add SQLite FTS-backed knowledge search

**Why**
Searchable context is the minimum viable knowledge layer.

**Scope**
- FTS index/table strategy
- search endpoint with excerpts and filters

**Acceptance criteria**
- documents can be searched by content
- results return score + excerpt + scope metadata
- implementation stays local-first and deterministic

**Verification**
- application tests for index/search behavior

---

### Issue D3 — Add inbound browser clipper / web clip capture pathway

**Why**
Capture should be available outside the app.

**Scope**
- design contract for browser clipper or import endpoint
- first implementation can be a simple authenticated HTTP intake

**Acceptance criteria**
- clipped content arrives as capture item or knowledge document
- source metadata is preserved
- no direct board mutation on ingest by default

**Verification**
- API tests and manual clip simulation

---

### Issue D4 — Expand external import adapters with a “notes/transcript” profile

**Why**
Current import capability should support knowledge-heavy workflows, not only CRM-style imports.

**Scope**
- add provider/profile for note-style imports
- route imported text into knowledge or capture flow

**Acceptance criteria**
- user can import note/transcript text through the import subsystem
- dry-run/apply semantics remain clear
- imported content can be triaged later

**Verification**
- adapter tests
- manual scenario import

---

### Issue D5 — Add integrations management view and connector registry concept

**Why**
As connectors grow, they need a discoverable home.

**Scope**
- `Integrations` page
- registry of connector types and status
- existing imports/webhooks surfaced here

**Acceptance criteria**
- imports and webhooks no longer feel buried
- future connectors can register metadata for display
- guided mode can hide this behind advanced navigation

**Verification**
- view tests and manual walkthrough

---

## EPIC E — Testing, docs, and help center maturity

### Issue E1 — Add first-run golden path Playwright test as required smoke

**Why**
The product needs a required test for the real user story, not only infrastructure surfaces.

**Scope**
- register/login
- create project
- capture note
- triage/review/execute
- verify board result

**Acceptance criteria**
- deterministic smoke path runs against clean DB
- failure artifacts are uploaded in CI

**Verification**
- Playwright lane green in CI

---

### Issue E2 — Add in-app help center and “What is this?” dismissible help blocks

**Why**
The docs are stronger than the UI right now.

**Scope**
- help center route or panel
- page-specific help blocks
- link to manual sections

**Acceptance criteria**
- every main page can show contextual help
- help blocks are dismissible and persistent per user
- novice users can recover from confusion without leaving the app

**Verification**
- frontend view tests
- manual first-run walkthrough

---

### Issue E3 — Add proposal-first product manual and page-level docs sync governance

**Why**
As the app grows, docs need product-shaped organization.

**Scope**
- add user manual sections aligned to Home/Today/Review/Projects/Agents
- ensure docs index links all canonical manuals

**Acceptance criteria**
- docs structure matches actual top-level product navigation
- manual includes novice and advanced sections
- docs governance checks updated if canonical set changes

**Verification**
- docs governance checks
- manual review

---

### Issue E4 — Add telemetry dashboard spec and launch criteria doc for novice beta + agent alpha

**Why**
Product maturity needs explicit exit criteria.

**Scope**
- define key metrics
- add dashboard event list / queries
- define launch gates for novice beta and agent alpha

**Acceptance criteria**
- core events are documented
- launch criteria are explicit and measurable
- telemetry naming stays privacy-safe

**Verification**
- docs review and telemetry smoke


---

# Comprehensive Manual Blueprint

Taskdeck is now large enough that it needs a real user manual architecture, not only scattered docs.

## Manual goal

The manual should answer three different kinds of questions:

1. **What is this product for?**
2. **How do I use it for real work?**
3. **How do I use the advanced or future-facing surfaces safely?**

## Suggested manual structure

### Section 1 — Start here

Audience:
- new users

Contents:
- what Taskdeck is in one page
- the 2-minute first value path
- glossary of Projects / Inbox / Review / Today / Agents

### Section 2 — Daily use

Audience:
- novice and regular users

Contents:
- Home
- Today
- Inbox
- Review
- Projects
- daily and weekly routines

### Section 3 — Working inside projects

Audience:
- all users

Contents:
- board/project basics
- cards, labels, comments, due dates, blocked state
- starter packs
- common project templates

### Section 4 — Capture and review

Audience:
- all users

Contents:
- capture sources
- triage
- proposal review
- proposal risk
- provenance and trust model

### Section 5 — Advanced automation and diagnostics

Audience:
- power users

Contents:
- queue
- chat
- activity
- notifications
- ops
- archive
- access

### Section 6 — Agents

Audience:
- advanced users and future customers

Contents:
- what agents are
- what a run is
- policies and review thresholds
- agent templates
- reading run traces

### Section 7 — Integrations and knowledge

Audience:
- advanced users

Contents:
- imports
- webhooks
- knowledge docs
- search
- connector model

### Section 8 — Recipes

Audience:
- everyone

Examples:
- use Taskdeck for engineering sprint planning
- use Taskdeck for content planning
- use Taskdeck for support triage
- use Taskdeck for learning/research
- use Taskdeck with an inbox triage assistant

### Section 9 — Troubleshooting

Audience:
- everyone

Contents:
- why is this page empty?
- why did triage fail?
- why do I need review before apply?
- what does “risk” mean?
- where are advanced pages?
- how do I enable demo/sample workspace?

## Writing rules for the manual

- explain the user goal before the mechanism
- use screenshots and examples heavily
- prefer examples over abstract definitions
- isolate advanced sections clearly
- do not assume familiarity with “proposal-first” language; explain it plainly

## Product docs map recommendation

### Keep in `docs/`

- `USER_MANUAL.md` (or split manual index + chapters)
- `DEMO_PLAYBOOK.md`
- `DOGFOODING_GUIDE.md`
- `TESTING_GUIDE.md`
- `STATUS.md`
- `IMPLEMENTATION_MASTERPLAN.md`

### Split the manual when it gets too large

Recommended chapter files:

- `docs/manual/01_start_here.md`
- `docs/manual/02_home_and_today.md`
- `docs/manual/03_projects_and_cards.md`
- `docs/manual/04_inbox_and_review.md`
- `docs/manual/05_advanced_automation.md`
- `docs/manual/06_agents.md`
- `docs/manual/07_integrations_and_knowledge.md`
- `docs/manual/08_recipes.md`
- `docs/manual/09_troubleshooting.md`

## In-app help mapping

The manual should have short in-app summaries.

Example mapping:

- Home page -> manual chapter 1/2
- Inbox page -> chapter 4
- Review page -> chapter 4
- Queue page -> chapter 5
- Agents page -> chapter 6
- Integrations page -> chapter 7

## Suggested first manual improvements

1. rewrite manual around top-level navigation instead of implementation slices
2. add “When should I use this page?” at top of every section
3. add “Common mistakes” at end of every section
4. add “See also” links between related sections

## Future documentation work

- stakeholder presentation scripts
- recipe packs for vertical use cases
- agent template cookbook
- connector developer guide
- troubleshooting index keyed by error message / state


---

# Phased Roadmap and Release Plan

## Recommended sequencing

This roadmap assumes you want visible product improvement quickly while keeping architectural debt under control.

## Track 1 — polish the current product first

### Phase 0 — 1 to 2 weeks

Goal:
- make the app teach itself

Ship:
- workspace mode (`guided`, `workbench`, `agent`)
- Home route + basic summary endpoint
- Review alias in nav
- action-oriented empty states
- board picker replacing raw IDs in common flows

Do not ship in this phase:
- agent entities
- knowledge search
- integration UI

### Phase 1 — 2 to 4 weeks

Goal:
- make it useful every day

Ship:
- Today page
- board action rail
- proposal summary cards
- first-run wizard + onboarding checklist
- command palette search upgrade

Exit criteria:
- daily use no longer feels like wandering across scaffolding pages
- first-time user can understand product without reading docs first

## Track 2 — add the agent substrate

### Phase 2 — 2 to 4 weeks

Goal:
- turn “assistant behavior” into first-class runtime primitives

Ship:
- `AgentProfile`
- `AgentRun`
- `AgentRunEvent`
- create/list/manual-run APIs
- Agents and Runs UI (simple)

Non-goals:
- broad autonomy
- external sync writes
- generalized multi-agent orchestration

### Phase 3 — 2 to 4 weeks

Goal:
- make runs useful, not just visible

Ship:
- tool registry
- policy evaluator
- first template assistant (`Inbox triage assistant`)
- run-to-proposal linking
- basic run detail timeline

Exit criteria:
- narrow agent template is genuinely useful in dogfooding
- user can inspect what happened and why

## Track 3 — add knowledge and integrations

### Phase 4 — 3 to 6 weeks

Goal:
- give agents and humans a durable context layer

Ship:
- knowledge documents
- SQLite FTS search
- integrations page
- note/transcript import profile
- browser clip or similar intake path

### Phase 5 — 3 to 6 weeks

Goal:
- supervised automation loops become practical

Ship:
- schedules/triggers
- limited low-risk auto-apply rules
- inbound connector -> capture/agent trigger paths
- static HTML run/demo report

## Release framing

### Release `R1` — novice-first beta

Must include:
- Home
- Today
- Review
- first-run wizard
- readable proposals
- board-centered action rail
- no raw ID requirements in common flows

### Release `R2` — agent foundation alpha

Must include:
- agent profiles
- runs
- run events/traces
- first template agent
- proposal linkage
- policy evaluator

### Release `R3` — knowledge/integrations alpha

Must include:
- searchable notes/docs
- integrations page
- at least 2 meaningful inbound context/capture paths

## Anti-roadmap guidance

Do not do these out of order:

- general autonomous multi-agent orchestration before run traces exist
- vector DB work before text search and knowledge documents exist
- broad connector wave before Home/Today/Review are good
- new surface proliferation before page-level help and empty states are fixed


---

# Risks, Non-Goals, and Decision Rules

## Primary risks

### Risk 1 — surface sprawl outruns user understanding

Symptom:
- more pages exist, but core value is still unclear

Mitigation:
- no new major surface without a clear golden-path reason
- require a “why would a novice ever use this?” answer

### Risk 2 — agent ambitions outrun trust model

Symptom:
- agents can do a lot, but users do not understand what happened

Mitigation:
- run entities and traces before broad automation
- keep proposal-first as default
- do not expose chain-of-thought as explanation

### Risk 3 — domain churn breaks the working core

Symptom:
- pressure to introduce separate task/project/subproject objects too early

Mitigation:
- keep board/card model stable until real evidence requires change

### Risk 4 — overbuilding knowledge infrastructure too early

Symptom:
- adding embeddings/vector services before the basic note/doc layer is useful

Mitigation:
- start with SQLite FTS and metadata filters

### Risk 5 — docs and product drift apart again

Symptom:
- docs explain workflows the UI does not support clearly

Mitigation:
- tie canonical manual sections to actual top-level navigation
- update docs whenever Home/Today/Review/Agents meaning changes

## Non-goals for the next major cycle

- replacing boards/cards with a new planning model
- shipping silent destructive autonomy
- turning Queue into the main end-user surface
- building a generalized app platform before first-run UX is fixed
- adding external vector infrastructure as a requirement

## Decision rules

### Rule A
If a feature makes demos better but makes the product harder to understand, it is not done.

### Rule B
If a feature needs internal IDs in the happy path, it is not novice-ready.

### Rule C
If a page is empty and offers no next step, it is incomplete.

### Rule D
If an agent action cannot be traced or linked to a proposal/artifact, it is not ready.

### Rule E
If a new concept cannot be explained in one sentence on its own page, it should not be top-level navigation yet.
