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
