# Reliability and Resilience

Score: **7.5 / 10**  
(The system shows deliberate reliability engineering: leasing, stuck recovery, consistent error contracts. The main reliability risks are cross-instance behavior and “ops endpoints exposed as normal endpoints”.)

## 1) Error handling and API behavior

### Strengths
- Central unhandled exception middleware returns a consistent JSON error contract:
  - no stack traces leaked to clients
  - correlation IDs are present
- Result→HTTP mapping is consistent across controllers.

**Evidence:**
- `backend/src/Taskdeck.Api/Middleware/UnhandledExceptionMiddleware.cs`
- `backend/src/Taskdeck.Api/Extensions/ResultExtensions.cs`

### Weaknesses
- Some domain exceptions are returned directly as messages.
  - Good for UX; potentially risky if messages include sensitive details.
- You should standardize which errors can include detail.

## 2) Background workers (queue + webhooks)

### Strengths
- Workers follow a “poll → claim → process → update status” pattern.
- Webhook delivery includes:
  - stuck-work recovery
  - retry scheduling
  - isolation of outbound HTTP with connect callback guard
- Health endpoints report worker heartbeat / queue depth.

### Risks / failure modes
- **Multi-instance deployments:** if you scale API horizontally without careful configuration:
  - two workers may process the same queue item unless claims are fully atomic
  - presence tracking (SignalR) becomes inconsistent without a backplane
  - rate limiting becomes inconsistent (per-instance)

- **Crash in the middle of processing:**
  - if an item is marked “Processing” and worker dies, recovery needs to re-queue it.
  - there is stuck recovery logic in some workers; ensure it exists for all critical queues.

## 3) Data integrity

### Strengths
- EF Core migrations exist; schema is explicit.
- UnitOfWork has custom handling for notification deduplication unique constraint.

### Risks
- Auto-migrations at startup (`Database.Migrate()`) can:
  - block startup
  - cause multi-instance migration races
  - be unsafe if schema changes are not backwards compatible

For local-first this is often acceptable; for SaaS it’s not.

## 4) Idempotency and duplicate suppression

Outbound webhooks: the architecture implies eventual delivery, but idempotency keys and duplicate suppression are not fully described in docs.

**Recommendation**
- For deliveries, include an idempotency header (delivery id) and document it.
- For inbound API writes, consider client-provided idempotency keys for “create card”, “capture submit”, etc., if you expect retries.

## 5) Observability as a reliability tool
Correlation IDs are already present. To make reliability actionable:
- include correlationId in *all* logs and worker status updates
- record error codes and categories for SLO dashboards

## 6) Reliability recommendations (prioritized)

### P0/P1
1. Lock down ops-like endpoints (LLM queue process-next/status/stats) — reliability and security.
2. Add “stuck processing” recovery semantics to any queue that can be claimed.
3. Clarify whether the system is intended to be:
   - single-node local-first, or
   - multi-node scalable
   Then align reliability mechanisms accordingly.

### P2
4. Add retention policies (logs, webhook deliveries, queue items) to prevent unbounded DB growth.
5. Add explicit “safe shutdown” behavior:
   - workers should stop accepting new work before shutdown
   - in-flight work should be marked for retry if aborted

## 7) Reliability “tabletop exercise” scenarios

If you want to stress the design, walk through:

- DB file becomes read-only or disk fills up
- LLM provider returns 500s for 30 minutes
- webhook endpoint starts timing out
- two instances of API accidentally start against the same DB volume
- user spams chat/capture endpoints and triggers 429s

For each, define:
- expected user-facing behavior
- recovery action
- telemetry signals you need to detect it quickly
