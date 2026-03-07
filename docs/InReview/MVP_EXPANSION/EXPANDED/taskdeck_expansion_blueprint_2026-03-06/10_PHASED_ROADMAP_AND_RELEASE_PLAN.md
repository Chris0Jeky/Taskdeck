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
