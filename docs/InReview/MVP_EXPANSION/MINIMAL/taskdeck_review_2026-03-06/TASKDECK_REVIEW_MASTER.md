# Taskdeck post-demo-expansion review
Date: 2026-03-06

## Scope I reviewed

I reviewed the repository state in the zip, focusing on:

- product docs: `README.md`, `docs/STATUS.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`
- demo/dogfooding docs: `docs/product/DEMO_PLAYBOOK.md`, `docs/product/SCENARIOS.md`, `docs/product/DOGFOODING_GUIDE.md`, `docs/USER_MANUAL.md`, `docs/TESTING_GUIDE.md`
- frontend product surfaces: boards, board view, inbox, automations, chat, ops, activity, notifications, settings
- demo tooling: `demo:seed`, `demo:run`, `demo:autopilot`, `demo:snapshot`, `demo:director`
- tests around the harness and e2e flows
- the new screenshots you shared
- the demo-run archive metadata and artifact layout

## Executive summary

You solved the original demoability problem much better than you solved the original product-clarity problem.

That sounds harsher than it is. It is actually a good sign.

What has clearly improved:

- the repo now has a serious **demo/testing harness**
- the app no longer feels like empty scaffolding after a fresh start if you use the seeding flow
- you now have deterministic scenarios, an autopilot, a one-command director, traces, snapshots, smoke coverage, and a much better documentation spine
- the product thesis is now much clearer in the codebase and docs: **capture -> triage -> proposal -> apply**

What is still missing:

- the app itself still does not fully teach the user how to use it
- the best path is still mostly known by reading docs or running scripts, not by entering the UI cold
- some critical flows remain **technically possible but cognitively expensive**
- the "core story" is still spread across Boards, Inbox, Proposals, Queue, and Chat instead of being presented as one deliberate journey

## My high-level verdict

### As an engineering demo system
Strong.

This is now a **real demo platform**, not just ad hoc seed data.

### As a stakeholder-guided demo
Good.

A prepared presenter can show a coherent story and reliably produce evidence artifacts.

### As a self-serve first-run experience
Still incomplete.

A smart user can figure it out, but a fresh user is still too likely to think:
"Okay, there are many pages here. Which one is the main one? What am I actually supposed to do first?"

### As an MVP you can start using
Yes, conditionally.

You can already use it if you adopt one discipline:

1. quick capture first
2. triage later
3. review proposals explicitly
4. work the board

If you expect it to already feel like a polished end-user productivity app, it is not there yet.
If you treat it as a **developer-first execution workspace with safe automation**, it is already crossing into "useful now".

## The core insight

Originally, the problem was not mainly missing backend capability.
The problem was:

- no populated state
- no guided narrative
- no reliable demo setup
- no repeatable scenarios
- too much of the system only came alive after secondary events

The migration wave fixed the first four very well.

The next phase should be much more product-focused:

- reduce ambiguity
- collapse the number of decisions on first run
- make the golden path visually obvious
- turn board context into the main organizing principle

## My blunt product take

Right now Taskdeck is strongest when understood as:

**a local-first developer work intake system where AI/automation proposes changes instead of mutating your board silently**

That is a solid product identity.

It is weaker when understood as:

- a general-purpose autonomous agent workspace
- a broad team PM tool
- a polished end-user productivity app
- a "magic AI copilot" product

Those can come later. The MVP should stay narrower.

## What I would do next if I were driving the product

In order:

1. make the golden path unavoidable in the UI
2. make board context flow through every automation surface
3. improve proposal readability and "what happens next" affordances
4. make demo/self-serve mode accessible from inside the app
5. then add broader workflows and autonomy

## Files in this review package

- `01_DEMO_COMPLETENESS_ASSESSMENT.md`
- `02_GOLDEN_PATH_AND_PRODUCTIZATION.md`
- `03_DOGFOODING_AND_USEFUL_NOW.md`
- `04_SCENARIO_MATRIX_AND_TEST_PLAN.md`
- `05_MANUAL_AND_DOCS_STRATEGY.md`


---

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


---

# Golden path and productization plan

## The surface taxonomy you should adopt

Right now Taskdeck has many pages, but not all pages are equal in product importance.

You should explicitly think in three layers.

## Layer 1: core product surfaces
These define the MVP.

- Boards
- Board view
- Inbox
- Proposals
- Starter Packs
- Quick Capture modal
- optionally Chat, if framed carefully

If a user never opens any other page, the product should still feel usable.

## Layer 2: supporting trust surfaces
These help explain and validate the core loop.

- Notifications
- Activity
- comments and mentions
- presence/conflict hints

These are important, but secondary.

## Layer 3: operator/dev surfaces
These are valuable, but they are not the first-run product.

- Queue
- Ops
- Access
- Archive
- Export/Import
- direct endpoint explorer

These should not dominate the first impression.

## The current state

You have already moved in the right direction by:

- hiding some advanced surfaces by default through feature flags
- defaulting Automations to Proposals instead of Queue
- documenting the main flow
- creating seed/demo flows

But the UI itself still mostly behaves like a collection of pages rather than a guided system.

## The golden path I would implement

## Path A: first-time user golden path
This is the one that matters most.

### Step 1: land on a start screen, not a raw board list
After login, do not drop the user directly into a plain "My Boards" grid with no guidance.

Give them one of these:

- a dedicated `/workspace/home` route, or
- a hero/onboarding panel at the top of `BoardsListView`

That screen should answer:

- what Taskdeck is
- what the main loop is
- what I should do right now

### Step 2: present exactly three primary actions
The first-run screen should have three large calls to action:

- Quick Capture
- Create Board
- Load Demo Workspace

That is enough.

### Step 3: immediately show causality
After the first capture, route the user to Inbox with the created item selected.
When triage completes, show "Open Proposal".
After execute, show "Open Board".

Right now these links exist in pieces.
They need to become the obvious happy path.

## Path B: daily user golden path
For real use, the flow should be:

1. capture quickly from anywhere
2. process Inbox to zero
3. review proposals
4. execute accepted changes
5. work active board
6. use comments/mentions for context
7. occasionally inspect activity/notifications

That means the app should support these shortcuts in a first-class way:

- global quick capture
- Inbox badge/count
- proposal count in nav
- board-scoped actions from the current board
- one-click return to the active board after proposal execution

## Path C: stakeholder golden path
A demo should be even tighter than normal use.

Suggested order:

1. Quick Capture
2. Inbox selected item
3. Start triage
4. Open proposal
5. Approve + execute
6. open the board where the cards appeared
7. mention/collaboration proof
8. notification/activity proof
9. ops only if needed

Your current stakeholder flow is good breadth coverage, but I would tighten it around the causal chain above.

## Concrete UI changes I would make next

## 1. Add a start surface
Minimal version:
add a banner to `BoardsListView` when the user has zero or very few boards.

Better version:
add `/workspace/home` with:

- thesis statement
- Quick Capture button
- Create Board button
- Load Demo Workspace button
- "How Taskdeck works" 4-step explainer
- counts: Inbox, proposals needing review, unread notifications

## 2. Add board-scoped automation affordances
The current board page should become the main execution hub.

Add buttons like:

- Capture into this board
- Review proposals for this board
- Open chat for this board
- Add from automation
- View board activity

These should prefill the relevant context.
Users should not have to manually carry board identity between screens.

## 3. Replace raw board ID inputs with board pickers
This is one of the highest-leverage usability changes.

In the Queue composer, entering a GUID is still a developer affordance, not a product affordance.

Use:

- board picker by name
- board ID hidden as implementation detail
- prefill current board when coming from a board route

This alone would make Queue feel far less like scaffolding.

## 4. Make proposal cards more readable
Current proposals are functional, but still quite "system-shaped".

Improve them with:

- operation summaries rendered as bullets, not only raw diff text
- affected board/card links
- provenance summary in plain language
- risk explanation in human language
- strong primary CTA based on state:
  - pending -> Approve
  - approved -> Execute
  - applied -> Open Board

## 5. Improve empty states with next actions
The current app still has too many "No X found" states.

Each empty state should say:

- what this page is for
- why it is empty
- what to click next

Examples:

### Notifications empty state
"No notifications yet. Mentions and proposal outcomes appear here. Add a comment with @username or execute a proposal to see examples."

### Activity empty state
"No activity yet. Board changes, proposal execution, and comments create audit history. Open a demo board or make a change."

### Queue empty state
"Queue is the advanced instruction surface. Most users should start with Inbox or Chat."

## 6. Make quick capture board-aware
Current quick capture is workspace-global. That is good for friction reduction, but incomplete for real use.

I would support both:

- global capture with no board
- board-scoped capture from inside a board

That lets the product support both spontaneous capture and intentional project work.

## 7. Add a "Today" or "Focus" view later, not now
This is valuable, but not the next step.

Do it after the golden path is obvious.
Otherwise you risk adding another page before the core story is settled.

## What makes the product useful already

Even now, Taskdeck can already be useful for a solo developer if you frame it correctly.

It is already good for:

- collecting follow-ups while coding
- converting messy notes into structured cards
- reviewing suggested mutations before applying them
- running board-based workflows with comments/mentions
- preparing reproducible demo/test worlds

It is not yet equally good for:

- polished team onboarding
- novice-first self-serve use
- broad autonomous agent project management
- rich analytics/prioritization workflows

## Product position I would keep

Keep the message tight:

Taskdeck is a **safe execution workspace**.
It is not trying to automate everything.
It is trying to make capture cheap and automation trustworthy.

That is much stronger than trying to be "a generic AI task manager".


---

# Dogfooding and "useful now" plan

## Can you use this product already?

Yes.

But you should use it in the shape that matches its strengths, not in the shape of a future vision.

The right current use pattern is:

- one or a few active boards
- lots of low-friction capture
- daily Inbox triage
- proposal review as a safety boundary
- board execution as the place where work actually gets done

If you try to use it as a broad autonomous assistant that manages your entire work life, you will feel the seams immediately.
If you use it as a structured capture-and-execution loop for real development work, it is already viable.

## The best current dogfooding persona

The strongest current persona is:

**solo developer / builder / operator who wants safer AI-assisted work intake**

That person will actually benefit from:

- quick capture of ideas and TODOs
- converting rough notes into cards
- using starter packs for context setup
- maintaining explicit review before mutation
- verifying system behavior through traces/audit when something odd happens

## The practical dogfooding setup I recommend

## Workspace shape
Use 3 boards only at first:

1. active product board
2. backburner / ideas board
3. demo / experiments board

Do not create many boards early.
That will make the product feel more scattered than it is.

## Column conventions
Keep a stable structure:

- Backlog
- Ready
- In Progress
- Review
- Done

Avoid inventing too many custom columns while dogfooding the MVP.
You want to test the core loop, not taxonomy complexity.

## Label conventions
Start with a tiny set:

- priority-high
- bug
- tech-debt
- blocked
- demo

Too many labels will make early use feel heavier than the product thesis allows.

## Your recommended daily rhythm

### Morning
- open Inbox
- triage new captures
- review pending proposals
- execute only the ones that clearly help
- choose 1 to 3 cards for the day

### During work
- use quick capture aggressively
- do not sort while capturing
- use comments on cards for context you would otherwise lose
- mention yourself or collaborators when needed

### End of day
- move board state forward
- capture loose ends into Inbox
- do not leave context only in your head

## What success looks like in dogfooding

The right success criteria are behavioral, not feature-count based.

### Healthy signs
- you capture more often because it is cheap
- you do not resent board maintenance
- Inbox gets triaged regularly
- proposal execution feels safe, not mysterious
- the board becomes the place where work becomes visible

### Unhealthy signs
- you avoid Inbox because triage feels annoying
- you stop reviewing proposals because the value is unclear
- you use Queue instead of Inbox for normal work
- you need IDs or internal knowledge too often
- you keep context in text files because Taskdeck feels slower

## What I would personally treat as "useful now"

## 1. Developer project execution
This is the best current use case.

Examples:
- capture bugs found during coding
- capture refactors while deep in implementation
- convert rough notes into backlog items
- let proposals create/move/update cards
- use comments to preserve reasoning

## 2. Support / issue triage
Also strong.

The support-triage scenario is a good indicator of product fit because it forces:

- messy intake
- triage judgment
- clear provenance
- explicit execution

That matches the product thesis very well.

## 3. Content / writing pipeline
Good enough already.

Content workflows are useful because they visibly benefit from:
- columns
- labels
- due dates
- board state
- suggestions/proposals

## What I would avoid relying on yet

## 1. Queue as the main user-facing surface
Queue is still too implementation-shaped.

Use it for:
- power-user flows
- debugging
- test/dev demo coverage

Do not make it the normal path for typical users.

## 2. Ops as a main product surface
Ops is valuable, but it is still an operator/developer surface.

It should support trust and diagnosis, not define the product for most users.

## 3. Too much autonomy
The current system is much stronger when it suggests than when it pretends to know.

Stay aligned with the product’s strongest value:
**safe, review-first transformation.**

## Metrics worth tracking right now

If you want to know whether Taskdeck is becoming genuinely useful, track these.

### Flow metrics
- capture save time
- time from capture to proposal created
- time from proposal created to reviewed
- proposal approve rate
- proposal reject rate
- execution success rate

### Behavior metrics
- captures per day
- Inbox items triaged per day
- number of days Inbox ends near zero
- number of boards actively touched per week
- comments/mentions per week

### Friction metrics
- number of times you needed a raw ID
- number of dead-end empty states encountered
- number of actions where you asked "where am I supposed to go now?"
- number of failed automations with unclear error recovery

## Highest-leverage improvements for actual daily use

## P0
- add a start/home screen
- add board-scoped capture
- add board-scoped automation shortcuts
- replace Queue board GUID entry with board picker
- improve proposal readability and open-target links

## P1
- add lightweight board health summary (due soon, blocked, pending proposals)
- add Inbox / Proposals badges in nav
- make notifications actionable links, not just messages
- improve board card distribution in seeded scenarios for better visual state

## P2
- add a Today/Focus view
- add cross-board search
- add saved views/filters
- add personal defaults for starter packs and label conventions

## Bottom line

You can use Taskdeck now if you keep it narrow and disciplined.

Use it as:
- capture
- triage
- proposal review
- board execution

Do not ask it yet to be:
- your entire PM suite
- your autonomous project manager
- your polished novice-first productivity product

That restraint will make dogfooding much more honest and much more useful.


---

# Scenario matrix and testing plan

## What you have now

The shipped scenarios cover three important narrative classes:

- engineering sprint
- support triage
- content calendar

That is a good base because together they cover:

- normal board execution
- messy intake and triage
- non-engineering workflow structure

The autopilot and director then make those scenarios dynamic and inspectable.

## What the next scenario layer should do

The current scenario set is good for proving the new harness.
The next scenario set should be optimized for three separate goals:

1. **product persuasion**
2. **regression protection**
3. **behavior stress**

Those are not the same thing.

## Recommended scenario matrix

## A. Core persuasion scenarios
These are the ones you show humans.

### 1. Safe AI work intake
Narrative:
messy note -> Inbox -> triage -> proposal -> apply -> board card

Surfaces:
Inbox, Proposals, Boards, Notifications, Activity

Use:
main stakeholder story

Determinism:
support both deterministic and live-LLM modes

### 2. Engineering sprint
Narrative:
work in multiple states, blocked item, comments, queue-created card, operational trace

Surfaces:
Boards, comments, proposals, ops, activity

Use:
best "this looks like real product work" board

Determinism:
high

### 3. Support triage
Narrative:
raw issue intake, ignored item, applied item, pending item

Surfaces:
Inbox, proposals, notifications

Use:
best story for provenance and safe mutation

Determinism:
medium without live LLM, stronger with live LLM

### 4. Content pipeline
Narrative:
planning, drafting, review, scheduled publication

Surfaces:
Boards, starter packs, labels, due dates

Use:
shows the product is not only for engineering

Determinism:
high

## B. Productization scenarios
These are for validating real use patterns.

### 5. Solo developer week
A board for one developer using the product for 5 to 10 realistic tasks:
bugs, refactors, feature ideas, notes, blockers.

Why useful:
tests actual day-to-day viability.

### 6. Learning / study roadmap
Narrative:
capture concepts to learn, triage into tasks, schedule, review progress.

Why useful:
proves the system can support personal structured work, not just team/project boards.

### 7. Release train
Narrative:
cards across backlog, implementation, QA, release notes, shipped.

Why useful:
strong board visualization and high demo clarity.

## C. Trust and recovery scenarios
These test the promise that automation is safe.

### 8. Bad instruction / recoverable failure
A scenario intentionally submits malformed queue instructions and shows:

- visible failure
- actionable error
- unaffected board state
- audit/log visibility

Why useful:
very persuasive for trust.

### 9. High-risk proposal requiring explicit rejection reason
Force the review system to show stronger guardrails.

Why useful:
proves review-first is a product principle, not a slogan.

### 10. Conflict / concurrent edit scenario
Use multi-session behavior to show presence/conflict hints.

Why useful:
shows serious product engineering and team safety.

## D. Long-run / stress scenarios
These are more for test value than demo value.

### 11. Capture flood
Create many capture items quickly.

Validate:
- list performance
- triage throughput
- status visibility
- Inbox usability under load

### 12. Autopilot soak
Run autopilot for hundreds of turns.

Validate:
- state coherence
- queue/proposal durability
- artifact size
- error accumulation behavior

### 13. Multi-agent simulation
One agent captures.
One agent triages.
One agent works a board.
One agent comments/mentions.

Validate:
- if the system still feels interpretable under many events

## Test strategy I would adopt

## Tier 1: required deterministic smoke
Purpose:
prove the main narrative still works.

Command:
- `demo:director:smoke`

Properties:
- no live LLM required
- fixed scenario
- fixed RNG seed
- fixed artifact directory
- zero or minimal autopilot turns

## Tier 2: opt-in richer deterministic nightly
Purpose:
push more state and more surfaces.

Examples:
- engineering sprint with 25 autopilot turns
- content calendar with queue + ops
- support triage with skipped LLM steps but heavier Inbox volume

## Tier 3: live-LLM validation lane
Purpose:
prove the highest-value interactive magic still works.

Properties:
- manual or scheduled
- live provider only when explicitly configured
- not required for every PR

## Tier 4: adversarial harness
Purpose:
find silent degradations.

Examples:
- invalid scenario references
- duplicate board names
- missing columns
- unsupported labels
- queue instruction failures
- long capture payloads
- stale server reuse conditions

## Snapshot strategy

You already have `snapshot.json` and `trace.ndjson`.
That is excellent.

The next step is to use them more deliberately.

## Snapshot assertions to add
- board count expectations
- per-board card count expectations
- proposal status distribution
- notification count floor
- activity count floor
- capture item status distribution

## Trace assertions to add
- at least one capture-created event
- at least one proposal execution event
- no unexpected turn.error events above threshold
- scenario steps all resolved or skipped intentionally

## Director/report improvements I would add later

### 1. HTML report
Turn the artifact folder into a shareable report:
- screenshots inline
- counters
- selected trace events
- links to video/trace

### 2. Demo modes
Instead of one director, support named demo modes:

- `narrative-safe-ai`
- `narrative-operator`
- `narrative-collaboration`
- `stress-autopilot`
- `soak-capture`

### 3. Snapshot diff mode
Compare current run snapshot to a reference snapshot and highlight drift.

This would turn the director into a stronger regression instrument.

## Issue seeds I would open next

## P0
- Demo Tools in-app launcher
- board picker for Queue composer
- board-scoped capture from BoardView
- proposal cards: affected entities + open links
- "Open Board" / "Open Card" post-execution CTA

## P1
- HTML demo report renderer
- narrative-specific director presets
- long-run autopilot soak command
- snapshot assertion helper
- seeded collaboration scenario with comments/mentions/conflicts

## P2
- multi-agent simulation runtime
- persona-driven scenario packs
- replay mode from trace to UI overlays
- scenario composer/editor UI for internal use

## Final assessment

Your scenario/testing/demo layer is already meaningfully ahead of the product UI in maturity.

That is not a problem.
It is actually a good platform advantage.

Now use that advantage to drive the next phase:
product clarity, not just test/demo completeness.


---

# Manual and docs strategy

## The good news

You already have much more documentation than most projects at this stage.

The docs are not the weak point anymore in the sense of absence.
The risk is now the opposite:

**there is enough documentation that a newcomer may not know what to read first.**

That means the next docs improvement should be about layering and role clarity, not volume alone.

## Current docs shape

The current structure is strong for maintainers:

- `STATUS.md`
- `IMPLEMENTATION_MASTERPLAN.md`
- `TESTING_GUIDE.md`
- `MANUAL_TEST_CHECKLIST.md`
- `DEMO_PLAYBOOK.md`
- `SCENARIOS.md`
- `DOGFOODING_GUIDE.md`
- `USER_MANUAL.md`

This is excellent for engineering and repo governance.

It is less optimized for:

- a new evaluator
- a new contributor trying to understand the product in 10 minutes
- a future user who wants "just tell me how to use this"

## What manual set I would standardize

Think in four layers.

## Layer 1: README = pitch + 2-minute setup
Audience:
everyone

Purpose:
- what Taskdeck is
- why it exists
- how to start it
- what the main loop is
- where to go next

It should not try to be a handbook.

## Layer 2: START HERE = first 15 minutes
Audience:
new users, evaluators, stakeholders, new contributors

This is the most missing document right now.

It should answer:

1. what am I looking at
2. what should I click first
3. what is the golden path
4. how do I load a demo workspace
5. what pages are advanced

Suggested outline:

### Start Here: Taskdeck in 15 minutes
- product thesis in 5 lines
- one command to start backend/frontend
- one command to seed demo workspace
- one demo flow:
  - open Boards
  - open Inbox
  - triage capture
  - open proposal
  - execute
  - open board
- page map:
  - core surfaces
  - advanced surfaces
- "If you only remember one thing" section

## Layer 3: USER MANUAL = reference manual
Audience:
people already using the product

The current `USER_MANUAL.md` is a good base, but I would restructure it more explicitly as a manual.

Suggested sections:

1. concepts
2. first run
3. boards
4. quick capture
5. inbox and triage
6. proposals
7. chat
8. queue
9. notifications
10. activity
11. ops
12. settings / access / archive
13. troubleshooting
14. keyboard shortcuts
15. demo tooling appendix

The most important improvement:
make the distinction between **normal-user flows** and **advanced/operator flows** explicit.

## Layer 4: DEMO + DOGFOODING + TESTING docs
Audience:
internal operators / maintainers

These already exist and are useful.
I would keep them separate from the user manual.

## What I would change in the current USER_MANUAL

## 1. Add a clear role-based framing
At the top, say something like:

- "If you are new, read Start Here first."
- "If you want the quick product story, skip to Golden Path."
- "Queue and Ops are advanced surfaces."

## 2. Add a "Golden Path" section near the top
Not buried.
Near the top.

It should show:

- Quick Capture
- Inbox
- Proposal
- Board

with one paragraph each.

## 3. Add "When should I use X?" sections
This would massively reduce cognitive load.

Examples:

### When should I use Inbox?
For messy raw input, notes, tasks, and things you do not want to structure yet.

### When should I use Queue?
When you know the instruction pattern and want a power-user/dev flow.

### When should I use Chat?
When you want a conversational interface and optionally proposal generation.

### When should I use Ops?
For diagnostics and admin/operator workflows.

## 4. Add troubleshooting tables
This is especially valuable because some surfaces are event-driven.

Examples:

- "Notifications page empty" -> probably no mentions/proposal outcomes yet
- "Queue failed" -> unsupported instruction or missing board scope
- "Nothing in Activity" -> no qualifying audit events yet
- "Triage failed" -> provider config or board/proposal generation issue

## 5. Add screenshots/gifs deliberately
You do not need many.
You need a few very intentional ones:

- start page / boards
- quick capture modal
- inbox with triage states
- proposal card
- board after execution

## A comprehensive manual outline I would actually use

## Part I — What Taskdeck is
- thesis
- vocabulary
- review-first safety model

## Part II — Getting value quickly
- quickstart
- first run
- load demo workspace
- first useful loop in 5 minutes

## Part III — Daily use
- boards
- cards
- comments
- labels
- due dates
- blocked state

## Part IV — Intake and automation
- quick capture
- Inbox
- triage
- proposals
- Chat
- Queue

## Part V — Trust, visibility, and operations
- notifications
- activity
- ops
- archive
- access

## Part VI — Advanced workflows
- scenario runner
- autopilot
- director
- API walkthrough
- smoke demo

## Part VII — Troubleshooting
- empty states
- auth
- queue failures
- live LLM setup
- smoke/demo issues

## One strong docs recommendation

Create a single canonical doc called something like:

- `docs/START_HERE.md`, or
- `docs/FIRST_15_MINUTES.md`

That doc should become the bridge between:
- the repo
- the product
- the demo system
- the manual

Right now that bridge is still distributed across README, Demo Playbook, and User Manual.

## Final docs verdict

Your documentation is now strong enough to support serious iteration.

The next step is not "write even more docs everywhere".
It is:

- tighten entry points
- distinguish audience types
- put the golden path in the most visible place
- make the product easier to understand without reading internal material first


---

# Prioritized backlog after the demo-expansion wave

## Priority 0 — make the product teach itself

These are the highest-leverage changes because they convert the current harness strength into product clarity.

## P0-1. Add a real start surface
Form:
- `/workspace/home`, or
- a persistent onboarding panel in `BoardsListView`

Must answer:
- what Taskdeck is
- what to do first
- what the 4-step loop is
- how to load demo data

## P0-2. Make board context travel with the user
Current pain:
the user still has to mentally carry board context across Inbox, Proposals, Queue, and Chat.

Fix:
- board-scoped deep links everywhere
- board-aware buttons in `BoardView`
- proposal cards link back to board/card targets
- post-execution CTA opens the affected board

## P0-3. Replace Queue board GUID input with board picker
The current raw GUID input is still too internal.

Better:
- picker by board name
- current board preselected if opened from a board route
- show the ID only as secondary/debug info

## P0-4. Make proposals legible at a glance
Needed improvements:
- operation summary bullets
- affected entity links
- human-readable provenance
- risk explanation
- state-specific CTA

## P0-5. Add empty-state guidance with next actions
Apply to:
- Notifications
- Activity
- Queue
- Chat
- Access
- Archive

Rule:
no "No X found." without "Why?" and "What next?"

## Priority 1 — close the UX gap between demo and product

## P1-1. Add in-app Demo Tools page
Dev/demo only.
Buttons:
- seed workspace
- run scenario
- start/stop autopilot
- enable walkthrough feature flags
- open latest artifacts folder or artifact index

## P1-2. Add a guided narrative mode
The current demo proves breadth.
Add a specific guided mode for the main story:

- Quick Capture
- Inbox
- Proposal
- Execute
- Board
- Notification / Activity

This can be:
- a walkthrough overlay
- a stepper page
- a "Start Demo Tour" flow

## P1-3. Add nav badges
Show:
- Inbox count
- pending proposals count
- unread notifications count

This makes the system feel live and directs attention to next work.

## P1-4. Make quick capture board-aware
Support both:
- global workspace capture
- capture into current board

## P1-5. Improve seeded board aesthetics
At least one hero board should always look visually healthy:
- cards across multiple columns
- at least one blocked card
- at least one due-soon card
- at least one comment/mention
- at least one applied proposal artifact visible

## Priority 2 — turn the harness into a stronger product asset

## P2-1. HTML demo report
Use current artifact bundle to generate:
- one shareable report
- screenshots inline
- counts
- scenario metadata
- links to video/trace
- selected important events from `trace.ndjson`

## P2-2. Snapshot assertions
Use `snapshot.json` for run-quality checks:
- board count
- card counts
- proposal distribution
- capture distribution
- notification floor
- activity floor

## P2-3. Narrative presets for director
Examples:
- `safe-ai-intake`
- `engineering-flow`
- `support-triage`
- `operator-proof`
- `collaboration-proof`

## P2-4. Long-run soak mode
Run autopilot for 100 to 500 turns and measure:
- error rate
- state drift
- backlog clustering
- trace size
- ops/log stability

## Priority 3 — make the product more useful day to day

## P3-1. Today / Focus view
Not first.
But useful once the golden path is stable.

Should show:
- captures needing triage
- pending proposals
- due today
- blocked cards
- current board quick links

## P3-2. Cross-board search
Search:
- cards
- captures
- comments
- proposals

## P3-3. Saved views
Examples:
- blocked work
- due this week
- review needed
- my mentions

## P3-4. Better import surfaces
Eventually:
- browser clipper
- markdown import
- dev-notes import
- meeting-note capture source

## Priority 4 — agent and autonomy expansion

Only do these after the golden path is stable.

## P4-1. Workspace-scoped proposals
This would let agents propose:
- new boards
- renamed workspaces
- broader setup operations

without forcing everything through a board-scoped path.

## P4-2. Multi-agent simulation runtime
Useful for:
- richer demos
- long-run behavior testing
- collaboration narratives

## P4-3. Replay-from-trace mode
Use trace data to replay or annotate demo flows.

## P4-4. Scenario composer UI
Internal tool only.
Lets you assemble scenario JSON through forms.

## Suggested issue batching

## Batch A — golden path
- start surface
- board context propagation
- queue board picker
- proposal readability
- empty-state guidance

## Batch B — in-app demoability
- Demo Tools page
- nav badges
- guided tour
- board-aware quick capture

## Batch C — harness maturity
- HTML report
- snapshot assertions
- director presets
- long-run soak

## Batch D — productivity expansion
- Today view
- cross-board search
- saved views
- better imports

## Final recommendation

Do not spend the next cycle mainly adding new capability families.

Spend it making the current capability set feel:
- obvious
- connected
- board-aware
- self-explanatory


---
