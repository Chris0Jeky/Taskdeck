# Scalability and Elasticity

Score: **4.5 / 10**  
(This architecture is not designed for horizontal scaling, and that is probably fine if the goal is “single-node local-first”. But if you want SaaS elasticity, there are real architectural blockers.)

## 1) What currently blocks elasticity

### A) SQLite as primary datastore
- Single-writer behavior
- File-based storage makes multi-instance deployments risky

### B) In-process background workers
- Scaling API instances scales workers unintentionally
- Work claiming must be distributed-safe (hard with SQLite + file locks)

### C) In-memory, per-instance state
- Rate limiting is in-memory per instance (`AddRateLimiter`)
- SignalR presence tracking is in-memory (no backplane)
- Any “cache” implicitly becomes per instance

### D) No distributed coordination primitives
There is no Redis / message queue / distributed lock service.
That’s OK for a monolith, but it means:
- “scale out” is not a knob you can turn safely.

## 2) If you want to scale: a realistic migration path

### Step 1: Separate worker host
- Keep API as HTTP + SignalR
- Move LLM processing + webhook delivery to a worker service

### Step 2: Replace SQLite with Postgres
- Enables multiple app instances with safe concurrency
- Supports better indexing, counts, and query performance

### Step 3: Add Redis (or similar) for
- distributed rate limiting
- SignalR backplane
- distributed caching

### Step 4: Add a queue/bus
- e.g., RabbitMQ/SQS/Azure Service Bus
- workers consume jobs instead of polling DB

## 3) Elasticity considerations even in single-node mode

Even if you never scale out:
- put explicit statements in docs:
  - “single instance only”
  - “SQLite file should not be shared between containers”
- add guardrails:
  - startup check for “another instance already running” (lock file)
  - warnings when workers are enabled in multiple instances (if detectable)

## 4) Scalability quick wins (without major migration)
- Add paging and limits to list endpoints (logs, queue lists).
- Add DB indexes on high-cardinality columns.
- Replace some list-based health checks with COUNT queries.

## 5) Decision recommendation

If the product intent is:
- personal use / small team / self-hosted: current architecture is a good fit.
- SaaS / enterprise / high concurrency: start planning the Postgres + worker split early.
