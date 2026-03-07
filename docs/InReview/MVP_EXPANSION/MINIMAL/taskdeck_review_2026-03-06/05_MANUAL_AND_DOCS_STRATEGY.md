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
