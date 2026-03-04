# Dogfooding Guide

This guide is for using Taskdeck daily while the product remains review-first and capture-centric.

Primary goal:
- reduce maintenance overhead by capturing quickly
- turn capture into reviewed proposals, not silent automation
- log friction with reproducible evidence

## MVP Loop

1. Capture into Inbox
- Use Inbox for notes, bugs, follow-ups, and "do this later" tasks.
- Do not pre-sort during capture. Capture first, triage later.

2. Triage into proposals
- Open Inbox daily (or twice daily for high intake).
- For each capture item: ignore or start triage.
- Let triage generate a proposal asynchronously.

3. Review proposals
- Open `Automations -> Proposals`.
- Validate operations before approval.
- Execute only after review.

4. Work the board
- Keep one active board per context.
- Use due dates and blocked reasons consistently.

## Daily Routine

Morning (5-10 min)
- Triage Inbox.
- Review due/blocked cards.
- Unblock one important item.

Midday (2-5 min)
- Move active cards into `In Progress`.
- Update next step in card description when context-switching.

End of day (3-5 min)
- Move completed work to `Done`.
- Capture unfinished work as next-day Inbox items.

Weekly (15-30 min)
- Archive stale items or move to a backlog board.
- Refresh board naming/scope for next sprint.

## Automation Guardrails

Use automation for:
- repetitive edits
- templated card creation
- low-risk board hygiene

Avoid automation for:
- high-context decisions without manual review

Queue guidance:
- keep `requestType` as `instruction`
- provide `Board ID` for board-scoped patterns (`create card`, `rename board`, `move column`, `move card`, etc.)
- prefer one clear instruction per request

Chat guidance:
- board-scope sessions where possible
- request proposals only when you want board mutations

## What To Log

Keep a lightweight friction log for each issue:
- objective
- expected behavior
- actual behavior
- minimal repro
- impacted workflow

High-value friction themes:
- poor discoverability
- empty states with no next-step guidance
- silent failures
- flows that require internal IDs without guidance

## Practical Success Criteria

Track these sanity checks:
- Inbox reaches zero (or near-zero) daily
- proposals execute without manual DB intervention
- a new user reaches first useful card in under 2 minutes
- failed automations return readable, actionable errors

