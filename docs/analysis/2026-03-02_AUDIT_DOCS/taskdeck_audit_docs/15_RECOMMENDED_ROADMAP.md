# Recommended Roadmap (Prioritized)

This is a suggested sequence of changes that maximizes risk reduction per unit effort.

Effort scale:
- **S** = small (few hours / localized changes)
- **M** = medium (multi-file refactor, test updates)
- **L** = large (architecture shifts, data migrations)

## P0 — Blockers before serious multi-user / public deployment

### 1) Enforce password policy (S/M)
**Goal:** prevent empty/weak passwords.

Actions:
- Add server-side validation in:
  - `AuthenticationService.RegisterAsync`
  - `AuthenticationService.ChangePasswordAsync`
  - `UserService.CreateUserAsync`
- Decide a baseline policy:
  - min length 10 (or 12)
  - reject whitespace-only
  - optional: reject common passwords list
- Add API integration tests:
  - empty password rejected
  - too short rejected

### 2) Remove client control over roles (S)
**Goal:** stop privilege escalation.

Actions:
- In registration:
  - ignore `CreateUserDto.DefaultRole` and force `Editor`
- In `/api/users` creation:
  - either remove endpoint, or require admin/owner privileges
- Add tests:
  - registering with Owner role still yields Editor
  - non-admin cannot create privileged users

### 3) Lock down LLM queue endpoints (M)
**Goal:** prevent cross-user leakage and queue manipulation.

Actions (pick a model):
- **User-scoped model:**
  - restrict `GetByStatus`, `Stats`, `ProcessNext` to `currentUserId`
- **Ops model:**
  - move endpoints under `/api/ops/llm-queue/*`
  - require admin/owner role, or operator token, or IP allowlist

Add tests:
- cross-user access returns 403
- queue processing only affects permitted scope

### 4) Update/remove `Microsoft.AspNetCore.Http` 2.3.9 (S)
**Goal:** fix dependency risk.

Actions:
- Remove package reference if not required.
- If required, update to a compatible 8.x package.
- Run dependency vulnerability scan in CI.

## P1 — Hardening and operational safety

### 5) Tighten CSP and reconsider auth token storage (M/L)
**Goal:** reduce XSS impact.

Options:
- keep bearer tokens but remove `'unsafe-inline'` via CSP nonces/hashes
- or switch to httpOnly cookies + CSRF protections (bigger change)

### 6) Expand rate limiting coverage (S/M)
**Goal:** protect expensive endpoints.

Candidates:
- import/export endpoints
- webhook subscription creation/rotation
- logs endpoints (if potentially heavy)

### 7) Add retention policies (M)
**Goal:** prevent DB bloat.

- logs retention
- webhook delivery retention
- completed queue item retention

## P2 — Performance and maintainability improvements

### 8) Add paging to unbounded list endpoints (M)
- logs list endpoints
- queue status endpoints
- audit endpoints

### 9) Replace list-based health computations with COUNT queries (S)
**Goal:** avoid health checks becoming a load source.

### 10) Add analyzers and formatting gates (S)
- `global.json` for SDK pinning
- `dotnet format` in CI
- security analyzers

## P3 — If you want “real” elasticity

### 11) Split workers into separate host (L)
- remove background workers from API container
- create worker service container

### 12) Move to Postgres + Redis + queue (L)
- enables scale-out, distributed rate limiting, signalR backplane, etc.

## Suggested sequencing

1. Password policy + role control + queue lockdown (P0)
2. Dependency cleanup + CSP hardening (P1)
3. Retention + paging (P1/P2)
4. Worker split + DB migration only if product goals demand it (P3)
