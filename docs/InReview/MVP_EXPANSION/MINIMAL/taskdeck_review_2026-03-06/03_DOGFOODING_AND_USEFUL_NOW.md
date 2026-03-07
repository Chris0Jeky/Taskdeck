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
