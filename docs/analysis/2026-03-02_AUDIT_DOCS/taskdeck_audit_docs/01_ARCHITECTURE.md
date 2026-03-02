# Architecture and System Design

## System context (what this repo is, in concrete terms)

At a high level, Taskdeck is a **single logical product** made of:

- **Web UI** (Vue SPA)
- **API + background workers** (ASP.NET Core)
- **Database** (SQLite via EF Core)
- **Optional external integrations**
  - LLM providers (OpenAI/Gemini)
  - Outbound webhooks to arbitrary external endpoints

### Current “reference deployment” topology

```
Browser
  |
  |  (HTTP)
  v
nginx reverse proxy  (deploy/nginx/reverse-proxy.conf)
  |                       |                      -> static frontend container
  v
Taskdeck.Api container (ASP.NET Core)
  |
  v
SQLite DB file (volume bind)
```

This is very much a **monolith** (in a good way for a local-first tool): one API process owns HTTP + web sockets + workers + DB access.

## Backend architecture (Clean Architecture style)

The backend is split into projects:

- `Taskdeck.Domain` — entities + domain rules (no ASP.NET / EF dependencies)
- `Taskdeck.Application` — use cases/services, DTOs, interfaces
- `Taskdeck.Infrastructure` — EF Core, repositories, persistence details
- `Taskdeck.Api` — controllers, middleware, DI wiring, hosted background services

### What’s strong here

**1) Layer boundaries are not only “documented”; they are enforced.**

There is a dedicated architecture test project:
- `backend/tests/Taskdeck.Architecture.Tests/ProjectReferenceBoundariesTests.cs`
- `backend/tests/Taskdeck.Architecture.Tests/SourceLayerPurityTests.cs`

This is an unusually strong move: it prevents drift (“just reference DbContext from Application, it’s faster”) that tends to kill maintainability.

**2) The controllers are generally thin and service-driven.**

Most controllers:
- read current user from claims (`AuthenticatedControllerBase`)
- check board-level permissions via `Taskdeck.Application.Services.AuthorizationService`
- call Application services
- map `Result<T>` → consistent HTTP error contract

This yields a predictable API surface and keeps HTTP concerns out of business logic.

**3) The system has explicit “cross-cutting” middleware with clear responsibility.**

Examples:
- correlation IDs (`CorrelationIdMiddleware`)
- security headers (`SecurityHeadersMiddleware`)
- unhandled exception mapping (`UnhandledExceptionMiddleware`)

### Architectural weak points / inconsistencies

**A) The API host runs background workers.**
- This is OK for a local-first product, but it makes scaling, deployment, and operational blast radius more complex.
- If you ever run >1 API instance, you need careful “exactly-once” semantics, distributed locks, or a separate worker service.

**B) Some “ops/internal” surfaces are mixed with normal user surfaces.**
- Example: LLM queue endpoints appear globally scoped.
- This suggests internal-ops endpoints should either:
  - be moved into a separate “ops” controller area with stronger auth, or
  - be deployed behind a separate interface, or
  - be removed from public deployments.

**C) Configuration models live in Application layer.**
Settings classes like `JwtSettings`, `RateLimitingSettings`, `SecurityHeadersSettings` live under `Taskdeck.Application.Services`.

This isn’t *wrong*, but it mixes “app/business rules” with “host/platform policy knobs”.
If the app grows, you may prefer:
- `Taskdeck.Api.Contracts` or `Taskdeck.Api.Configuration` for host concerns
- keep Application settings only when the **use case itself** depends on configuration

**D) Repository/UoW abstraction vs EF Core reality**
You have a classic repository/UoW layer, but:
- several repositories use raw SQL to work around SQLite behaviors (valid)
- some application services still rely on “load all then filter” patterns

This can become a maintenance tension:
- either embrace repositories and add purpose-built query methods (preferred), or
- accept more EF-in-Application patterns (but that violates your architecture tests)

## Frontend architecture

Frontend is a standard modern Vue stack:

- Vue 3 + Router
- Pinia stores
- Axios-based API layer with typed DTO-ish interfaces
- Tailwind CSS
- Unit tests (Vitest) + E2E tests (Playwright)

### Strengths

- **`strict: true` TypeScript** configuration is enabled.
- API access is centralized (`src/api/*`), reducing “random fetch calls”.
- There is a consistent pattern for auth token handling and route guards.

### Weak points

- Auth token is stored in `localStorage` and attached as a bearer token on requests.
  - This is a common choice, but it makes XSS dramatically higher impact.
  - It also complicates future work like refresh tokens, token rotation, etc.

- HTTP error handling is mostly “best effort” (log to console, show message).
  - Rate limiting UX (429) is not first-class yet.

## Architectural fit vs likely product intent

If the goal is: **a local-first team tool** with a single-node deployment and modest concurrency:

- This architecture is *excellent* — it has the ergonomics of a monolith but the clarity of clean boundaries.

If the goal shifts to: **a multi-tenant SaaS** (internet exposed, horizontally scalable):

- SQLite + in-process workers + in-memory rate limiting and presence tracking become hard blockers.
- You would likely need:
  - Postgres or similar
  - background worker host separated from the HTTP API
  - distributed cache/message bus
  - distributed rate limiting and distributed SignalR backplane

## Architectural recommendations (practical)

### Near-term (low disruption)

1. **Define “internal ops endpoints” and isolate them**
   - prefix `/api/ops/*` (already used by Ops CLI) and move queue ops into it
   - enforce stronger auth (role, operator token, IP allowlist)

2. **Normalize “actor identity”**
   - stop accepting `userId` in request bodies except where cryptographically necessary (e.g., password reset tokens)
   - prefer `sub` from JWT

3. **Treat SQLite workarounds as first-class**
   - codify them in repository methods (e.g., “ordered by createdAt in SQL if possible else fallback”)
   - add performance notes + tests for ordering semantics

### Mid-term (more structural)

4. **Split workers into a separate host**
   - keep shared Application/Infrastructure
   - run “worker” as a separate .NET host process that consumes queue items

5. **Formalize module boundaries**
   - e.g., `Boards`, `Capture`, `Automation`, `Webhooks`, `Ops`, `Auth`
   - this helps both humans and AI agents maintain consistent patterns
