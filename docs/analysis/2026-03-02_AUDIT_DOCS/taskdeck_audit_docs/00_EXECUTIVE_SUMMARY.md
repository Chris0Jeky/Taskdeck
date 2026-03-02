# Taskdeck Repo Audit (feature: api rate limit + abuse protection)

Audit date: 2026-03-02 (Europe/London)  
Scope: static review of the provided repository snapshot (`Taskdeck-feature-api-rate-limit-abuse-protection`).  
Reviewer: ChatGPT (static analysis only — build/runtime tests were not executed in this environment).

## Repository snapshot at a glance

**Primary goal (inferred):** a local-first task/board system with automation (LLM-assisted proposals), capture/triage flows, outbound webhooks, notifications, and an ops console.  
**Stack:**
- Backend: .NET 8 (ASP.NET Core), “Clean Architecture” layering (Domain / Application / Infrastructure / Api)
- Frontend: Vue 3 + TypeScript + Pinia + Vite
- Deploy baseline: Docker Compose (nginx reverse proxy + API container + static web container)
- Storage: SQLite (via EF Core)

**Codebase size (approx):**
- Backend production code: **33,788 LOC** across **266 files**
- Backend tests: **24,241 LOC** across **102 files**
- Frontend production code: **21,926 LOC** across **152 files**
- Frontend tests: **1,689 LOC** across **11 files**

> Note: The repo’s own `docs/TESTING_GUIDE.md` claims a very large passing automated test corpus. I did not re-run it here (no `dotnet` toolchain available in this execution environment).

## Scorecard (10 = best-in-class)

These scores reflect *general software engineering expectations* (including multi-user / internet-exposed deployments), not only “works for a local single-user dev box”.

| Category | Score | What drives the score |
|---|---:|---|
| Architecture & boundaries | **8.5** | Clear Domain/Application split, repository/UoW, architecture boundary tests, consistent Result→HTTP mapping. |
| Security | **6.0** | Many good baselines (JWT, security headers, SSRF guard, rate limiting). **But several high-impact authz / role / password-policy gaps exist.** |
| Abuse protection / rate limiting | **7.0** | Reasonable policy set + explicit forwarded-header trust model + tests. Still lacks distributed limits and broader endpoint coverage. |
| Testing & quality gates | **9.0** | Repo shows unusually extensive tests + guides + boundary tests + E2E & load harness docs. |
| Reliability & resilience | **7.5** | Background workers include leasing/retry, heartbeat, stuck recovery. Some cross-instance and “ops endpoints” risks remain. |
| Performance | **6.5** | Appropriate for SQLite + local-first. Several hot paths load whole collections; queue endpoints can be heavy; health endpoints use list retrieval not COUNT. |
| Deployability & operations | **8.0** | Docker baseline, env config patterns, health checks, OTel scaffolding. TLS/hardening is documented but not “default-on”. |
| Maintainability | **7.5** | Structure + docs are strong. Some dependency hygiene and “policy vs implementation drift” needs attention. |
| Extensibility | **7.5** | Interfaces for LLM providers, import adapters, notifications, realtime; reasonable modular seams. |
| Scalability / elasticity | **4.5** | SQLite + in-process workers + in-memory rate limiting/presence tracking constrains horizontal scaling. |
| UI/UX | **7.0** | Typed frontend, sensible API wrappers, E2E coverage. Some UX gaps around 429 handling, auth token storage, accessibility details. |
| Observability | **8.0** | Correlation IDs, health endpoints, OTel metrics/tracing hooks, worker metrics. Dashboards/SLOs not included. |

## The “big wins” (strengths)

1. **Clean layering + enforcement.** Domain/Application isolation is backed by architecture tests (`backend/tests/Taskdeck.Architecture.Tests/*`).
2. **Consistency of API error contract.** The API returns structured error responses and uses centralized middleware for unhandled errors.
3. **Security baseline is unusually proactive for an early-stage repo.** CSP/HSTS/etc middleware, correlation IDs, SSRF guard for webhooks, and rate limiting are all present.
4. **Workers are not naive.** Webhook delivery and LLM queue processing use leasing/claiming and stuck-work recovery patterns (good reliability posture).
5. **High test ambition.** There are integration tests for authz, CORS, security headers, rate limiting, etc., plus frontend E2E and load harness docs.
6. **Explicit deployment baseline.** Compose + nginx reverse proxy + docs for environment variables and hardening.

## The “big risks” (what I’d fix first)

These are ranked by “how badly can this go wrong if exposed to untrusted users”.

### P0 (high severity, should be fixed before any serious multi-user or public deployment)

1. **Role escalation via registration / user creation**
   - `CreateUserDto` includes `DefaultRole`, and both `/api/auth/register` and `/api/users` accept it without server-side restriction.
   - Any user can self-register as `Owner` or `Admin` if those roles unlock privileged endpoints (they do for Ops CLI).
   - Evidence: `backend/src/Taskdeck.Application/DTOs/UserDtos.cs`, `backend/src/Taskdeck.Application/Services/AuthenticationService.cs`, `backend/src/Taskdeck.Application/Services/UserService.cs`.

2. **No password policy (including “empty password” possibility)**
   - Registration and password change do not validate `Password`/`newPassword` for emptiness/length/strength.
   - BCrypt will happily hash empty strings, so accounts can be created with empty passwords.
   - Evidence: `AuthenticationService.RegisterAsync()` and `ChangePasswordAsync()`.

3. **LLM queue endpoints appear globally scoped (cross-user visibility + control)**
   - `LlmQueueController` has endpoints that return **all** requests by status and can claim/process the next request, with no “current user” scoping and no role gating.
   - That’s both an information leak and an operational control channel if multiple users exist.
   - Evidence: `backend/src/Taskdeck.Api/Controllers/LlmQueueController.cs` + `Taskdeck.Application/Services/LlmQueueService.cs`.

### P1 (material risk / likely to bite in production)

4. **Outdated dependency hygiene**
   - `Taskdeck.Infrastructure.csproj` references `Microsoft.AspNetCore.Http` **2.3.9** while the rest targets .NET 8.
   - Even if it “works”, it’s a red flag for compatibility and known vulnerability surface.
   - Evidence: `backend/src/Taskdeck.Infrastructure/Taskdeck.Infrastructure.csproj`.

5. **CSP relies on `'unsafe-inline'` (script/style)**
   - This materially weakens CSP as an XSS mitigation (especially given JWT stored in localStorage).
   - Evidence: `backend/src/Taskdeck.Api/appsettings.json` + `SecurityHeadersMiddleware`.

6. **Rate limiting is single-instance in-memory**
   - Fine for local-first. Not fine behind a load balancer with multiple API instances.
   - Evidence: `Program.cs` uses built-in `AddRateLimiter()` fixed window limiters.

### P2 (quality + operational)

7. **Some endpoints/paths load whole collections and then filter in memory**
   - Reasonable for small datasets, but not robust if boards/cards/logs grow.
   - Evidence: several repositories and services (e.g., board ordering workaround; capture service filtering; health checks).

8. **Mismatch between app upload limits and proxy upload limits**
   - Nginx `client_max_body_size 10m`, while DB import default limit is 50 MB.
   - Evidence: `deploy/nginx/reverse-proxy.conf`, `DatabaseExportImportSettings`.

## Immediate remediation checklist (concrete)

- Force `DefaultRole` on registration to `Editor` (ignore client value). Add an admin-only route to change roles if needed.
- Enforce password constraints (min length, deny empty, possibly complexity) in registration + change-password. Add tests.
- Lock down LLM queue operational endpoints:
  - either scope to `currentUserId`, or
  - move behind an “operator token” / service-to-service auth / admin role.
- Upgrade/remove `Microsoft.AspNetCore.Http` 2.3.9.
- Tighten CSP (remove unsafe-inline via nonces/hashes; or at least confine unsafe-inline to styles if unavoidable).
- Add 429 UX handling in frontend (show retry-after countdown; avoid spamming).

## Reading guide for the rest of this audit bundle

This audit is split into “one doc per category”:

- `01_ARCHITECTURE.md`
- `02_SECURITY.md`
- `03_TESTING_AND_QUALITY.md`
- `04_ALGORITHMS_AND_DOMAIN_LOGIC.md`
- `05_PERFORMANCE_AND_COST.md`
- `06_RELIABILITY_AND_RESILIENCE.md`
- `07_DEPLOYABILITY_AND_OPERATIONS.md`
- `08_MAINTAINABILITY_AND_CODE_HEALTH.md`
- `09_EXTENSIBILITY_AND_MODULARITY.md`
- `10_SCALABILITY_AND_ELASTICITY.md`
- `11_UI_UX_AND_ACCESSIBILITY.md`
- `12_DATA_MODEL_AND_PERSISTENCE.md`
- `13_OBSERVABILITY_AND_DIAGNOSTICS.md`
- `14_THREAT_MODEL_AND_FAILURE_SCENARIOS.md`
- `15_RECOMMENDED_ROADMAP.md`
