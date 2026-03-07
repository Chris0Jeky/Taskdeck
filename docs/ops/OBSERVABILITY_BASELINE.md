# Observability Baseline (OBS-01)

Last Updated: 2026-02-20  
Related issue: `#68`

## Purpose

Define the baseline telemetry, dashboard, and alerting contract for Taskdeck runtime operations.

## OpenTelemetry Wiring

Backend API exports telemetry from:
- ASP.NET Core request instrumentation
- HttpClient instrumentation
- Custom Taskdeck activity source: `Taskdeck.Api`
- Custom Taskdeck meter: `Taskdeck.Api`

Configuration (`backend/src/Taskdeck.Api/appsettings*.json`):
- `Observability:EnableOpenTelemetry`
- `Observability:ServiceName`
- `Observability:OtlpEndpoint`
- `Observability:EnableConsoleExporter`
- `Observability:MetricExportIntervalSeconds`

## Stable Metric Names and Dimensions

Metrics emitted by Taskdeck custom meter:
- `taskdeck.automation.queue.backlog`
  - dimensions: `taskdeck.queue.name`
- `taskdeck.worker.items.processed`
  - dimensions: `taskdeck.worker.name`, `taskdeck.outcome`
- `taskdeck.worker.item.processing.duration` (ms)
  - dimensions: `taskdeck.worker.name`, `taskdeck.outcome`
- `taskdeck.housekeeping.proposals.expired`
  - dimensions: `taskdeck.worker.name`
- `taskdeck.worker.heartbeat.staleness` (s)
  - dimensions: `taskdeck.worker.name`, `taskdeck.outcome`

## Stable Trace Attributes

Correlation middleware tags the active request span with:
- `taskdeck.correlation_id`
- `taskdeck.request_id`

Worker spans include:
- `taskdeck.worker.name`
- `taskdeck.llm.request_id`
- `taskdeck.llm.request_type`
- `taskdeck.user.id`
- `taskdeck.board.id` (when available)

## Baseline Dashboard Definition

Recommended panels:
1. API request latency (`http.server.request.duration`) p50/p95/p99 by route/status.
2. API request error rate (5xx and 4xx share) by route.
3. Automation queue backlog (`taskdeck.automation.queue.backlog`).
4. Worker processed outcomes (`taskdeck.worker.items.processed`) split by outcome.
5. Worker processing duration (`taskdeck.worker.item.processing.duration`) p95.
6. Worker heartbeat staleness (`taskdeck.worker.heartbeat.staleness`) max by worker.

## Alert Threshold Baseline

Suggested initial alerts:
1. API error rate > 5% for 5m.
2. API p95 request latency > 1500ms for 10m.
3. Queue backlog > 100 for 10m.
4. Queue worker heartbeat staleness > 30s for 3 consecutive samples.
5. Proposal housekeeping heartbeat staleness > 180s for 3 consecutive samples.

## Non-Prod Smoke Verification Path

1. Set `Observability:EnableConsoleExporter=true` in Development config.
2. Start API and execute a board/card mutation and one ops command.
3. Call `GET /health/ready`.
4. Verify console exporter output includes:
   - HTTP spans and metrics
   - `taskdeck.*` metrics listed above
   - `taskdeck.correlation_id` tag on request spans
5. Reset console exporter setting after verification.
