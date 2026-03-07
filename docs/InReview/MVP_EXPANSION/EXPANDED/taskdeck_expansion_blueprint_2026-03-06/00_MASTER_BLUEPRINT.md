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
