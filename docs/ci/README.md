# docs/ci — Smart CI Fabric

Last Updated: 2026-08-30

The CI programme that lets Taskdeck go private for v0.3.0 on a personal GitHub Pro account without
weakening verification. Decision: [ADR-0066](../decisions/ADR-0066-smart-ci-fabric-and-private-repository-runner-trust.md).
Tracker: CI-00 `#2324` (children `#2325`–`#2339`). Human-only actions: `OUTSTANDING_TASKS.md` §J.

| Document | What it holds |
| --- | --- |
| [SMART_CI.md](SMART_CI.md) | Architecture and operating model: invariants, risk/trust classes, control-plane placement in personal mode, lanes, event topology, gate, receipts, rollout phases, commands, file map |
| [RUNNER_TOPOLOGY_AND_THREAT_MODEL.md](RUNNER_TOPOLOGY_AND_THREAT_MODEL.md) | Isolated self-hosted runner design, assets, adversaries, controls, trust matrix, runbook, revisit triggers |
| [PRIVATE_REPO_CUTOVER_CHECKLIST.md](PRIVATE_REPO_CUTOVER_CHECKLIST.md) | The cutover checklist (decisions, measurement, planner/gate, topology, right-sizing, runners, supply chain, nightly/release, rehearsal, manual cutover, rollback) |
| [CI_BASELINE.md](CI_BASELINE.md) | The measured baseline (window, method, findings) with the generated ledgers under `baselines/` |
| `baselines/` | Generated `ci-estate-<date>.json` / `.md` from `scripts/ci/smart-ci/measure-ci-estate.mjs` — append, never overwrite history |

Evidence and provenance: the maintainer's 2026-08-30 pack as received plus the pack-versus-repository
reconciliation live in [`docs/analysis/2026-08-30-smart-ci/`](../analysis/2026-08-30-smart-ci/RECONCILIATION.md).
The live CI topology stays documented in the header of `.github/workflows/ci-required.yml` and in
`docs/STATUS.md` §CI Status.
