# Weekly Smart CI report (template)

## Outcomes

- PR/gate critical path P50/P95:
- Runner minutes per merged PR:
- Hosted minutes/cost and rate-table version:
- Self-hosted wall time:
- Queue P50/P95:
- Selected-lane yield and failure yield:
- Flake/rerun rate:
- Duplicate exact-SHA qualifications:
- Cache hit utility:
- Artifact/storage emitted:

## Budget regressions

| Lane | Budget | Observed P95 | Delta | Reason | Owner/issue |
|---|---:|---:|---:|---|---|

## Flakes/quarantine

| Test | First failure | Rerun result | Issue | Owner | Expiry | Compensating coverage |
|---|---|---|---|---|---|---|

## Slow-test regressions

| Test/class | Previous P95 | Current P95 | OS | Suspected cause | Issue |
|---|---:|---:|---|---|---|

## Policy changes proposed

Each change needs evidence, expected benefit, risk and rollback. “Skip more jobs” is not an outcome by itself.
