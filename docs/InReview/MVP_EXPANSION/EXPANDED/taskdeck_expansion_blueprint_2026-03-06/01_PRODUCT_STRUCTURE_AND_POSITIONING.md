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
