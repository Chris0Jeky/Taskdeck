# Dogfooding Guide

This guide is for using Taskdeck daily while it remains capture-first, board-centered, and review-first.

Primary goal:

- reduce maintenance overhead by capturing quickly
- turn capture into reviewed proposals, not silent automation
- find product-legibility friction before adding more surface breadth

## Best Current Dogfooding Shape

Taskdeck is strongest right now for:

- solo developers
- builders/operators
- anyone who wants safer AI-assisted work intake without silent board mutation

Use it as:

- capture
- triage
- review
- board execution

Do not expect it yet to be:

- a polished novice-first app shell
- a broad autonomous project manager
- a replacement for every planning/reporting surface

## Recommended Workspace Setup

Keep the workspace intentionally small at first.

Recommended boards:

1. active product board
2. backburner or ideas board
3. demo or experiments board

Recommended columns:

- `Backlog`
- `Ready`
- `In Progress`
- `Review`
- `Done`

Recommended starter labels:

- `priority-high`
- `bug`
- `tech-debt`
- `blocked`
- `demo`

Reason:

- the goal is to test the core loop, not taxonomy complexity

## Current MVP Loop

1. Capture into Inbox
- use Inbox for notes, bugs, follow-ups, and loose ends
- do not pre-sort during capture; save it first

2. Triage into proposals
- open Inbox daily or twice daily
- ignore noise and triage the rest
- let triage create proposals asynchronously

3. Review proposals
- open `Automations -> Proposals`
- validate operations before approval
- execute only after review

4. Work the board
- keep the board as the visible work surface
- move cards honestly
- use comments to preserve reasoning

## Daily Rhythm

Morning (5 to 10 min):

- triage Inbox
- review pending proposals
- pick 1 to 3 cards for active work

During work:

- capture follow-ups immediately
- do not reorganize while capturing
- use comments or mentions when context would otherwise get lost

End of day (3 to 5 min):

- move completed work forward
- capture unfinished work into Inbox
- avoid ending the day with critical context only in your head

Weekly (15 to 30 min):

- archive or move stale work
- check which board is truly active
- review friction notes from the week

## Automation Guardrails

Use automation for:

- repetitive edits
- templated card creation
- low-risk board hygiene

Avoid automation for:

- high-context decisions without human review
- broad changes you cannot quickly inspect

Queue guidance:

- treat Queue as advanced
- keep `requestType` as `instruction`
- use board context when available
- prefer one clear instruction per request

Chat guidance:

- prefer board-scoped sessions
- use it when you want conversational planning or board bootstrap
- keep proposal review as the actual mutation gate

## What To Log

Keep a lightweight friction log for each meaningful issue:

- objective
- expected behavior
- actual behavior
- minimal repro
- impacted workflow

High-value friction themes:

- poor discoverability
- empty states with no next-step guidance
- flows that still feel too internal or system-shaped
- steps where you asked "what should I click next?"
- automation failures with unreadable recovery guidance

## Healthy Signs

- you capture more often because it is cheap
- Inbox reaches zero or near-zero regularly
- proposals feel safe rather than mysterious
- the board becomes the place where work gets finished
- you need fewer external notes to remember follow-ups

## Unhealthy Signs

- you avoid Inbox because triage feels annoying
- you bypass review because the value is unclear
- you use Queue for ordinary work
- you still need internal IDs or hidden knowledge too often
- you keep context in text files because Taskdeck feels slower than capture elsewhere

## Practical Success Criteria

Track these sanity checks:

- Inbox reaches zero or near-zero most days
- proposals execute without manual DB intervention
- a new user could reach first useful card in under 2 minutes with guidance
- failed automations return readable, actionable errors
- the board remains the system of visible work, not just a side effect of automation
