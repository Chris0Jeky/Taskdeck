# MVP Expansion Gap Map

Date: 2026-03-07
Source packs:
- `docs/InReview/MVP_EXPANSION/MINIMAL/taskdeck_review_2026-03-06/`
- `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/`

## Executive Summary

The new MVP expansion material does not call for a domain rewrite.
It calls for a product-legibility correction.

Current Taskdeck strengths are already real:
- capture/inbox and proposal-first mutation exist
- board execution is stable
- demo/scenario/director tooling is unusually strong
- trust surfaces already exist through audit, notifications, activity, and logs

Current Taskdeck weakness is presentation and entry clarity:
- the current shell still feels workbench-first rather than novice-first
- the app still spreads the main story across boards, inbox, proposals, queue, and chat
- documentation is strong for maintainers but weaker for first-time evaluators and future end users
- the backlog only partially reflects the sharper productization wave proposed in the new blueprint

## High-Confidence Product Direction

The source packs converge on this sequence:

1. keep one core product, not separate human and agent products
2. make the current product teach itself before adding broader autonomy
3. make board context travel everywhere
4. make review/proposal UX legible enough that the trust model is obvious
5. only then add agent, knowledge, and broader integration layers

## Current Docs Gap Map

### `README.md`

Already covered:
- product thesis
- repo layout
- local setup
- basic core loop

Missing or drifting:
- no direct first-15-minutes entry path for evaluators
- no link to a single `Start Here` bridge doc
- shipped-provider state drift: README still says Gemini support is tracked and not shipped, while active docs record `OpenAI` and `Gemini` as shipped behind explicit config gates

Recommendation:
- keep README short
- link to a new `docs/START_HERE.md`
- keep README thesis-aligned and reality-correct

### `docs/STATUS.md`

Already covered:
- shipped behavior
- current implementation snapshot
- issue-wave history
- known gaps and risks

Missing:
- explicit statement that current UX remains closer to a workbench shell than a novice-first product
- explicit gap record for no dedicated `Home` / `Today` / workspace-mode product shell yet
- explicit documentation of raw-ID friction still present in some advanced flows
- reconciliation note that the new MVP expansion packs are now the active source for post-demo productization planning

Recommendation:
- keep `STATUS.md` factual
- add the new product-legibility gaps only as current constraints/known gaps
- do not describe future `Home`/`Today` surfaces as shipped

### `docs/IMPLEMENTATION_MASTERPLAN.md`

Already covered:
- current hardening tracks
- existing issue-wave ordering
- priority-labeled backlog structure
- capture and demo wave history

Missing:
- no dedicated novice-first productization track after the demo-expansion wave
- no clean separation between immediate productization work and later agent/knowledge expansion
- no issue-overlap note tying new blueprint work back to existing issues such as `#93`, `#96`, `#100`, `#216`, `#97`, and `#98`

Recommendation:
- add a 2026-03-07 planning update for MVP expansion reconciliation
- add phased track language:
  - novice-first shell and first-run productization
  - review/proposal and board-centered daily workflow
  - agent workspace foundation
  - knowledge/integration layer
  - testing/help/manual maturity
- keep existing higher-priority security and control-plane work ahead of the new wave

### `docs/INDEX.md`

Already covered:
- canonical active docs categories
- governance rules
- doc archive policy

Missing:
- no audience-first entry path for new users/evaluators
- no bridge doc between README and the manual/demo/testing stack

Recommendation:
- add `docs/START_HERE.md`
- add quick read paths by audience:
  - new user/evaluator
  - maintainer/planning
  - demo operator

### `docs/USER_MANUAL.md`

Already covered:
- current surface descriptions
- quick current-loop orientation
- advanced flow descriptions

Missing:
- no strong golden-path section near the top
- no "When should I use X?" guidance
- no clean separation between normal flows and advanced/operator flows
- still normalizes `Board ID` entry in Queue as if that were a standard user path

Recommendation:
- restructure around:
  - what Taskdeck is
  - current golden path
  - current normal surfaces
  - advanced surfaces
  - troubleshooting and constraints
- explicitly say Queue/Ops are advanced
- mention planned `Home`/`Today` surfaces as roadmap, not current UI

### `docs/DOGFOODING_GUIDE.md`

Already covered:
- capture -> proposal -> board loop
- daily routine
- friction logging

Missing:
- still instructs users to provide raw `Board ID` in Queue guidance
- does not clearly discourage Queue as the normal path strongly enough
- does not frame dogfooding around the narrow "useful now" persona from the new review pack

Recommendation:
- reframe around the solo developer / builder persona
- explicitly treat Queue as advanced/debug tooling
- add useful-now success signals and unhealthy signals

### `docs/SCENARIOS.md`

Already covered:
- current JSON scenario runner
- current step types
- deterministic CI posture

Missing:
- no explicit scenario matrix for persuasion vs regression vs stress
- no roadmap note for next scenarios such as safe-AI intake, solo developer week, failure/recovery, and collaboration/conflict

Recommendation:
- add a short scenario-strategy section without claiming the new scenario packs exist yet

### `docs/TESTING_GUIDE.md`

Already covered:
- verified totals
- automated/manual commands
- CI policy
- capture-loop thesis validation

Missing:
- no explicit record that the next required smoke should become a novice-first first-run path after productization work ships
- no mapping between current demo-strength testing and weaker current self-serve product testing

Recommendation:
- add a short productization testing note:
  - current smoke remains director/capture centered
  - future required smoke should cover first-run golden path once the supporting UI exists

## GitHub Issue Overlap Map

### Existing issues that should be reused, not duplicated

- `#93` `UX-07`: global search and quick-action launcher
  - maps to blueprint issue `B5`
- `#96` `UX-10`: interactive onboarding tour and contextual help
  - partially maps to blueprint issues `A4` and `E2`
- `#100` `DOC-04`: end-user workflow guides, tutorials, and FAQ baseline
  - partially maps to blueprint issue `E3`
- `#216` `GTM-01`: thesis-aligned demo and landing baseline
  - aligns with the blueprint's stronger product-story emphasis
- `#97` `INT-03`: plugin/extension architecture RFC
  - later than the current blueprint's integration-management view
- `#98` `INT-04`: third-party connector framework
  - later and broader than the blueprint's integrations-management concept

### Existing issues that are adjacent but not sufficient

- `#92` accessibility remediation
  - useful dependency for productization work, but not a substitute for novice-first shell work
- premium UI wave `#242` to `#250`
  - useful for polish, but not a substitute for the `Home`/`Today`/workspace-mode/navigation shift
- capture wave `#199` to `#213`
  - already delivered the substrate the blueprint depends on

### Missing issue groups implied by the new blueprint

These areas do not appear to have clean, explicit issue coverage yet:

- workspace presentation modes (`guided`, `workbench`, `agent`)
- `Home` route and workspace summary endpoint
- `Today` route and aggregated agenda endpoint
- blind-empty-state replacement across core pages
- board picker/search selectors replacing raw board IDs in common flows
- review alias and review-first primary automation navigation
- proposal summary/readability overhaul
- board action rail and board-centered deep-linking
- agent profile/run/run-event foundation
- knowledge documents and SQLite FTS search
- integrations management view
- first-run golden-path Playwright smoke
- telemetry launch criteria for novice beta and agent alpha

## Recommended Execution Batches

### Batch A — Novice-first shell

- workspace mode
- `Home`
- `Today`
- onboarding checklist/project wizard
- action-oriented empty states
- board picker/raw-ID removal

### Batch B — Review and board-centered workflow

- `/workspace/review` alias
- readable proposal summaries/cards
- board action rail
- deep links across inbox/notifications/proposals
- global search reuse of `#93`

### Batch C — Help, docs, and first-run regression

- `Start Here` documentation bridge
- manual restructuring
- page-level help alignment and reuse of `#96` / `#100`
- first-run golden-path Playwright smoke

### Batch D — Agent foundation

- agent profiles
- agent runs
- run events/traces
- policy evaluator
- narrow inbox-triage assistant

### Batch E — Knowledge and integrations

- knowledge documents
- SQLite FTS search
- browser/web clip capture
- note/transcript import profile expansion
- integrations management view
- later connector/plugin work via `#97` and `#98`

## Decision Rules For Promotion Into Canonical Docs

- `STATUS.md` only records current truth and current gaps.
- `IMPLEMENTATION_MASTERPLAN.md` records the new phased direction and issue reuse decisions.
- `INDEX.md`, `README.md`, and `USER_MANUAL.md` should become audience-first.
- Advanced/operator flows must be labeled as advanced everywhere they appear.
- Raw IDs may still exist as debug affordances, but docs should stop presenting them as the happy path.

## Short Conclusion

The repo does not need another broad expansion brainstorm.
It needs to promote this blueprint into the active docs as:

- a novice-first productization correction
- a backlog-reconciliation exercise that reuses existing issues where possible
- a staged expansion path that keeps agent/knowledge ambitions after the product becomes self-explaining
