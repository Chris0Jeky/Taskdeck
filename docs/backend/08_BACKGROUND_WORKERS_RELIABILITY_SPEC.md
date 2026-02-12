# Background Workers and Reliability Specification

Last Updated: 2026-02-12

## 1. Objective

Define hosted worker architecture that turns queue and proposal scaffolding into resilient runtime processing with clear failure behavior.

## 2. Worker Set

Required:
1. `LlmQueueToProposalWorker`
   - polls pending queue items
   - calls planner
   - creates proposals
   - marks queue request completed or failed
2. `ProposalHousekeepingWorker`
   - expires stale pending proposals
   - emits reminder/notification events if needed

Optional phase-2:
3. `RetentionCleanupWorker`
   - purges old logs/archive snapshots per retention policies

## 3. Scheduling and Throughput

Defaults:
- queue poll interval: 5 seconds
- max batch per cycle: 5 requests
- max concurrent request handlers: 2
- proposal expiry scan interval: 1 minute

Backpressure:
- if failure rate exceeds threshold, reduce concurrency automatically,
- expose degraded mode in health endpoint.

## 4. Retry and Dead-Letter Behavior

Retry strategy:
- transient failures: exponential backoff with jitter
- non-transient failures: no retry

Default retry budget:
- max retries: 3
- delays: 10s, 30s, 90s

Dead-letter behavior:
- after retry budget exceeded, mark queue item failed,
- persist terminal reason and emit alert metric.

## 5. State Machine Requirements

Queue request states:
- `Pending` -> `Processing` -> (`Completed` or `Failed`)
- `Pending` -> `Cancelled`
- `Failed` -> `Pending` (manual retry only)

Proposal states are defined in `04_AUTOMATION_FRAMEWORK_SPEC.md`.

## 6. Health and Readiness

Add endpoints:
- `GET /health/live`
- `GET /health/ready`

Readiness checks must include:
- DB connectivity,
- worker heartbeat freshness,
- queue lag threshold status.

## 7. Telemetry

Metrics:
- queue depth by status,
- queue age p50/p95,
- worker iteration duration,
- worker error rate,
- proposal generation success ratio.

Logs:
- structured worker start/stop events,
- per-item processing logs with correlation IDs,
- failure classification in logs.

## 8. Configuration Contract

`appsettings` section:

```json
{
  "Workers": {
    "QueuePollIntervalSeconds": 5,
    "MaxBatchSize": 5,
    "MaxConcurrency": 2,
    "MaxRetries": 3,
    "RetryBackoffSeconds": [10, 30, 90],
    "ProposalExpiryMinutes": 1440,
    "EnableAutoQueueProcessing": true
  }
}
```

## 9. Failure Modes and Guardrails

- stuck processing timeout:
  - detect processing age over threshold, mark failed with timeout reason.
- poison message detection:
  - repeated same validation failure -> fail permanently.
- duplicate processing protection:
  - claim row via transactional status transition.

## 10. Test Requirements

Unit:
- retry/backoff policy behavior,
- state transition validation,
- stuck item timeout logic.

Integration:
- worker processes pending queue items,
- dead-letter behavior after max retries,
- readiness endpoint reflects worker degradation.

Operational:
- startup/shutdown graceful behavior,
- restart recovery from in-flight records.

## 11. Acceptance Criteria

- queue requests progress automatically without manual process-next calls,
- failures are bounded and diagnosable,
- readiness reflects worker health and backlog risk accurately.
