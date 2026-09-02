# Taskdeck v0.4 development-acceleration bundle

**Snapshot:** 2026-08-30  
**Repository:** https://github.com/Chris0Jeky/Taskdeck  
**Inspected `main`:** `221aa88c80f5b2c3265ac794edc2ade0edd70c72`  
**Primary target:** milestone 5, `v0.4 — Hosted Open Beta + Work Model + Fabric Foundation`  
**Secondary target:** high-leverage milestone 4 residuals that can still accelerate v0.3 without duplicating merged work

This bundle converts the milestone into an executable development system rather than a flat backlog. It contains:

- one implementation pack and one ready-to-paste issue update for each of the 24 milestone-5 issues;
- a dependency DAG, collision map, task queue, file-ownership plan and agent claim protocol;
- granular architecture for Context Fabric, work model, worker containment, hosted beta, SSE streaming, performance and Smart CI;
- implementation candidates in C#, Python, SQL and JSON Schema;
- executable CI/ops/analysis utilities with tests;
- migration proofs, adversarial cases, fixture catalog and acceptance matrices;
- ten rendered architecture diagrams plus source DOT files;
- a milestone-4 tracker-drift and residual-work audit;
- an ingestion prompt and coordinator protocol for a repository agent.

## Start here

1. Read `00_EXECUTIVE/EXECUTIVE_SUMMARY.md`.
2. Open `dashboard.html` for a filterable overview.
3. Read `VALIDATION_REPORT.md`, then give `07_AGENT_HANDOFF/UNBUNDLE_PROMPT.md` to the repository coordinator agent.
4. Require the coordinator to refresh live issue/PR state before posting or implementing anything.
5. Use `07_AGENT_HANDOFF/task-queue.json` and `file-ownership-map.json` to assign conflict-bounded worktrees.

## Important trust boundary

The bundle is source-grounded at the commit above, but the candidate C# files were not compiled against a full local checkout. Treat them as reviewed, compile-shaped reference implementations. The Python utilities and JSON files are validated by `07_AGENT_HANDOFF/scripts/verify_bundle.py` and their unit tests.

## Recommended repository placement

Keep the full intake record under:

```text
docs/analysis/2026-08-30-v04-acceleration-bundle/
```

Move durable outputs only after review:

- architecture decisions → `docs/architecture/` or `docs/decisions/`;
- issue diagrams → `docs/architecture/diagrams/`;
- CI schemas/scripts → `ci/` and `scripts/ci/`;
- benchmark tooling → `scripts/analysis/` and `benchmarks/`;
- runbooks → `docs/ops/`;
- candidate source → the owning issue branch, never directly from the intake PR.

## Validation

Run from the bundle root:

```bash
python3 07_AGENT_HANDOFF/scripts/verify_bundle.py .
python3 -m unittest discover -s 04_TESTING/python-tests -p 'test_*.py'
```

The generated `SHA256SUMS.txt` lets an agent verify that the intake has not been silently altered.
