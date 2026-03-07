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
