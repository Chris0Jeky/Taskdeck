# Rate Limiting and Abuse Protection (Deep Dive)

Score: **7 / 10**  
(Policy design is reasonable and documented, and forwarded-header trust is handled safely. Biggest gaps are coverage breadth, UX integration, and distributed behavior.)

## 1) What’s implemented

### Policies
Rate limiting is implemented using ASP.NET Core’s built-in rate limiter middleware with three named policies:

- `AuthPerIp`
- `HotPathPerUser`
- `CaptureWritePerUser`

Policies are applied via `[EnableRateLimiting("PolicyName")]` attributes on selected endpoints.

**Evidence:**
- `backend/src/Taskdeck.Api/RateLimiting/RateLimitPolicyNames.cs`
- `backend/src/Taskdeck.Api/Program.cs`
- Controllers: `AuthController`, `CaptureController`, `ChatController`, `LlmQueueController`

### Partition key design
- `AuthPerIp` partitions by `RemoteIpAddress`.
- User-based policies attempt to partition by authenticated user id; if unauthenticated, fall back to IP.

This is a sensible default:
- authenticated users can’t easily evade by changing IP (in most cases)
- unauthenticated users are limited by IP

### Rejection semantics
- Fixed-window limiter with `QueueLimit = 0` → rejected requests fail fast (good).
- `OnRejected` writes:
  - JSON error response with `errorCode=RateLimitExceeded`
  - `Retry-After` header (if available)
  - `X-RateLimit-Policy` header (policy name)

### Forwarded header trust model
The code explicitly does **not** trust forwarded headers unless `KnownNetworks` / `KnownProxies` are configured.

This is correct: many production outages and security issues come from trusting `X-Forwarded-For` blindly.

## 2) What’s good

1. **Documentation exists and matches implementation** (rare):
   - `docs/RATE_LIMITING_POLICY.md` is clear about limits, keys, and why.

2. **Test coverage exists**
   - There are integration tests verifying:
     - correct behavior when forwarded headers are untrusted
     - correct rate limiting behavior and headers

3. **Good failure UX contract on the API**
   - Consistent JSON error response and headers.

## 3) Where this can break or be abused

### A) Single-instance only
Built-in rate limiting is in-memory:
- If you deploy 2 API instances behind a load balancer, the effective limit doubles (and becomes inconsistent).

Mitigation:
- For true scale-out, move to distributed rate limiting (Redis) or enforce at the edge (nginx/Cloudflare/etc).

### B) Proxy/IP misconfiguration can cause accidental throttling
If the backend doesn’t trust forwarded headers:
- all clients appear to come from the proxy IP
- IP-based policies become “shared limits” → false throttles

Mitigation:
- Document required `KnownNetworks` for Docker deployments (optional profile).

### C) Coverage is selective
Only some endpoints are rate-limited.

You should decide whether “missing rate limit” is:
- intentional (documented), or
- an oversight.

High-leverage candidates:
- import/export endpoints
- webhook subscription creation/rotate-secret
- logs correlation endpoints
- any endpoint that triggers an LLM request or heavy DB work

### D) Fixed-window edge effects
Fixed-window limiters allow burst at the window boundary:
- an attacker can send N requests at the end of window + N at start of next window.

For many apps, this is acceptable.
If you need smoother behavior, consider sliding window or token bucket.

## 4) UX integration (frontend)

Backend returns Retry-After; frontend currently does not:
- read it and present a meaningful countdown
- lock out UI actions until retry is possible
- implement client-side backoff

Recommendation:
- Add an Axios interceptor for 429 to:
  - parse `Retry-After`
  - show a single toast per policy per cooldown period
  - optionally disable action UI temporarily

## 5) Suggested policy evolution

### Add a “global per-user” limiter
If you want to stop a single user from hammering many endpoints, add:
- `GlobalPerUser` policy (token bucket)

Apply to:
- all authenticated endpoints (or all write endpoints)

### Add cost-based limiting for LLM endpoints
Rate limit is a proxy for cost, but token usage is the real measure.

Add:
- per-user daily token budgets
- per-board budgets
- per-request max tokens

### Add edge enforcement
If deployed behind nginx, you can optionally enforce:
- IP-based limits at nginx (fast)
- keep per-user limits at app layer (needs JWT)

## 6) Concrete “next improvements” checklist

- Add rate limiting to expensive endpoints (imports, webhooks, logs).
- Add a shared helper method to create rate limiter policies (avoid drift).
- Add frontend 429 UX with Retry-After.
- If scaling is a goal:
  - define “rate limiting strategy per deployment mode”:
    - local-first single instance → in-memory ok
    - multi-instance → edge + Redis-based distributed
