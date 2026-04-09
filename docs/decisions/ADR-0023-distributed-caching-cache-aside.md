# ADR-0023: Distributed Caching — Cache-Aside Pattern with Redis

- **Status**: Proposed
- **Date**: 2026-04-09
- **Deciders**: Project maintainers

## Context

Issue #85 (PLAT-02) requires a distributed caching strategy with well-defined cache-invalidation semantics. Taskdeck's board listing endpoint is a high-read, low-write path that benefits from caching. The system is local-first with SQLite persistence, so the caching layer must degrade gracefully when no external cache is available.

Key requirements:
- Cache hot read paths (board listing) to reduce database load
- Define explicit TTL, key strategy, and invalidation triggers
- Cache failures must never break correctness — safe degradation to no-cache mode
- Observability: hit/miss/error metrics for cache effectiveness analysis
- Support both distributed (Redis) and local (in-memory) cache backends

## Decision

Adopt the **cache-aside** (lazy-loading) pattern with two interchangeable implementations:

1. **Redis-backed** (`RedisCacheService`) for production/multi-instance deployments
2. **In-memory** (`InMemoryCacheService`) using `ConcurrentDictionary` with periodic sweep for local dev and test

The abstraction lives in `Taskdeck.Application` as `ICacheService`. Implementations live in `Taskdeck.Infrastructure`.

### Cache-Aside Flow

```
Read:  Check cache → hit? return cached → miss? load from DB → store in cache → return
Write: Mutate DB → invalidate cache key(s)
```

### Key Strategy

- Board list: `boards:user:{userId}` (user-scoped because board visibility depends on authorization)
- Keys are prefixed with `td:` namespace to avoid collisions in shared Redis instances
- Board detail is intentionally NOT cached: `BoardDetailDto` includes columns with card counts, and `ColumnService`/`CardService` mutate that data without cache awareness. Caching board detail would serve stale column/card information after sibling-service mutations.

### TTL Policy

- Board list: 60 seconds (short TTL — list changes frequently with board creation/archival)
- All TTLs are configurable via `appsettings.json`

### Invalidation Triggers

- Board create/update/delete/archive/unarchive: invalidate `boards:user:{ownerId}` cache for the board owner
- For simplicity in the initial implementation, board list cache is invalidated per-owner (invalidate the acting user's list cache on mutation)

### Safe Degradation

- All cache operations are wrapped in try/catch
- On cache error: log warning, proceed without cache (transparent to caller)
- No exceptions propagated from cache failures
- Cache unavailability does not affect data correctness

### Metrics

- Cache hit/miss/error counters emitted via `ILogger` structured logging
- Metric names: `cache.hit`, `cache.miss`, `cache.error`
- Tagged with `cache_key_prefix` for per-resource analysis

## Alternatives Considered

- **Write-through**: Updates cache on every write. Adds latency to write paths and complexity for multi-key invalidation. Rejected because Taskdeck's write patterns are relatively simple and cache-aside is simpler to reason about for invalidation correctness.

- **Read-through**: Cache itself is responsible for loading on miss. Requires tighter coupling between cache and data access layers, violating the clean architecture boundary (cache would need repository references). Rejected.

- **No caching**: Simplest option. Adequate for single-user local-first usage but would not scale for multi-user or hosted deployments (PLAT expansion strategy). Rejected for forward-compatibility reasons, though the fallback mode effectively provides this.

- **EF Core second-level cache**: Third-party packages like `EFCoreSecondLevelCacheInterceptor` exist but couple caching decisions to the ORM layer rather than the application layer. Rejected for lack of explicit invalidation control and observability.

## Consequences

- Board listing endpoint gains cache-aside behavior with measurable hit rates (board detail intentionally excluded — see Key Strategy)
- New `ICacheService` abstraction available for future hot paths (cards, columns, proposals)
- Redis becomes an optional infrastructure dependency (not required for local dev)
- Cache invalidation correctness must be maintained as new board mutation paths are added
- TTL values may need tuning based on observed usage patterns

## References

- Issue: #85 (PLAT-02: Distributed caching strategy and cache-invalidation semantics)
- Related: `BoardService`, `BoardsController`, `InMemoryActiveUserCache` (existing per-request cache pattern)
- Platform expansion: ADR-0014, #531
