# MVP Productization Seeding Plan

Date: 2026-03-07
Status: Executed on GitHub
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

## Executed Outcome

Canonical seeded wave:

- `#318` tracker
- `#320` workspace modes + `Home` summary shell
- `#322` `Review`-first routing, empty/help states, and selector/raw-ID cleanup
- `#324` `Today` agenda + onboarding path
- `#326` proposal readability + board-centered action flow
- `#96` onboarding/contextual help (reused and reprioritized to `Priority II`)
- `#100` docs/manual/FAQ/help-center follow-through (reused and reprioritized to `Priority II`)
- `#328` product first-run smoke + launch-criteria guardrail

Wave index update:

- `#107` now includes the seeded productization wave as `Wave P`
- source-pack naming note: the `MINIMAL` pack called this immediate tranche `Wave I`; canonical GitHub indexing uses `Wave P`

Duplicate cleanup:

- `#317`, `#319`, `#321`, `#323`, `#325`, and `#327` were closed as duplicates after a parallel-creation conflict was detected during the project-hygiene pass

Project metadata outcome:

- `#318`, `#320`, `#322`, `#324`, `#326`, `#328`, `#96`, and `#100` were verified in GitHub Project v2 with `Status=Pending`
- their `Priority` field was synced to `Priority II`

## Seeding Decision

Seed one dedicated productization wave tracker plus five focused child issues.
That exact structure was kept, but the final canonical issue split used the `#318` / `#320` / `#322` / `#324` / `#326` / `#328` set above.

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

Canonical issue:
- `#318` `UX-13: MVP productization wave tracker (Home -> Review -> Today)`

Priority:
- `Priority II`

Purpose:
- track the full near-horizon legibility wave
- act as the parent reference from `#107`
- keep child ordering explicit

Child checklist now includes:

- `#320`
- `#322`
- `#324`
- `#326`
- `#96`
- `#100`
- `#328`

Reused but not duplicated:

- `#93`
- `#216`
- `#77`

### 2. Product shell, `Home`, and empty/help-state foundation

Canonical issue:
- `#320` `UX-14: Add workspace mode foundation and Home summary shell`

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

### 3. Review-first navigation, empty/help states, and selector cleanup

Canonical issue:
- `#322` `UX-15: Make Review the primary automation surface and remove raw-ID happy paths`

Priority:
- `Priority II`

Recommended dependencies:

- `#318`
- `#320`

Scope:

- add `/workspace/review` as the normal-user automation route
- shift navigation terminology from implementation language toward review-first language
- replace dead-end empty states on primary product pages with action-oriented guidance
- remove raw board-ID happy paths from common flows via selectors/pickers
- keep queue/advanced controls available but explicitly secondary

Acceptance notes:

- review becomes the default place to evaluate automation work
- users no longer need internal mental models of queue mechanics to understand pending work

### 4. `Today` agenda and onboarding path

Canonical issue:
- `#324` `UX-16: Add Today agenda and first-run onboarding path`

Priority:
- `Priority II`

Recommended dependencies:

- `#318`
- `#320`
- `#322`

Scope:

- add `Today` route and supporting agenda aggregation
- add the first-run onboarding/checklist or wizard path for the current MVP loop
- connect onboarding progression back to `Home`, `Review`, and board execution
- keep onboarding resumable/replayable/dismissible for experienced users

Acceptance notes:

- user can move from `Home` to `Today` to `Review` to a board without context loss
- onboarding is tied to the same `Home` / `Today` / `Review` story rather than a detached tour

### 5. Proposal readability and board-centered action flow

Canonical issue:
- `#326` `UX-17: Improve proposal readability and board-centered action flow`

Priority:
- `Priority II`

Recommended dependencies:

- `#318`
- `#320`
- `#322`

Scope:

- improve proposal readability on the main review surfaces
- add clearer proposal cards with plain-language summaries and next-step links
- add board-centered action affordances and better cross-surface travel
- keep board execution as the visible center of the normal user path

Related reuse:

- `#93` remains the later broader search/launcher issue

Acceptance notes:

- proposal cards keep board continuation obvious instead of hiding next actions behind route-hunting
- board-centered follow-through stays explicit even as review surfaces become easier to scan

### 6. First-run smoke and launch-criteria slice

Canonical issue:
- `#328` `TST-20: Product first-run smoke and launch-criteria guardrail`

Priority:
- `Priority II`

Recommended dependencies:

- `#318`
- `#320`
- `#322`
- `#324`
- `#326`
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

- `#318`
- `#320`
- `#324`

### `#100`

Update direction:

- keep the issue number
- move it into the new productization wave
- raise it to `Priority II`
- treat the already-landed doc reshaping as baseline, not closure
- focus the remaining work on aligning docs/help/manual content to the product shell once `Home` / `Review` / `Today` exist

Recommended new dependencies:

- `#318`
- `#96`
- `#199`

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

## Executed GitHub Write Sequence

1. Created the productization wave tracker.
2. Created the new Wave P child issues.
3. Updated `#96` and `#100` to join the wave and raised them to `Priority II`.
4. Updated `#107` with the new wave section and child checklist.
5. Read back the created/updated issues and verified dependency text, labels, and priority consistency.
6. Closed the parallel duplicate batch when the conflict was detected.
