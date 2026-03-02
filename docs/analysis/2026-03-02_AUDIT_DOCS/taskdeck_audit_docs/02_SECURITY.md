# Security Assessment

Score: **6 / 10**  
(Strong baseline controls exist, but there are several high-impact gaps that matter in any multi-user or internet-exposed deployment.)

## 1) Threat model (what you’re defending)

### Assets worth protecting
- **User accounts** (credentials, tokens, roles)
- **Board/task data** (often business-sensitive)
- **LLM provider keys** (direct cost exposure)
- **Outbound webhook secrets** (integrity of external integrations)
- **Audit/log data** (can contain PII and operational details)
- **Database export/import** (complete compromise if mishandled)

### Likely attacker profiles
- Anonymous internet user (if exposed)
- Authenticated but malicious user (multi-user deployment)
- Insider with limited role (tries escalation)
- Compromised browser (XSS / malicious extension)
- Network attacker (if TLS misconfigured)
- Supply chain attacker (dependency compromise)

### Primary attack surfaces in this repo
- Auth endpoints (`/api/auth/*`)
- Board endpoints (`/api/boards/*`)
- Capture endpoints (LLM-queue-backed)
- Chat endpoints (LLM calls, streaming)
- Ops endpoints (`/api/ops/*`)
- Outbound webhook configuration and delivery
- Import/export endpoints

## 2) Authentication (AuthN)

### What’s implemented well
- JWT bearer auth is implemented with:
  - strict lifetime validation (ClockSkew = 0)
  - a minimum secret length check (>= 32 chars)
  - a consistent JSON error contract on challenge
- Token validation re-checks user existence and `IsActive`.

**Evidence:**  
- `backend/src/Taskdeck.Api/Program.cs` JWT setup  
- `backend/src/Taskdeck.Application/Services/AuthenticationService.cs`

### Critical gaps / risks

#### A) Password policy is effectively “none”
- Registration and change-password do **not** validate password:
  - not empty
  - not minimum length
  - not “common password”
  - not similar to username/email
- BCrypt can hash empty strings, so **empty-password accounts are possible**.

**Evidence:**
- `AuthenticationService.RegisterAsync(CreateUserDto dto)` hashes `dto.Password` without validation.
- `AuthenticationService.ChangePasswordAsync(...)` hashes `newPassword` without validation.
- Same issue exists in `UserService.CreateUserAsync()`.

**Impact:** credential stuffing becomes easier, and users can create trivially weak accounts.

**Fix:** enforce server-side password constraints (see Recommendations).

#### B) No login lockout / throttling beyond IP rate limiting
Rate limiting on auth endpoints exists, but:
- it’s per-IP (not per-username)
- it’s not a lockout on repeated failures
- it’s in-memory per instance

This is fine for local-first, but weaker than expected for public internet deployments.

## 3) Authorization (AuthZ)

### What’s implemented well
- Board access is modeled explicitly (`BoardAccess`) with roles (Owner/Admin/Editor/Viewer).
- Services typically enforce permissions with `AuthorizationService` before mutating board state.

**Evidence:**
- `Taskdeck.Domain/Entities/BoardAccess.cs`
- `Taskdeck.Application/Services/AuthorizationService.cs`
- Many controllers call `EnsureBoardPermissionAsync(...)` before service calls.

### Critical gaps / risks

#### A) LLM queue endpoints look globally scoped and not role-gated
`LlmQueueController` exposes:
- `GET /api/llm-queue/status/{status}` → returns queue requests by status
- `GET /api/llm-queue/stats` → returns global queue stats
- `POST /api/llm-queue/process-next` → claims next pending request

These endpoints:
- are `[Authorize]`
- but do **not** scope to the current user
- do **not** require an admin/operator role
- can leak cross-user queue items and allow a user to interfere with queue processing

**Evidence:**
- `backend/src/Taskdeck.Api/Controllers/LlmQueueController.cs`
- `backend/src/Taskdeck.Application/Services/LlmQueueService.cs` (`ProcessNextRequestAsync` selects next pending request without user scope)

**Impact:**
- information disclosure (other users’ queued content, board ids, etc.)
- operational abuse (queue starvation, forced processing state, potential cost/latency issues)

**Fix:** treat queue operations as operator-only or user-scoped (see Recommendations).

#### B) Role escalation: client can choose `DefaultRole`
`CreateUserDto` contains `DefaultRole`, and both registration and user creation accept it.

If `DefaultRole` is used to gate privileged features (it is for Ops CLI), then:
- a user can register as `Owner`/`Admin` and unlock privileged endpoints.

**Evidence:**
- `backend/src/Taskdeck.Application/DTOs/UserDtos.cs` (CreateUserDto includes DefaultRole)
- `AuthenticationService.RegisterAsync(...)` constructs `new User(..., dto.DefaultRole)`
- `UserService.CreateUserAsync(...)` constructs `new User(..., dto.DefaultRole)`
- Ops CLI enforces access via `user.DefaultRole`

**Impact:** privilege escalation / bypass of “ops” restrictions.

**Fix:** ignore client-supplied role in public registration and only allow role changes through a privileged path.

#### C) “Ownerless board bootstrap” can be a takeover vector
`BoardAccessService.EnsureCanManageBoardAccessAsync` assigns ownership to the first manager if `OwnerId` is null and there are no access entries.

This can be correct for bootstrapping imported boards, but it is also:
- a race condition
- a potential unauthorized ownership claim if an attacker can reference an ownerless board id

**Evidence:**
- `backend/src/Taskdeck.Application/Services/BoardAccessService.cs`

**Fix:** lock this behind an explicit migration/admin-only workflow, or require proof-of-creation.

## 4) Abuse protection / rate limiting

### What’s implemented well
- Rate limiting uses explicit policies:
  - `AuthPerIp`
  - `HotPathPerUser`
  - `CaptureWritePerUser`
- Policies are explicitly documented in `docs/RATE_LIMITING_POLICY.md`.
- Forwarded header trust is opt-in: the code does **not** blindly trust `X-Forwarded-For`.
- Tests exist verifying forwarded-header behavior and rate limiting.

**Evidence:**
- `backend/src/Taskdeck.Api/Program.cs` (AddRateLimiter, forwarded headers)
- `backend/tests/Taskdeck.Api.Tests/RateLimitingApiTests.cs`
- `docs/RATE_LIMITING_POLICY.md`

### Gaps / risks
- Rate limiting is in-memory per instance → not horizontally scalable.
- Coverage is selective (only a few endpoints have `[EnableRateLimiting]`).
  - Endpoints with potential heavy load (imports, exports, webhook configuration) are not explicitly limited.

**Fix:** add targeted limits for expensive endpoints, and document “why not limited” where you intentionally skip.

## 5) SSRF / outbound network controls

This repo is **better than average** here.

- Outbound webhooks use a `ConnectCallback` to block:
  - localhost
  - private IP ranges
  - dynamic DNS services that encode IPs
- It resolves DNS and blocks if any A/AAAA is private/loopback.

**Evidence:**
- `Taskdeck.Application/Services/OutboundWebhookEndpointGuard.cs`
- `Taskdeck.Api/Workers/OutboundWebhookConnectCallback.cs`

**Residual risks to note**
- DNS rebinding remains a general risk; the connect callback helps, but consider:
  - pinning to the resolved IP for the duration of a delivery
  - caching TTL-aware results
- The dynamic DNS allow/block list is necessarily incomplete.

## 6) Browser security & frontend concerns

### What’s good
- Security headers middleware includes CSP, HSTS, nosniff, frame deny, etc.
- In production, swagger is disabled and is excluded from CSP when enabled.

### Risks
- CSP includes `'unsafe-inline'` for scripts and styles.
- JWT is stored in `localStorage`.
- Combined, the impact of any XSS is: **instant token theft and account takeover**.

**Evidence:**
- `backend/src/Taskdeck.Api/appsettings.json` CSP values
- `frontend/taskdeck-web/src/store/sessionStore.ts` uses localStorage

**Fix options (choose one based on product goals):**
1. Keep SPA + bearer tokens, but harden CSP to remove `unsafe-inline` via nonces/hashes.
2. Move to httpOnly cookies + CSRF protection (harder but stronger).
3. If local-first only, explicitly state the threat model and accept localStorage risk.

## 7) Dependency hygiene / supply chain

### Red flag
- `Microsoft.AspNetCore.Http` package reference at **2.3.9** in `Taskdeck.Infrastructure.csproj`.

This is:
- very old relative to .NET 8
- a likely vulnerability and compatibility risk

**Fix:** remove it or upgrade to a compatible 8.x package (or use framework references where possible).

Frontend:
- Node engine requirement is >=24; runtime environment mismatch will be common.
- `ws` is vendored as a `.tgz` — this can be a deliberate mitigation or a maintenance hazard. Document why.

## Security recommendations (prioritized)

### P0
1. **Enforce server-side password policy**
   - Reject empty passwords
   - Minimum length (>= 10 is a decent baseline)
   - Consider zxcvbn-like strength or deny-list for common passwords
   - Add tests for:
     - empty password rejected
     - too-short password rejected
     - change-password enforces same rules

2. **Disable client-controlled roles on registration**
   - Force `DefaultRole = Editor` in `RegisterAsync`
   - For `/api/users` creation, require admin/owner (or remove route)
   - Add tests:
     - registering with `DefaultRole=Owner` results in `Editor`
     - non-admin cannot create admin users

3. **Lock down LLM queue endpoints**
   - Option A (simple): scope all queue reads and processing to `currentUserId`
   - Option B (ops model): require admin role or an operator token
   - Add tests for cross-user access:
     - user A cannot see or process user B queue items

### P1
4. Upgrade/remove `Microsoft.AspNetCore.Http` 2.3.9.
5. Tighten CSP and/or reconsider token storage (localStorage).
6. Add rate limiting to other expensive endpoints (imports, exports, webhook creation).

### P2
7. Add account lockout or per-username throttling for auth endpoints (optional if local-only).
8. Add structured audit logs around security-sensitive operations (role changes, webhook secret rotation, DB import/export).

## What you should monitor in production (if deployed)
- 401/403/429 rates per endpoint
- failed login attempts per username/IP
- outbound webhook blocked attempts (SSRF guard rejections)
- LLM token usage per user and per board
- queue backlog depth + stuck work recovery counts
