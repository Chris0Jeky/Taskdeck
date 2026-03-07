# MVP Expansion Gap Map

Date: 2026-03-07
Scope: Reconcile the 2026-03-06 MVP expansion review packages against the current GitHub backlog and identify what should be reused, what is missing, and how the next seeding waves should be ordered.

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

The backlog had been stronger on engine and demo capability than on product legibility.
That gap is now represented by three explicit waves instead of scattered future notes:

- Wave P `#318` to `#328`
  - novice-first productization: `Home`, `Review`, `Today`, onboarding/help, readable proposals, first-run smoke
- Wave Q `#329` to `#334`
  - lower-priority secondary follow-through: demoability, harness maturity, saved views, broader note/clip intake
- Wave R `#335` to `#341`
  - later architecture follow-through: agent substrate, knowledge/search, supervised connector architecture, and explicit `R1` / `R2` / `R3` launch-gate framing

## Reuse Anchors

Real overlap that should be reused instead of duplicated:

- `#96` onboarding/contextual help
- `#93` global search/actions
- `#100` user guides/tutorials/FAQ
- `#216` thesis-aligned demo/landing baseline
- `#77` analytics/telemetry naming and dashboard work
- `#75` import adapters foundation
- `#97` plugin/extension RFC
- `#98` connector framework
- `#218` transcript capture source
- `#219` voice capture/transcription
- `#311` completed demo-epic hardening baseline

## Wave Mapping

### Batch A: novice-first shell and entry clarity

Now seeded in Wave P:

- `#320` workspace mode preference + `Home`
- `#322` `Review`-first routing + empty/help states + board selectors
- `#96` and `#93` remain reuse anchors for help/search breadth

### Batch B: board-centered daily workflow

Now seeded in Wave P:

- `#324` `Today` + first-run onboarding path
- `#326` proposal readability + board-centered action flow

### Batch C: docs/help/testing coherence

Now seeded in Wave P:

- `#100` user docs/help-center follow-through
- `#96` in-app help/onboarding follow-through
- `#328` first-run smoke + launch-criteria guardrail

### Batch D: agent substrate

Now seeded in Wave R:

- `#335` tracker
- `#336` agent profile/run/event foundation + manual-run API
- `#337` tool registry, policy evaluator, first bounded template
- `#338` agent mode surfaces and run-detail timeline

### Batch E: knowledge and integrations surface

Now seeded in Wave R:

- `#339` knowledge documents + SQLite FTS
- `#340` integrations registry + supervised connector foundation

Shared intake overlap stays intentionally split across:

- `#334`
- `#218`
- `#219`

### Secondary lower-priority follow-through

Now seeded in Wave Q:

- `#329` tracker
- `#330` in-app demoability/product evidence
- `#331` harness reporting/assertions/presets/soak
- `#332` replay-from-trace and scenario authoring
- `#333` saved views/productivity follow-through
- `#334` broader note-style import and clip intake follow-through

## Recommended Order

### Immediate

1. Wave P
2. Wave Q only after Wave P is underway or delivered

### Later architecture

3. Wave R only after Wave Q is stable enough that agent/knowledge breadth will not compete with product-legibility work

Reason:

- the blueprint is explicit that agents, traces, knowledge, and connector breadth should formalize only after the human product is understandable

## Release Framing

Recommended release framing from the expanded package:

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
  - tool registry and policy evaluator
  - first bounded template
- `R3` knowledge/integrations alpha
  - searchable notes/docs
  - integrations page
  - at least two meaningful inbound context/capture paths

## Operational Seeding Rules

- Keep the seeded productization wave indexed in `#107` as the canonical wave map.
- Use `docs/analysis/2026-03-07_mvp-expansion-source-coverage-audit.md` as the audit ledger for anything promoted only partially or carried into later waves.
- Keep Wave Q (`#329` to `#334`) below Wave P in urgency.
- Keep Wave R (`#335` to `#341`) below Wave Q in urgency.
- Reuse the overlap issues above instead of cloning scope into disconnected new tickets.
- Keep `MINIMAL` as the near-horizon filter when `EXPANDED` suggests broader future breadth.
- Do not let plugin/connector/agent breadth outrun `Home` / `Today` / `Review` productization.
