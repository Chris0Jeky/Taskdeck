# Performance & Responsiveness Playbook (Practical)

Last Updated: 2026-02-21  
Applies to: Taskdeck API, worker, and web UI.

This playbook focuses on how to make Taskdeck **feel fast** (low perceived latency) and **be fast** (low actual latency), without overbuilding.

---

## 1) Define performance goals (numbers, not vibes)

Pick concrete targets. Example “solo dev product” targets:

### Frontend (perceived performance)
- First load:
  - LCP: <= 2.5s on a decent laptop on Wi‑Fi
  - INP: <= 200ms for common interactions (drag, open card, add card)
- Board view:
  - 500 cards renders without UI lockups
  - typing into inputs never janks
- Capture:
  - capture modal opens in <= 100ms
  - capture submit gives immediate feedback (optimistic UI)

### Backend (real performance)
- p95 API latency:
  - common read endpoints <= 100ms on local SQLite
  - create/update endpoints <= 200ms
- Worker:
  - queue processing throughput stable at configured concurrency
  - proposal generation does not block request threads

---

## 2) Instrumentation first (Taskdeck already started this)

You already have OpenTelemetry settings and worker metrics scaffolding.

Minimum baseline:

- Trace:
  - request duration
  - DB query duration (EF Core instrumentation)
  - worker iteration timings
- Metrics:
  - queue pending count
  - processing duration histogram
  - proposal creation duration

Rule:
- Do not “optimize” until you can measure before/after on at least one representative scenario.

---

## 3) Backend performance checklist (high leverage)

### 3.1 Avoid thread pool starvation
- No blocking on async (`.Result`, `.Wait()`).
- Keep hot endpoints fully async.
- Avoid synchronous IO.

### 3.2 EF Core patterns for speed
Use these when the endpoint is read-heavy:

- Prefer `AsNoTracking()` for read-only queries.
- Avoid loading large graphs unnecessarily:
  - projection into DTOs rather than returning entities
- Use split queries when necessary to avoid giant JOIN explosions.
- Add indexes for filters/sorts used frequently (verify with query plans).

### 3.3 Pagination everywhere (protect p95)
Any list endpoint should have:
- `limit` + a cursor or offset (cursor preferred long-term)
- server-side sorting
- a hard maximum limit (e.g., 200)

### 3.4 Caching (only after measurement)
Start with cheap wins:
- in-memory cache for “slow but stable” things (catalogs, starter packs, user prefs)
- ETag/If-None-Match for immutable resources (optional)

### 3.5 Queue/worker performance
- Batch size and concurrency should be tuned using a load harness.
- Avoid per-item DB roundtrips in tight loops:
  - fetch required board/column data once per item
- Make retries exponential (already in config).
- Use idempotency keys deterministically (so retries do not duplicate work).

---

## 4) Frontend responsiveness checklist (what makes it “feel premium”)

### 4.1 Keep interactions on the client-side fast
- Optimistic UI for:
  - add card
  - move card
  - capture submit
- UI should show “pending” state and recover cleanly if API fails.

### 4.2 Virtualize big lists
If any list can exceed ~100 items (cards, activity logs, queue items):
- use list virtualization (only render visible items)
- avoid expensive computed properties per item
- keep DOM node count bounded

### 4.3 Reduce reactive churn
- Watchers can become performance traps in Vue if overused.
- Prefer:
  - computed derived state with stable dependencies
  - narrow stores (feature-sliced stores) over giant global stores

### 4.4 Reduce bundle + render costs
- Code split views (Vite route-level splitting).
- Lazy-load heavy components (diff viewer, charts).
- Avoid large JSON payloads; use summaries.

---

## 5) Performance testing (cheap and effective)

### 5.1 Backend load harness (CI-friendly)
- Add a simple load test harness that can run locally:
  - seed DB with: 10 boards, 50 columns, 2000 cards
  - run read/write endpoints concurrently
- Measure:
  - p50/p95 latency
  - CPU usage
  - DB file contention

### 5.2 Frontend performance sanity checks
- Lighthouse on main pages
- Web Vitals in dev console
- a “large board” fixture for manual testing

---

## 6) Capture-specific responsiveness (ties directly to your product vision)

A capture tool dies if it’s slow.

### MVP behavior
- Hotkey opens capture composer immediately.
- User types/pastes.
- On submit:
  1) UI adds item locally with status `Pending`
  2) API call happens in background
  3) UI updates to “triaging…” / “triaged” when worker completes

### Later improvements
- If LLM triage is enabled:
  - show a progress indicator with stages:
    - “extracting tasks”
    - “generating proposal”
    - “ready for review”
- Provide fallback when LLM is slow/unavailable:
  - deterministic bullet parsing always works

---

## 7) Performance milestone roadmap (issue seeds)

1) **OBS-01 observability baseline** — ensure traces/metrics cover UI + worker + EF.
2) **TST-01 load/concurrency harness** — reproducible performance measurements.
3) **FE list virtualization** — cards/queue/logs where needed.
4) **API list pagination** — ensure stable p95 under growth.
5) **EF Core hot path optimization** — indexes, split queries, projections.
6) **Caching strategy ADR** — only after you have real profiling data.

---

## 8) References

- ASP.NET Core best practices (perf/reliability): https://learn.microsoft.com/en-us/aspnet/core/fundamentals/best-practices
- EF Core advanced performance topics: https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics
- Vue performance guide (virtualization): https://vuejs.org/guide/best-practices/performance
- Core Web Vitals thresholds (LCP/INP): https://developers.google.com/search/docs/appearance/core-web-vitals
