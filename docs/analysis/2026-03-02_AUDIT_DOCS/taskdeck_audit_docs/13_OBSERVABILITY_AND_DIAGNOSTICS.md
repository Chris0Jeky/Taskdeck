# Observability and Diagnostics

Score: **8 / 10**  
(Correlation IDs + health endpoints + OpenTelemetry hooks are already present. The remaining work is “turning instrumentation into an operational story”: dashboards, alerting, and retention.)

## 1) What exists now

### Correlation IDs
- Middleware accepts `X-Request-Id` (validated and length-limited).
- Response echoes `X-Request-Id`.
- Correlation IDs are stored with logs and used by Ops CLI runs.

**Evidence:**
- `backend/src/Taskdeck.Api/Middleware/CorrelationIdMiddleware.cs`
- `backend/tests/Taskdeck.Api.Tests/OpsCliApiTests.cs` correlation coverage

### Health endpoints
- `/api/health` and deeper health checks exist.
- Deep health includes queue depth and worker heartbeat.

**Evidence:**
- `backend/src/Taskdeck.Api/Controllers/HealthController.cs`

### OpenTelemetry scaffolding
- OTel is set up with meters/activities.
- Worker instrumentation exists.

**Evidence:**
- `backend/src/Taskdeck.Api/Telemetry/*`

## 2) What’s missing for real operations

### Dashboards and SLOs
You need standard charts:
- request rate, latency (p50/p95), errors by route
- auth failures
- 429 rate limit rejections
- queue depth over time
- worker throughput and failure rates
- webhook delivery success rate and retry counts

### Alerting
Define thresholds:
- queue depth > N for > M minutes
- webhook failure rate > X%
- auth failures spike
- DB health check failing

### Log retention
If logs are stored in DB, you need:
- retention policy
- storage limit
- vacuum/maintenance schedule

## 3) Recommended telemetry additions (low effort, high value)

### Backend metrics
- `http.server.requests` by route + status
- custom counters:
  - `taskdeck.rate_limit.rejected` (with policy name)
  - `taskdeck.llm.tokens_used` (already implied)
  - `taskdeck.webhook.deliveries` (success/failure/retry)

### Structured logs
- Ensure every log includes:
  - correlationId
  - userId (when available)
  - boardId (when relevant)
  - feature/module tag

### Frontend instrumentation
- capture client-side errors
- capture “429 cooldown triggered” events (helps tune rate limits)

## 4) Practical “make it real” plan

1. Pick a telemetry backend (Prometheus+Grafana, OTLP collector, etc.).
2. Add a `deploy/observability` compose profile with:
   - OTel Collector
   - Prometheus/Grafana
3. Publish a baseline dashboard JSON.
4. Add a short ops runbook:
   - where to look for queue issues
   - how to identify a stuck worker
   - how to debug webhook failures
