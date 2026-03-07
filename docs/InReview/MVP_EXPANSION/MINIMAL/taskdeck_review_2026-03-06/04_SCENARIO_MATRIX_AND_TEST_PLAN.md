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
