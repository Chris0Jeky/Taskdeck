# Demo completeness assessment

## What exists now

The demo/tooling layer is no longer "some scripts". It is a small system.

### Runtime/demo commands
Current command surface in `frontend/taskdeck-web/package.json`:

- `demo:seed`
- `demo:run`
- `demo:autopilot`
- `demo:snapshot`
- `demo:director`
- `demo:director:smoke`

### Demo infrastructure
I found:

- 10 dedicated demo scripts (`scripts/demo-*.mjs`)
- JSON scenario support with schema
- 3 shipped JSON scenarios:
  - `engineering-sprint`
  - `support-triage`
  - `content-calendar`
- an HTTP walkthrough file for dev-like, UI-free exercise
- an opt-in stakeholder Playwright walkthrough
- director artifacts: README, summary, snapshot, trace, logs, screenshots, Playwright outputs

### Test surface around the demo tooling
Frontend test surface now includes dedicated specs for:

- director runtime/cleanup
- live-LLM env resolution
- scenario arg parsing
- scenario defaults
- shared demo helpers
- JSON scenario determinism
- legacy scenario parity
- port resolution
- server reuse policy
- multiple E2E flows, including stakeholder demo

That matters because it means the demo harness is no longer "best effort"; it has become part of the verified repo behavior.

## What the demo run proves

From the run archive metadata you shared, one director run produced:

- 11 screenshots
- 5 logs
- Playwright artifacts including video and trace
- `run-summary.json`
- `snapshot.json`
- `trace.ndjson`

The artifact names show a complete guided pass across:

1. Boards
2. Capture board
3. Card modal
4. Inbox
5. Automations / Proposals
6. Automations / Queue
7. queue submitted state
8. Ops
9. Activity
10. Notifications

That is exactly the kind of proof you originally needed: not just seeded data, but a reproducible, inspectable, narratively ordered walkthrough.

## How complete is the demo?

I would score it like this.

### 1. Technical demo completeness: 9/10
Strong because you now have:

- seeded state
- deterministic scenario setup
- optional LLM-driven flows
- autopilot
- artifact capture
- smoke mode
- explicit CI policy
- HTTP walkthrough
- screenshots + video + trace outputs

What stops it from being a 10:

- the best entry points are still mostly CLI/script driven
- the most "wow" demos still depend on a prepared operator
- there is not yet an in-product launcher/control panel for scenarios and agents

### 2. Guided stakeholder demo completeness: 8/10
Strong because:

- the presenter can drive a full story
- pages are populated
- there is evidence capture
- different scenarios give different narratives
- there is enough activity to make the product feel alive

What is still weak:

- the narrative is still a bit breadth-heavy
- the current clickthrough proves coverage more than it proves one tight product story
- some surfaces are still more "look, this exists too" than "this is why the user cares"

### 3. Self-serve demo completeness: 5.5/10
This is the real missing layer.

A user can be given the app and the seeded workspace, but the UI still does not sufficiently explain:

- what the main loop is
- what to do first
- when to use Queue vs Chat vs Inbox triage
- why Ops / Activity / Notifications exist in the user’s mental model

## The important distinction: demo infrastructure vs product UX

This is the single most important product diagnosis after the epic.

You now have **high-quality demo infrastructure**.
You still have **mid-quality product onboarding UX**.

That means the system is much better prepared for:

- engineering confidence
- demo repeatability
- stakeholder walkthroughs
- regression testing

than for:

- a cold user doing first-run exploration without guidance

## What I consider "complete demo"

A complete demo has three levels.

### Level 1: prepared walkthrough
A presenter can click through a believable world.

You have this.

### Level 2: replayable demo-as-test
A script can generate the world, drive the walkthrough, and emit artifacts.

You have this too.

### Level 3: self-serve discovery
A new user can understand the product by using the UI without reading internal docs or needing terminal commands.

You do not fully have this yet.

That is why I would say:

**Taskdeck now has a complete engineering demo system, but not yet a complete self-explaining product demo.**

## What is missing for a truly complete demo

### A. In-app "Demo Tools" or "Load Demo Workspace"
Right now the best demo path still starts outside the app.

For internal teams, that is fine.
For stakeholders, evaluators, and future contributors, it is not ideal.

I would add a dev/demo-only page with:

- Seed baseline workspace
- Run scenario
- Start/stop autopilot
- Open latest artifact folder
- Toggle advanced feature flags needed for the walkthrough

### B. A sharper main storyline
Your current system can show almost everything.
That is good for coverage.
It is not yet optimal for persuasion.

The main story should be:

1. capture messy input quickly
2. triage it
3. review the proposal
4. execute it
5. work the resulting task/card
6. show audit trail and notification as proof of trust/safety

Everything else should support that.

### C. Better "causal" linking in the UI
You have data provenance technically.
You still need more user-facing causality:

- from capture -> open proposal
- from proposal -> open affected board/card
- from queue request -> open created proposal
- from notification -> open the exact thing that changed

### D. A better visual board demo
The `DEMO: Capture Loop` board is good as a provenance board, but not the best hero board.

It currently communicates:
"things get created"

It does not as strongly communicate:
"this is an actively managed workflow"

For demos, use a board with cards distributed across multiple columns, not a backlog-heavy board alone.

## Best current use of the demo system

Use the director as three different assets, not one:

### 1. demo-as-proof
Evidence for yourself and future contributors that the system can produce a coherent world

### 2. demo-as-presentation
Stakeholder walkthrough with screenshots/video/trace

### 3. demo-as-regression
A deterministic smoke lane that fails when core narrative flows break

You now have the foundation for all three.

## Overall conclusion

If the original requirement was:

"I need the product to stop feeling empty and I need a way to demo it automatically"

then this is a successful delivery.

If the requirement is now:

"I want a stranger to open the app and instantly understand why Taskdeck matters"

then the next work is mostly UX, IA, and product narrative work, not more harness work.
