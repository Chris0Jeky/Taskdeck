# Taskdeck Hardening & Performance Analysis

**Date:** 2026-04-16
**Scope:** Performance bottlenecks, hardening opportunities, resilience gaps, and operational maturity
**Companion:** `docs/AUDIT.md`, `docs/QA_STRATEGY.md`, `docs/EXPANSION_ROADMAP.md`

---

## Executive Summary

Taskdeck performs well for local-first, single-user-to-small-team use. Current architecture supports **~5-10 MAU** comfortably. With the quick fixes documented below (20 hours total effort), the system can support **~50+ MAU** before requiring architectural changes (PostgreSQL, worker separation).

| Area | Current State | Target State | Gap Size |
|------|--------------|-------------|----------|
| API throughput | ~100 req/s (SQLite bottleneck) | ~1000 req/s (PostgreSQL) | Large |
| Response size | 5-10MB uncompressed | 500KB-1MB compressed | Easy fix |
| Query performance | 10-100ms (missing indexes) | <1ms | Easy fix |
| Frontend bundle | Lazy-loaded (16/18 views) | Optimized | Small |
| Realtime (SignalR) | In-memory (single instance) | Redis backplane ready | Medium |
| Caching | Board list only | Query-level + write-through | Medium |
| Worker scaling | Single process, no redundancy | Separate service, horizontal | Large |

---

## 1. Performance Quick Fixes (Tier 1 — Do This Week)

### 1.1 Enable Response Compression

**Current**: No `AddResponseCompression()` in Program.cs. API responses transmitted uncompressed.
**Impact**: 90% bandwidth reduction. A 1000-card board detail response drops from ~5MB to ~500KB.
**Effort**: 1 hour.

```csharp
// Program.cs
services.AddResponseCompression(options => {
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});
// Before routing:
app.UseResponseCompression();
```

### 1.2 Add Missing Database Indexes

**Current**: AuditLog, LlmRequest, and some Card queries lack covering indexes.
**Impact**: 10-100x query speedup on tables with 1000+ rows.
**Effort**: 1 hour (EF migration).

Missing indexes:
```sql
-- AuditLog: Used by activity queries (filtered by userId/boardId + ordered by timestamp)
CREATE INDEX IX_AuditLogs_UserId_Timestamp ON AuditLogs(UserId, Timestamp DESC);
CREATE INDEX IX_AuditLogs_BoardId_Timestamp ON AuditLogs(BoardId, Timestamp DESC);

-- LlmRequest: Used by queue worker (filtered by userId/status)
CREATE INDEX IX_LlmRequests_UserId_Status ON LlmRequests(UserId, Status);

-- Card: Used by board detail loading
CREATE INDEX IX_Cards_BoardId_ColumnId ON Cards(BoardId, ColumnId);
```

### 1.3 Fix Synchronous I/O in WorkspaceService

**RESOLVED** (PERF-11 `#847`/PR `#904`): `.Result` in `GetOnboardingForPreferenceAsync` replaced with sequential `await`. Round 2 review reverted an unsafe `Task.WhenAll` parallelisation in `GetHomeAsync`/`GetTodayAsync` because all repositories share a scoped EF `DbContext` via `IUnitOfWork`, which is not thread-safe. Parallel execution of DbContext-backed queries requires architectural changes (per-request DbContext or dedicated repository instances) and is out of scope for this quick win.

### 1.4 Paginate Board List Endpoint

**RESOLVED** (PERF-12 `#848`/PR `#909`): `GET /api/boards` accepts `offset`/`limit` query parameters (default 50, server-clamped `[1, 200]`); response is now `PaginatedResult<BoardDto>` with `items`/`totalCount`/`hasMore`/`offset`/`limit`. Authorization filter is applied before pagination so `totalCount` reflects only boards the caller is authorised to see. `BoardRepository.SearchIdsAsync` uses stable ordering (`CreatedAt DESC`, `Id` tiebreaker) so offset-based pages do not overlap. Frontend `boardsApi.getBoards` delegates to `getBoardsPaginated` and returns `.items` for backward compatibility. 11 new `BoardPaginationApiTests` cover cross-user isolation, limit clamping, empty list, and full-page iteration.

**Follow-up (still open)**: audit and activity endpoints also need pagination.

### 1.5 Move AuditLog Filtering to SQL

**RESOLVED** (PERF-13 `#849`/PR `#903`): `AuditLogRepository.QueryAsync` SQLite branch now uses `BuildSqliteQueryAsync` that pushes `userId`, `boardId`, `source`, and `level` into a parameterised raw SQL query alongside `ORDER BY` and `LIMIT`. Board-scoped filtering uses `EXISTS` subqueries (matching `GetByBoardAsync`) rather than a materialised `EntityId IN (...)` list, avoiding the SQLite `SQLITE_MAX_VARIABLE_NUMBER` limit on boards with many cards/columns/labels and removing 3 extra round-trips from `ResolveBoardScopedEntityIdsAsync` for SQLite. Non-SQLite providers still use LINQ `Contains()` after ID materialisation.

---

## 2. Backend Hardening (Tier 2 — Do This Month)

### 2.1 Configuration Validation at Startup

**RESOLVED** (OPS-27 `#863`/PR `#908`): data annotations (`[Required]`, `[Range]`, `[MinLength]`, `[RegularExpression]`, `[Url]`) added to 15 settings classes — `JwtSettings`, `WorkerSettings`, `CacheSettings`, `RateLimitingSettings`, `LlmProviderSettings`, `OpenAiProviderSettings`, `GeminiProviderSettings`, `ObservabilitySettings`, `SentrySettings`, `SecurityHeadersSettings`, `TelemetrySettings`, `LlmQuotaSettings`, `LlmToolCallingSettings`, `AbuseDetectionSettings`, `MfaPolicySettings`. Four cross-property `IValidateOptions<T>` validators enforce: `WorkerSettings.RetryBackoffSeconds.Length >= MaxRetries`, `JwtSettings.SecretKey` non-empty + minimum length, `SentrySettings.Dsn` required when `Enabled`, and `RateLimitingSettings` nested policy ranges. All wired via a new `RegisterValidatedOptions<T>` helper that calls `ValidateDataAnnotations()` + `ValidateOnStart()`. `LlmProvider`/`CacheProvider` regex patterns use `(?i)` flag to match the runtime case-insensitive comparison. `JwtSettings.SecretKey` is intentionally not `[Required]` because `FirstRunBootstrapper` generates it before validation runs. 34 `OptionsValidationTests` cover data-annotation boundaries and cross-property rules.

### 2.2 Database Migration Infrastructure

**RESOLVED** (`#864`): 21 EF Core migrations are in source control and the full chain applies cleanly to a fresh SQLite database. `MigrationBootstrapTests` (5 tests) guard against regression. Developer workflow documented in `docs/platform/EF_MIGRATION_WORKFLOW.md`.

### 2.3 API Error Code Registry

**Gap**: No centralized documentation of all possible error codes and HTTP status mappings.

**Fix**: Create `ErrorCodesRegistry.cs` documenting all error codes, and add OpenAPI schema validation for error response shapes.

### 2.4 Health Check Enhancement

**Current**: `/health/ready` and `/health/live` exist with basic checks.

**Add per-service health endpoints**:
- `/health/database` — SQLite file accessible + schema version
- `/health/cache` — Redis/InMemory status
- `/health/llm-provider` — Provider reachable (probe endpoint exists)
- `/health/signalr` — Backplane connected (if configured)

### 2.5 Centralize MCP DI Registration

**Gap**: `Program.cs` manually registers MCP services, duplicating logic from `ApplicationServiceRegistration.cs`.

**Fix**: Extract MCP service registration into a shared extension method to prevent divergence.

### 2.6 Worker Configuration Validation

**Gap**: No validation that `RetryBackoffSeconds.Length >= MaxRetries`.

**Fix**: Add cross-property validation in WorkerSettings class.

---

## 3. Frontend Hardening (Tier 2)

### 3.1 Error Boundary Implementation

**Gap**: No Vue error boundary. Component render errors crash the entire application.

**Fix**: Add global error handler in `main.ts`:
```typescript
app.config.errorHandler = (err, instance, info) => {
  console.error('Component error:', err, info);
  // Show fallback UI, log to Sentry
};
```

Consider a dedicated `ErrorBoundary.vue` component for critical subtrees.

### 3.2 HTTP Retry with Exponential Backoff

**Gap**: No retry logic in HTTP interceptor. Network failures not recovered.

**Fix**: Add Axios interceptor for transient failures (5xx, network errors):
```typescript
axiosRetry(http, {
  retries: 3,
  retryDelay: axiosRetry.exponentialDelay,
  retryCondition: (error) =>
    error.response?.status >= 500 || !error.response
});
```

### 3.3 View Decomposition

**Target**: No view exceeds 400 lines.

| View | Current Lines | Decomposition Strategy |
|------|--------------|----------------------|
| ~~ReviewView~~ | ~~1,659~~ 148 | DONE (PR #923) — 6 components + 2 composables |
| ~~InboxView~~ | ~~1,527~~ 222 | DONE (PR #921) — 2 panels + 1 composable + utils |
| ~~AutomationChatView~~ | ~~1,523~~ 235 | DONE (PR #920) — 7 components + 1 composable |
| MetricsView | 920 | Extract MetricsCharts, MetricsTable, ForecastPanel |
| HomeView | 804 | Extract WorkspaceSummary, QuickActions, RecentActivity |

### 3.4 Loading State Consistency

**Gap**: TdSkeleton exists but not used in ReviewView, MetricsView, CalendarView.

**Fix**: Add skeleton layouts for all views that fetch data on mount.

### 3.5 Responsive Design

**Gap**: Only 8 media queries. Mobile board view broken (fixed 16rem column widths).

**Priority breakpoints**: 640px (mobile), 768px (tablet), 1024px (desktop).

**Key mobile fixes**:
- Board: Switch from horizontal kanban to vertical card list on mobile
- Sidebar: Collapse to hamburger menu
- Modals: Full-screen on mobile
- Capture: Touch-optimized input area

### 3.6 Session Timeout Warning

**Gap**: JWT expires silently. User sees redirect to login without warning.

**Fix**: Add countdown toast 5 minutes before token expiry. Offer "Extend Session" action.

---

## 4. Security Hardening (Tier 2)

### 4.1 SSRF Protection

**RESOLVED** (SEC-26 `#850`/PR `#905`): new `SsrfProtectionService` in Application layer provides `ValidateUrl`, `ValidateUrlWithDnsAsync`, and `ValidateLlmProviderUrl`, wrapping `OutboundWebhookEndpointGuard`. All previously listed blocked ranges are enforced (private IPv4 `127/8`, `10/8`, `172.16/12`, `192.168/16`; IPv6 `::1`, `fc00::/7`, `fe80::/10`; IPv4-mapped IPv6; `localhost`). Defense-in-depth additions: cloud metadata hostname blocking (`metadata.goog`, `metadata.google.internal`, Alibaba Cloud `100.100.100.200`, AWS IMDSv2 IPv6 `fd00:ec2::254`). `LlmProviderSelectionPolicy` now calls `ValidateLlmProviderUrl` on `OpenAi`/`Gemini` `BaseUrl` at provider selection time (invalid URLs fall back to Mock). LLM `HttpClient`s use `OutboundWebhookConnectCallback` for DNS-level protection against rebinding, and `AllowAutoRedirect = false` prevents redirect-based bypass. `allowLocalhostEndpoints` is toggled only for Development with `Llm:AllowLiveProvidersInDevelopment = true` so local Ollama/LM Studio workflows still function. 83 `SsrfProtectionServiceTests` cover decimal/hex/octal IP bypass, DNS rebinding, and HTTPS enforcement.

### 4.2 Secret Hygiene

**RESOLVED** (SEC-27 `#851`/PR `#911`): hardcoded `Jwt:SecretKey` removed from `appsettings.Development.json`. `FirstRunBootstrapper.EnsureJwtSecret` now runs unconditionally in all environments (Development and CI/headless included — round 2 review confirmed the headless guard caused CI auth failures) and writes a random 32-byte base64 secret to `appsettings.local.json` (atomically moved via sibling temp file under a named cross-process mutex, `#913`). `TestWebApplicationFactory` supplies its own in-memory JWT secret so API tests are environment-independent. Developers can alternatively use `dotnet user-secrets` or the `Jwt__SecretKey` environment variable (documented in `CONTRIBUTING.md`).

### 4.3 CSP Hardening

**Gap**: `style-src 'unsafe-inline'` allows inline style injection.

**Fix**: Migrate inline styles to external CSS classes or use nonce-based CSP.

### 4.4 Import File Validation

**RESOLVED** (SEC-30 `#860`/PR `#910`): new `FileContentValidator` in Application layer with `ValidateTextContent` (binary/null-byte/C1-control detection; all C1 controls `0x80-0x9F` blocked because in UTF-16 they are genuine control chars — real Windows-1252 smart quotes decode to `U+2018-U+201D`) and `ValidateJsonContent` (BOM-aware parse + SQLite magic-byte rejection via `SQLite format 3\0` header check). Character-based limits (renamed from `MaxMarkdownContentBytes` → `MaxMarkdownContentChars`) preserve CJK/emoji safety — the earlier byte-based cap rejected UTF-8 multi-byte content at ~1/3 of the intended character limit. Wired into `NoteImportController` (markdown + web clip), `ExternalImportsController` (CSV), and `ExportController` (board JSON, with `maxBytes: 0` to preserve round-trip portability since export has no size cap). 52 `FileContentValidatorTests`.

### 4.5 Audit Trail Retention

**Gap**: Audit log grows indefinitely.

**Fix**: Document and implement retention policy (e.g., 2-year retention with configurable archive/purge).

---

## 5. Resilience Improvements

### 5.1 Circuit Breaker for External APIs

**Gap**: LLM providers and OAuth endpoints can fail cascading. No circuit breaker.

**Fix**: Add Polly circuit breaker for external HTTP calls:
```csharp
services.AddHttpClient("llm-provider")
    .AddTransientHttpErrorPolicy(p =>
        p.CircuitBreakerAsync(5, TimeSpan.FromMinutes(1)));
```

### 5.2 Graceful Degradation for LLM Unavailability

**Current**: Mock provider fallback exists but is config-gated.

**Improve**: When live provider fails N times, auto-degrade to mock with user notification. Auto-recovery after cooldown period.

### 5.3 Database Write Contention Mitigation

**Current**: SQLite serializes all writes. Concurrent POSTs queue on database lock.

**Mitigations** (before PostgreSQL migration):
- Enable WAL mode: `PRAGMA journal_mode=WAL;`
- Add busy timeout: `PRAGMA busy_timeout=5000;`
- Queue writes through a single writer (channel pattern)

### 5.4 Offline Mutation Queue (Frontend)

**Gap**: Changes made while offline are lost.

**Fix**: Queue mutations in IndexedDB when offline. Sync with server on reconnect. Show pending items in UI.

---

## 6. Scalability Planning

### Tier 1: 5-50 MAU (Quick fixes above)
- Response compression
- Database indexes
- Query pagination
- SQL-level filtering
- Result caching (board lists, capture summary)

### Tier 2: 50-500 MAU (PostgreSQL + Infrastructure)
- PostgreSQL migration (ADR-0023 accepted, runbook exists)
- Redis caching (ICacheService already abstracted)
- Redis rate limiting (replace in-process)
- SignalR Redis backplane (ADR-0025, ready to enable)
- Separate worker process

### Tier 3: 500+ MAU (Horizontal Scaling)
- Kubernetes/ECS with autoscaling
- Read replicas for heavy-read endpoints
- CDN for static assets
- Queue-based worker scaling (Hangfire/Temporal)
- Event-driven architecture (domain events)

### Cost Estimates (from ADR-0027)
- Single ECS Fargate: ~$147-152/month
- With Redis + PostgreSQL RDS: ~$250-300/month
- Autoscaling targets: CPU 65%/25%, 1000 req/min, 500 WebSocket connections
- SLO targets: 99.5% availability, p95 read <300ms, write <800ms

---

## 7. Observability Gaps

### Current State
| Component | Status |
|-----------|--------|
| Health endpoints | Implemented (/health/ready, /health/live) |
| OpenTelemetry | Baseline configured (ASP.NET + HttpClient instrumentation) |
| Sentry (error tracking) | Available, opt-in, disabled by default |
| Product telemetry | Opt-in event taxonomy (35+ events, not yet implemented) |
| Frontend performance marks | 7 budgets, console warnings |
| Logging | Console stdout (no structured aggregation) |

### Gaps

| Gap | Priority | Fix |
|-----|----------|-----|
| No monitoring/alerting rules | High | Define 5xx rate, p95 latency, disk/memory alerts |
| No production dashboards | High | Grafana or CloudWatch for key metrics |
| No structured log aggregation | Medium | Serilog + centralized sink (ELK/Datadog) |
| No distributed tracing end-to-end | Medium | W3C trace context to all external calls |
| No synthetic uptime monitoring | Medium | Ping /health/ready from external service |
| No RUM (Real User Monitoring) | Low | Core Web Vitals collection |
| Sentry disabled by default | Low | Enable for production deployments |

### Recommended Alerting Rules

| Alert | Threshold | Action |
|-------|-----------|--------|
| 5xx error rate > 1% for 5 min | P1 | Page on-call |
| API p95 latency > 2s for 5 min | P2 | Investigate |
| Disk usage > 80% | P2 | Expand or cleanup |
| Memory usage > 85% for 10 min | P2 | Investigate leak |
| Worker heartbeat missing > 5 min | P1 | Restart worker |
| Database lock wait > 10s | P2 | SQLite contention |
| LLM provider failures > 50% for 5 min | P3 | Degrade to mock |

---

## 8. Docker & Deployment Hardening

### Container Hardening Checklist

| Item | Current | Target |
|------|---------|--------|
| HEALTHCHECK directive | Missing | `HEALTHCHECK CMD curl -f http://localhost:8080/health/ready` |
| Non-root user | Running as root | `RUN adduser app && USER app` |
| Resource limits | No limits | `cpus: '1.0', memory: 512m` (backend), `cpus: '0.5', memory: 256m` (frontend) |
| Logging driver | Default | `json-file` with `max-size: 10m, max-file: 3` |
| Network isolation | Default bridge | Dedicated network per service |
| Read-only filesystem | Not set | `read_only: true` + tmpfs for temp dirs |
| Security options | None | `no-new-privileges: true` |

### Deployment Hardening

| Item | Current | Target |
|------|---------|--------|
| Graceful shutdown | Not configured | SIGTERM handler with drain window |
| Database migration locking | No lock | Advisory lock before migration |
| Secret rotation automation | Manual | Scripted rotation (SSM/Vault) |
| Backup verification | Untested | Quarterly restore drill |
| Rollback automation | Manual | Scripted previous-slot switch |

---

## 9. Operational Maturity Assessment

| Capability | Level | Evidence |
|------------|-------|----------|
| CI/CD pipeline | Advanced | 27 workflows, multi-lane, SBOM |
| Test automation | Advanced | 7,070+ tests, adversarial review |
| Documentation | Advanced | 338 files, 30 ADRs |
| Security baseline | Intermediate | CSP, rate limiting, auth. Gaps in SAST, SSRF |
| Monitoring | Basic | Health endpoints only |
| Alerting | Absent | No alerting rules defined |
| Incident response | Intermediate | Runbooks exist, rehearsal cadence |
| Disaster recovery | Basic | Backup scripts, DR runbook, untested |
| Performance testing | Basic | k6 advisory, budgets defined, not gated |
| Capacity planning | Basic | Cost estimates exist, no load baselines |

---

## 10. Priority Implementation Order

### This Week (8 hours)
1. Response compression (1h) — Open
2. Missing database indexes (1h) — Open
3. ~~Fix WorkspaceService sync I/O (30m)~~ RESOLVED (PERF-11 PR `#904`)
4. ~~SSRF protection for webhooks (2h)~~ RESOLVED, extended to LLM provider URLs (SEC-26 PR `#905`)
5. ~~Remove dev JWT secret (15m)~~ RESOLVED (SEC-27 PR `#911`)
6. ~~Board list pagination (2h)~~ RESOLVED (PERF-12 PR `#909`)
7. ~~AuditLog SQL-level filtering (2h)~~ RESOLVED (PERF-13 PR `#903`)

### This Month (40 hours)
8. ~~Config validation at startup (4h)~~ RESOLVED (OPS-27 PR `#908`)
9. Vue error boundary (2h) — Open
10. HTTP retry interceptor (3h) — Open
11. View decomposition — ReviewView (8h) — Open
12. View decomposition — InboxView (8h) — Open
13. ~~Database migration infrastructure (4h)~~ RESOLVED: bootstrap regression tests + workflow guide (OPS-28 PR `#907`)
14. Docker hardening (HEALTHCHECK, USER, limits) (4h) — Open
15. CSP inline style migration (4h) — Open
16. ~~Import file magic byte validation (3h)~~ RESOLVED (SEC-30 PR `#910`)

### This Quarter (100 hours)
17. ~~View decomposition — remaining oversized views (24h)~~ RESOLVED (PRs #920, #921, #923)
18. Responsive design for mobile (16h)
19. PostgreSQL migration (40h)
20. ~~Monitoring/alerting setup (16h)~~ RESOLVED (OPS-30 PR `#914`)
21. Distributed rate limiting (8h)

---

## Estimated Capacity After Fixes

| Phase | Estimated MAU | Key Enabler |
|-------|--------------|-------------|
| Current | 5-10 | SQLite + no compression |
| After Tier 1 (this week) | 20-30 | Compression + indexes + pagination |
| After Tier 2 (this month) | 50+ | Caching + SQL filtering + error resilience |
| After PostgreSQL (Q3 2026) | 500+ | Concurrent writes + horizontal scaling ready |
| After full scaling (Q4 2026) | 5,000+ | Redis + workers + CDN + autoscaling |
