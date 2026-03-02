# Performance and Cost

Score: **6.5 / 10**  
(For the likely target — local-first + SQLite — performance is probably fine. But several patterns will degrade quickly with large boards, large logs, or many users.)

## 1) Backend performance

### A) SQLite + EF Core characteristics
SQLite is:
- fast for small datasets
- single-writer (write contention under concurrency)
- not ideal for large multi-user workloads

This repo already carries SQLite-specific workarounds (notably around ordering by `DateTimeOffset`).

**Risk:** if you expect “team scale” usage with heavy concurrency, SQLite becomes a blocker.

### B) “Load all then filter/sort” patterns
Examples include:
- ordering boards in memory due to DateTimeOffset ordering issues
- capture service pulling all user queue items and filtering in memory
- health endpoints enumerating pending queue items to compute depth

These are correct, but they scale poorly.

**Suggested improvements (low complexity)**
- add query methods that push filtering to the DB:
  - `GetCaptureRequestsForUser(limit, status, typePrefix)`
  - `GetQueueDepthCountByStatus(status)`
- for ordering, store `CreatedAt` as a numeric epoch column (if you keep SQLite).

### C) N+1 and over-fetching
Some repositories use `Include` chains that load full object graphs:
- board + columns + cards + labels

This is usually fine for “open board view” but can be wasteful for:
- list views
- simple metadata queries
- background operations

**Recommendation**
- Add lightweight query variants:
  - `GetBoardSummaryAsync`
  - `GetBoardColumnsAsync`
  - `GetCardsByColumnAsync` with paging

### D) Rate limiter cost
The rate limiter is ASP.NET Core built-in fixed window:
- overhead per request is small
- storage is in-memory
- no queuing (queue limit = 0) so rejected requests fail fast

This is a good performance posture (fail fast), but:
- if you ever deploy multiple instances, rate limiting becomes inconsistent.

## 2) Worker performance

### A) Per-item scopes and Task.Run scheduling
Workers create a new DI scope per processed item and often wrap work in `Task.Run`.

This is not wrong, but it increases overhead.

**Possible improvement**
- Use `Parallel.ForEachAsync` or a controlled channel-based worker pool.
- Reuse HttpClient properly (already uses HttpClientFactory; good).

### B) Queue polling frequency
Polling loops with short delays can create “background noise” load.
This repo appears to use configurable delays/backoff (good).

Make sure:
- backoff caps exist
- “no work” paths don’t hammer the DB

## 3) Frontend performance

### A) Bundle size / initial load
- Tailwind + component-heavy boards can create large bundles.
- Vite build is fast, but you need to watch:
  - chunking strategy
  - lazy loading for secondary routes (ops, settings, etc.)

### B) Network usage
- Frequent board polling would be expensive, but the repo uses SignalR for realtime updates.
- SSE is used for chat streaming (good for UX, but be mindful of connection limits).

## 4) Cost drivers (LLM + webhooks)

This system has two direct “money sinks”:
- LLM requests
- outbound webhook deliveries (can generate traffic + downstream cost)

### LLM cost controls currently present
- feature flags / settings for enabling providers
- rate limiting on chat/capture endpoints
- token usage tracking in logs/queue records

### Cost gaps / recommended controls
- per-user and per-board **token budgets** (daily/weekly)
- per-request “max tokens” and model constraints
- caching/dedup for repeated requests where appropriate
- “circuit breaker” when provider errors spike (to avoid runaway retries)

## 5) Concrete performance experiments to run (once you can execute code)

If you want to validate performance assumptions, run:

1. k6 board-heavy load against:
   - list boards
   - open board details
   - create/move cards
2. Synthetic “large column reorder” benchmark
3. Queue backlog benchmark:
   - 1k pending requests
   - worker throughput and DB load

Then record:
- p95 latency
- DB query times
- worker processing rate
- memory growth

## Performance quick wins (low effort)
- Replace “queue depth” calculations with SQL COUNT queries.
- Add paging to any list endpoints that can grow unbounded (logs, queue lists, audit lists).
- Ensure `AsNoTracking` for read-only list operations where possible.
- Add indexes on:
  - audit logs (CreatedAt, BoardId, UserId)
  - webhook deliveries (Status, NextAttemptAt)
  - queue requests (Status, CreatedAt, UserId) — some already exist.
