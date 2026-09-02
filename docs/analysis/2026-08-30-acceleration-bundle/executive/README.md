# Executive and planning material — snapshot-time, superseded by live state

Archived verbatim from `00_EXECUTIVE`, `01_MILESTONE_5` and `07_AGENT_HANDOFF` of the bundle grounded at
`221aa88c8` (2026-08-30). Every count, "recommended state", starter lane and queue position here describes
the snapshot, not today. Read them for the *framing* (four programmes, contract-first integration train,
collision fences, critical path), then take the live state from `../HEAD_START.md` and the per-issue files
in `../issues/`.

| File | What survives | What is stale |
| --- | --- | --- |
| `EXECUTIVE_SUMMARY.md` | Four-programme framing; highest-value-moves ordering | CF-01 "execute as five PRs" (shipped, PR `#2344`); "finish backup + connector-key proof" (shipped, PRs `#2361` / `#2360`) |
| `DECISION_QUEUE.md` | The decisions that still need a maintainer | Any decision whose issue closed since |
| `FIRST_72_HOURS.md` | The idea of a bounded first wave | Its lanes; the first wave already ran under `#2348` |
| `OPTIONS_AND_TRADEOFFS.md`, `RISK_REGISTER.md` | Risk framing for hosted beta and migrations | Nothing structurally; re-read against `../HEAD_START.md` |
| `COLLISION_MATRIX.md`, `file-ownership-map.json` | Which shared files collide (Card, DTOs, proposal operations, exports, EF snapshot, registrations) | File paths must be re-checked; see the work-model curated files |
| `MILESTONE_5_INDEX.md`, `MILESTONE_5_CRITICAL_PATH.md`, `MILESTONE_5_PARALLEL_WORKSTREAMS.md` | Dependency shape | Recommended states and counts (24 → live counts in `../HEAD_START.md`) |
| `INTEGRATION_TRAIN.md` | The integration-owner pattern for migrations and shared contracts | Nothing; not yet adopted as process |
| `dependency-graph.json` | Edge list | Advisory only: many pairs repeat as both `depends-on` and `unblocks` without a documented direction (`../RECONCILIATION.md`) |

Not archived: the 106-item task queue, claim locks, agent prompt templates, coordinator protocol and the
agent-receipt / issue-contract / task schemas — bundle tooling for an orchestration model Taskdeck does not
run. `dashboard.html` was a snapshot visualization and is likewise dropped.
