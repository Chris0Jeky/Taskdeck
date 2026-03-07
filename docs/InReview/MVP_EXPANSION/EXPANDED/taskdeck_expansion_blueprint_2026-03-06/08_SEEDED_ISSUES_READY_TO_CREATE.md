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
