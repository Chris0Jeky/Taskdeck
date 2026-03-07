# Testing, Metrics, and Operations

The product is now strong enough that quality work should shift from “does the code exist?” to “does the experience remain coherent under change?”.

## Recommended quality stack

### 1. Deterministic product smoke

Keep this as required:

- register/login
- create first project
- capture one item
- review one proposal
- execute it
- verify board result

This is the real P0 smoke test.

### 2. Scenario-driven acceptance tests

Keep expanding JSON scenarios and demo director.
Use them as acceptance fixtures.

### 3. Live-provider supervised tests

Keep these opt-in and non-blocking unless you have stable environment support.
Use them for:

- prompt/schema breakage detection
- agent run realism
- capture triage quality

### 4. Manual product walkthroughs

Run a short weekly dogfood protocol and record friction.

## Metrics that matter now

### Novice-first product metrics

- time to first value (from register/login to first board mutation)
- capture save time
- capture -> proposal latency
- proposal review -> execution latency
- proposal execution success rate
- inbox triage completion rate
- percent of sessions using Home or Today successfully

### Agent workspace metrics

- runs started / completed / failed
- proposal creation rate from runs
- average run steps
- average tokens/cost per run
- auto-apply rate by risk/tool
- human override/reject rate

### UX quality metrics

- page-level empty-state dwell time
- board picker usage vs raw ID fallbacks
- “failed with unreadable error” events
- navigation backtracks between pages in a short session

## Product telemetry suggestions

Emit events like:

- `home_loaded`
- `today_loaded`
- `capture_modal_opened`
- `capture_created`
- `capture_triage_clicked`
- `proposal_opened`
- `proposal_approved`
- `proposal_executed`
- `board_action_capture_here_clicked`
- `workspace_mode_changed`
- `agent_run_started`
- `agent_run_completed`
- `agent_run_failed`

Keep payloads privacy-safe and avoid raw text content.

## Launch criteria for a polished novice-first beta

You can call it polished enough for novice testing when:

1. Home exists and is the default landing page.
2. Today exists and is useful.
3. Review exists and proposals are readable.
4. No common flow requires raw IDs.
5. Every main page has a helpful empty state.
6. A new user can create first value in <2 minutes.
7. The daily dogfood loop is sustainable for a week.

## Launch criteria for agent workspace alpha

You can call it an agent workspace when:

1. agents can be created and scoped
2. runs are first-class and inspectable
3. runs can create proposals or artifacts
4. policies exist and are enforced
5. traces exist and are readable
6. at least 2 narrow assistant templates are useful in practice

## Suggested test matrix

### Frontend component/view tests

- Home view states
- Today view states
- Review view summary + actions
- workspace mode nav changes
- proposal summary card
- board action rail
- board picker/search selector
- onboarding checklist
- agent run detail timeline (later)

### Backend unit/application tests

- workspace summary query service
- proposal summary service
- agent policy evaluator
- agent run service state transitions
- knowledge search query service
- home/today aggregation edge cases

### API integration tests

- `/api/workspace/home`
- `/api/workspace/today`
- `/api/workspace/review/summary`
- `/api/agents/*`
- `/api/agent-runs/*`
- `/api/knowledge/*`

### Playwright acceptance flows

- first-run golden path
- daily review path
- board-scoped assistant path
- agent run creates proposal path
- knowledge import -> assistant -> proposal path

## Operational guidance

### Demo director becomes more than demo tooling

Treat it as:

- stakeholder recorder
- acceptance test artifact generator
- regression smoke evidence
- scenario benchmark harness

### Add a report mode

A future step should render a static HTML report from `run-summary.json`, `snapshot.json`, and screenshots.
That will give you a single artifact for demos, CI, and manual review.

## Performance hotspots to watch

- inbox lists over hundreds/thousands of items
- activity feed long histories
- proposal list filters and diff loading
- board lane rendering with many cards
- agent run timelines and events
- knowledge search result ranking

Use pagination, virtualization, and coarse summaries before chasing exotic optimizations.
