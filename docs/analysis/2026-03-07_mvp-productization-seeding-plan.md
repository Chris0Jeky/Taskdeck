# MVP Productization Seeding Plan

Date: 2026-03-07
Status: Proposed for GitHub write-back
Source basis:

- `docs/analysis/2026-03-07_mvp-expansion-gap-map.md`
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/ISSUE_EXECUTION_GUIDE.md`
- current open GitHub issues, especially `#107`, `#93`, `#96`, `#100`, `#216`, `#77`

## Why This Exists

The repo now has strong capture/demo infrastructure, but it still lacks a coherent issue wave for turning that into a self-explaining product.

This note defines the concrete seeding shape before new GitHub issues are created so the write-back stays:

- specific enough to execute
- small enough to review
- explicit about reuse versus new scope

## Seeding Decision

Seed one dedicated productization wave tracker plus five focused child issues.

Do not create duplicate tickets for:

- `#96` onboarding/contextual help
- `#100` end-user docs/manual/FAQ
- `#93` global search and quick actions
- `#216` thesis-aligned demo/landing baseline
- `#77` metrics dashboard

Instead:

- update `#96` and `#100` to join the new wave directly
- leave `#93`, `#216`, and `#77` as reuse anchors and follow-through references

## Recommended New Issue Set

### 1. Productization wave tracker

Suggested title:
- `MVP-02: Novice-first productization wave tracker (Home -> Review -> Today -> help)`

Priority:
- `Priority II`

Purpose:
- track the full near-horizon legibility wave
- act as the parent reference from `#107`
- keep child ordering explicit

Child checklist should include:

- new shell/home issue
- new review/readability issue
- new today/action-rail issue
- new selector/raw-ID-removal issue
- existing `#96`
- existing `#100`
- new first-run smoke/launch-criteria issue

Reused but not duplicated:

- `#93`
- `#216`
- `#77`

### 2. Product shell, `Home`, and empty/help-state foundation

Suggested title:
- `UX-11: Product shell modes, Home route, and action-oriented empty/help states`

Priority:
- `Priority II`

Why separate:
- this is the information-architecture pivot
- it should land before or alongside onboarding/help, not after

Recommended dependencies:

- `#199`
- `#211`

Scope:

- add workspace presentation-mode foundation with `guided` and `workbench` shipped now
- reserve the `agent` mode slot without forcing agent-runtime work into this tranche
- add `Home` route and backend summary endpoint
- make `Home` the default landing surface for guided mode
- define reusable action-oriented empty/help states across primary user pages
- keep advanced/operator surfaces visible but clearly secondary in guided mode

Acceptance notes:

- `Home` makes the core loop legible without requiring README/manual reading
- guided mode surfaces `capture`, `review`, `today`, and board continuation paths
- workbench mode preserves the current implementation-shaped navigation for power users

### 3. Review-first navigation and proposal readability

Suggested title:
- `UX-12: Review-first route, navigation terminology, and readable proposal summaries`

Priority:
- `Priority II`

Recommended dependencies:

- new `UX-11`

Scope:

- add `/workspace/review` as the normal-user automation route
- shift navigation terminology from implementation language toward review-first language
- make proposal cards/summaries readable in plain language:
  - intent
  - affected board/card scope
  - risk/confidence context
  - obvious next action
- keep queue/advanced controls available but explicitly secondary

Acceptance notes:

- review becomes the default place to evaluate automation work
- users no longer need internal mental models of queue mechanics to understand pending work

### 4. `Today`, board action rail, and cross-surface continuity

Suggested title:
- `UX-13: Today agenda, board action rail, and cross-surface deep-link continuity`

Priority:
- `Priority II`

Recommended dependencies:

- new `UX-11`
- new `UX-12`

Scope:

- add `Today` route and supporting agenda endpoint
- surface pending review, inbox follow-up, and board continuation cues in one place
- add board action rail to make next moves obvious from a board
- strengthen deep links and next-step shortcuts across:
  - inbox
  - review
  - notifications
  - boards
  - home

Acceptance notes:

- user can move from `Home` to `Today` to `Review` to a board without context loss
- board surfaces expose the obvious follow-up actions instead of forcing route hunting

### 5. Selector-first targeting and raw-ID removal

Suggested title:
- `UX-14: Selector-first board targeting and raw-ID removal for common flows`

Priority:
- `Priority II`

Recommended dependencies:

- `#38`

Scope:

- remove raw board-ID happy paths from common user flows
- add selector/picker targeting in the places that still feel operator-first
- keep raw IDs available only as diagnostic/advanced affordances
- shape the selector contracts so later global search in `#93` can build on them

Non-goal:

- this is not the full global search/launcher issue from `#93`

Acceptance notes:

- normal flows no longer require users to know opaque board IDs
- selectors are reusable, keyboard-safe, and consistent with existing input-assist work

### 6. First-run smoke and launch-criteria slice

Suggested title:
- `TST-20: First-run golden-path smoke and novice-beta launch criteria`

Priority:
- `Priority II`

Recommended dependencies:

- new `UX-11`
- new `UX-12`
- new `UX-13`
- `#96`

Scope:

- add a deterministic Playwright smoke for:
  - `Home`
  - capture
  - review
  - board continuation
- define novice-beta launch criteria and telemetry naming for the productization wave
- align the smoke and criteria with existing capture/trust metrics instead of waiting for the full dashboard in `#77`

Reuse rule:

- coordinate with `#77`
- do not block on full metrics-dashboard implementation

## Existing Issues To Update Instead Of Recreate

### `#96`

Update direction:

- keep the issue number
- move it into the new productization wave
- raise it to `Priority II`
- refocus body on first-run checklist, replayable contextual help, and route-level guidance for `Home` / `Review` / `Today`

Recommended new dependencies:

- `#50`
- new `UX-11`

### `#100`

Update direction:

- keep the issue number
- move it into the new productization wave
- raise it to `Priority II`
- treat the already-landed doc reshaping as baseline, not closure
- focus the remaining work on aligning docs/help/manual content to the product shell once `Home` / `Review` / `Today` exist

Recommended new dependencies:

- `#96`
- new `UX-11`
- new `UX-12`
- new `UX-13`

### `#107`

Update direction:

- append a new wave section for the productization tracker and child issues
- place it ahead of more disconnected UX/premium/future-breadth work in the execution story
- reference reuse anchors instead of duplicating them into the wave body

## Reuse Rules

### Reuse directly

- `#96` for onboarding/help
- `#100` for docs/manual/help-center shape

### Reference, but do not block the first wave on them

- `#93` for later global search and quick actions
- `#216` for public story/landing/demo follow-through
- `#77` for dashboard-scale metrics work

### Keep out of the near-horizon wave

- `#97`
- `#98`
- `#218`
- `#219`

These remain later expansion surfaces unless a concrete productization slice proves they are needed sooner.

## Why This Shape Is Better Than The Alternatives

### Better than only updating `#107`

- `#107` alone is too abstract to execute
- it does not give the next implementation pass testable acceptance criteria

### Better than a CAP-style explosion of many tiny tickets

- the productization work still has some discovery risk
- too many small tickets would create early churn and duplicate dependency bookkeeping

### Better than folding everything into `#96` and `#100`

- the missing work is not mostly documentation
- the product shell, route model, and review/today surfaces need dedicated implementation issues

## Recommended GitHub Write Sequence

1. Create the productization wave tracker.
2. Create the four new UX/TST child issues.
3. Update `#96` and `#100` to join the wave and raise them to `Priority II`.
4. Update `#107` with the new wave section and child checklist.
5. Read back the created/updated issues and verify dependency text, labels, and priority consistency.
