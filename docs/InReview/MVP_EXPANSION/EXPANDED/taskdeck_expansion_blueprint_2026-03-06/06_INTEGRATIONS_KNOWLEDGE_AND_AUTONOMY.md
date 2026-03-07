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
