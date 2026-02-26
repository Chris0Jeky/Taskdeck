# API Rate Limiting Policy

Last Updated: 2026-02-26
Owner: Taskdeck maintainers
Linked issue: `#81` (SEC-06)

## Scope

This document defines Taskdeck API rate-limiting defaults, partition rules, response contract behavior, and tuning guidance.

## Runtime Policies

Taskdeck uses ASP.NET Core fixed-window rate limiting with partitioned keys.

Configured policies:

- `AuthPerIp`
  - partition key: client IP (`X-Forwarded-For` first hop, then remote IP)
  - target endpoints: `POST /api/auth/login`, `POST /api/auth/register`, `POST /api/auth/change-password`
- `CaptureWritePerUser`
  - partition key: authenticated user id (fallback to client IP when unavailable)
  - target endpoints: `POST /api/capture/items`, `POST /api/capture/items/{id}/triage`
- `HotPathPerUser`
  - partition key: authenticated user id (fallback to client IP when unavailable)
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

## Verification

- targeted API checks:
  - `dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release --filter "FullyQualifiedName~RateLimitingApiTests"`
- full backend verification:
  - `dotnet test backend/Taskdeck.sln -c Release -m:1`
