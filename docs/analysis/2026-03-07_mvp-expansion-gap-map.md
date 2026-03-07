# MVP Expansion Gap Map

Date: 2026-03-07
Scope: Reconcile the 2026-03-06 MVP expansion review packages against the current GitHub backlog and identify what should be reused, what is missing, and how the next seeding wave should be ordered.

## Inputs Reviewed

Current planning/docs inputs:

- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/ISSUE_EXECUTION_GUIDE.md`
- `docs/TaskdeckNextWorkChecklist.md`
- `docs/InReview/MVP_EXPANSION/MINIMAL/*`
- `docs/InReview/MVP_EXPANSION/EXPANDED/*`

GitHub backlog inputs:

- full open-issue list for `Chris0Jeky/Taskdeck`
- targeted searches across onboarding/help, proposal/review, selectors/raw-ID removal, agents/runs/knowledge/integrations, and daily-use surfaces

## Bottom Line

The current backlog is still weighted toward making the engine stronger.
The MVP expansion package is mostly about making the product legible.

That product-legibility wave is not yet represented as one coherent execution wave on GitHub.

## What Already Overlaps

### Reuse candidates with real overlap

- `#96` UX-10 onboarding/contextual help
  - reuse for onboarding checklist, guided help blocks, or replayable help-state work
- `#93` UX-07 global search and quick actions
  - reuse for broader command-palette search/navigation work once `Today`/`Review` land
- `#100` DOC-04 user guides/tutorials/FAQ
  - reuse for navigation-shaped manual and help-center restructuring
- `#216` GTM-01 thesis-aligned demo/landing baseline
  - reuse where public-facing pitch/demo framing intersects the new novice-first story
- `#77` ANL-01 metrics dashboard
  - reuse for telemetry naming and launch-criteria alignment rather than creating a second isolated metrics story
- `#75` INT-01 import adapters foundation
  - reuse for note/transcript import pathways and future inbound capture contracts
- `#97` INT-03 plugin/extension RFC
  - reuse only for later connector/platform design; do not let it outrun the near-horizon productization work
- `#98` INT-04 connector framework
  - reuse for later integrations registry/connector implementation
- `#218` CAP-20 transcript capture source
  - reuse for transcript-style intake
- `#219` CAP-21 voice capture/transcription
  - reuse for broader external capture-source work when promoted

### Partial overlap that is not enough by itself

- capture wave `#199` to `#211`
  - already delivered the core loop, but not the novice-first shell around it
- premium UI wave `#242` to `#250`
  - improves quality of surfaces, but does not define `Home`, `Today`, or product-level navigation/teaching by itself
- demo-expansion wave `#297` to `#302`
  - solved demoability and seeded state well, but not self-serve product understanding

## What Is Still Missing

### Batch A: novice-first shell and entry clarity

Missing as a coherent wave:

- workspace mode preference (`guided`, `workbench`, `agent`)
- `Home` route and summary endpoint
- `Review` as the clear primary normal-user automation surface
- action-oriented empty/help states across primary pages
- board selectors/pickers instead of raw-ID happy paths

Current issue coverage:

- no dedicated issue wave exists
- `#96` helps for onboarding/help
- `#93` helps for search/selectability

### Batch B: board-centered daily workflow

Missing as a coherent wave:

- `Today` route and agenda endpoint
- first-run onboarding checklist and project wizard
- proposal summary service and readable proposal cards
- board action rail (`Capture here`, `Ask assistant`, `Review proposals`, `Add card`)
- stronger deep links and next-step shortcuts across inbox/review/notifications/board flow

Current issue coverage:

- partial overlap in delivered capture/proposal linking
- no dedicated issue wave exists

### Batch C: docs/help/testing coherence

Missing or incomplete:

- dedicated first-run smoke for the real product story
- in-app help-center/help-block direction
- docs/manual organization around current and intended top-level navigation
- explicit novice-beta and agent-alpha launch criteria

Current issue coverage:

- `#100` is the best reuse point for manual/docs reshaping
- `#96` is the best reuse point for contextual help
- `#77` can anchor telemetry/launch-criteria alignment

### Batch D: agent substrate

Effectively unseeded:

- `AgentProfile`
- `AgentRun`
- `AgentRunEvent`
- tool registry
- policy evaluator
- first bounded agent template
- agent views

Current issue coverage:

- no meaningful current issue coverage

### Batch E: knowledge and integrations surface

Mostly unseeded:

- knowledge documents/notes model
- SQLite FTS-backed knowledge search
- notes/transcript/clip intake tied to knowledge/capture
- integrations registry/management page

Current issue coverage:

- `#75`, `#98`, `#218`, `#219` are the main reuse anchors
- no product-facing knowledge/integrations wave exists yet

## Recommended Seeding Order

### Priority II

Seed and execute first:

1. Batch A novice-first shell and entry clarity
2. Batch B board-centered daily workflow
3. Batch C docs/help/testing coherence

Reason:

- this is the shortest path from "good demo infrastructure" to "self-explaining product"

### Priority III

Seed only after the above is underway:

4. Batch D agent substrate
5. Batch E knowledge and integrations surface

Reason:

- the blueprint is explicit that agents, traces, and knowledge should formalize only after the human product is clear

## Release Framing

Recommended release framing from the expansion package:

- `R1` novice-first beta
  - `Home`
  - `Today`
  - `Review`
  - onboarding
  - readable proposals
  - board-centered actions
  - no raw-ID requirement in common flows
- `R2` agent foundation alpha
  - profiles
  - runs
  - run events
  - first bounded template
  - policies
- `R3` knowledge/integrations alpha
  - searchable notes/docs
  - integrations page
  - at least two meaningful inbound context/capture paths

## Operational Seeding Rules

- Add the new productization wave to `#107` before active execution begins.
- Reuse the overlap issues above instead of cloning scope into disconnected new tickets.
- Keep `MINIMAL` as the near-horizon filter when `EXPANDED` suggests broader future breadth.
- Do not let plugin/connector/agent breadth outrun `Home` / `Today` / `Review` productization.
