# API Rate Limiting Policy

Last Updated: 2026-03-02
Owner: Taskdeck maintainers
Linked issue: `#81` (SEC-06)

## Scope

This document defines Taskdeck API rate-limiting defaults, partition rules, response contract behavior, and tuning guidance.

## Runtime Policies

Taskdeck uses ASP.NET Core fixed-window rate limiting with partitioned keys.

Trust boundary note:
- Taskdeck does not trust raw `X-Forwarded-For` request headers for partitioning.
- If deployed behind reverse proxies/load balancers, configure `ForwardedHeaders:KnownProxies` and/or `ForwardedHeaders:KnownNetworks` so trusted forwarded-header middleware can promote canonical client IPs to `RemoteIpAddress` before rate-limit partitioning.
- Set `ForwardedHeaders:ForwardLimit` to match the trusted proxy-hop depth between clients and Taskdeck (default `1`).
- When those allowlists are not configured, Taskdeck keeps the connection IP unchanged and will not trust caller-provided forwarding headers.
- Activation rule: forwarded-header trust is inactive unless at least one trusted proxy/network value is configured; `ForwardLimit` by itself is not enough.

Example trusted multi-hop proxy configuration:

```json
"ForwardedHeaders": {
  "ForwardLimit": 2,
  "KnownNetworks": [
    "10.0.0.0/24",
    "10.0.1.0/24"
  ],
  "KnownProxies": []
}
```

Configured policies:

- `AuthPerIp`
  - partition key: trusted client IP from `HttpContext.Connection.RemoteIpAddress`
  - target endpoints: `POST /api/auth/login`, `POST /api/auth/register`, `POST /api/auth/change-password`
- `CaptureWritePerUser`
  - partition key: authenticated user id (fallback to trusted connection IP when unavailable)
  - target endpoints: `POST /api/capture/items`, `POST /api/capture/items/{id}/triage`
- `HotPathPerUser`
  - partition key: authenticated user id (fallback to trusted connection IP when unavailable)
  - target endpoints:
    - `POST /api/llm-queue`
    - `POST /api/llm-queue/process-next`
    - `POST /api/llm/chat/sessions`
    - `POST /api/llm/chat/sessions/{id}/messages`
    - `GET /api/llm/chat/sessions/{id}/stream`

## Default Limits

Source: `backend/src/Taskdeck.Api/appsettings.json`

- `AuthPerIp`: `20` requests / `60` seconds
- `HotPathPerUser`: `30` requests / `60` seconds
- `CaptureWritePerUser`: `10` requests / `60` seconds
- `ForwardedHeaders`:
  - `ForwardLimit`: `1` (single trusted hop)
  - `KnownProxies` / `KnownNetworks`: empty by default (safe no-trust posture until explicitly configured)

Development overrides (`appsettings.Development.json`) are intentionally higher to reduce local friction:

- `AuthPerIp`: `120` requests / `60` seconds
- `HotPathPerUser`: `120` requests / `60` seconds
- `CaptureWritePerUser`: `60` requests / `60` seconds

## Throttle Response Contract

When a request is rejected by rate limiting:

- HTTP status: `429 Too Many Requests`
- content type: `application/json`
- body contract:
  - `errorCode`: `TooManyRequests`
  - `message`: human-readable retry guidance
- headers:
  - `Retry-After`: integer seconds to wait before retrying
  - `X-RateLimit-Policy`: matched policy name

This preserves stable API error-contract behavior for throttling paths.

## Tuning Guidance

Adjust values under `RateLimiting` based on:

- authentication abuse risk (`AuthPerIp`)
- provider-cost pressure and worker throughput (`HotPathPerUser`)
- capture ingestion throughput and queue pressure (`CaptureWritePerUser`)

Operational guidance:

- increase limits only with observed sustained false positives and supporting telemetry
- reduce limits when abuse/cost patterns increase or queue depth grows unexpectedly
- keep `AuthPerIp` stricter than authenticated user-keyed hot-path limits

## Emergency Controls

If rate limiting causes user-impacting false positives during an incident:

- temporary kill switch: set `RateLimiting__Enabled=false` and restart API hosts
- expected impact: all throttle protections are disabled until rollback
- rollback path:
  - re-enable `RateLimiting__Enabled=true`
  - confirm trusted forwarded-header allowlists are correct for deployed proxy topology
  - re-run smoke verification before full traffic restoration

## Verification

- targeted API checks:
  - `dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release --filter "FullyQualifiedName~RateLimitingApiTests"`
- full backend verification:
  - `dotnet test backend/Taskdeck.sln -c Release -m:1`
- pre-production operator smoke checks:
  - verify rate-limit contract (`429`, `Retry-After`, `X-RateLimit-Policy`) on burst auth/login calls
  - verify two distinct client IPs do not share an `AuthPerIp` bucket in deployed proxy topology
