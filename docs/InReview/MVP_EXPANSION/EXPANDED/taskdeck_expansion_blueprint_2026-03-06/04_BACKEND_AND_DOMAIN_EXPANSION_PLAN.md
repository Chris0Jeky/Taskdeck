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
