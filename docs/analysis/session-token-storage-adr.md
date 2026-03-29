# ADR: Session-Token Storage Hardening

Date: 2026-03-28
Status: Accepted
Linked issue: `#156` (SEC-12)

## Context

Taskdeck currently stores JWT tokens in `localStorage` (key `taskdeck_token`) and session metadata in a separate `localStorage` entry (`taskdeck_session`). The token is attached to outbound API requests via an Axios request interceptor that reads directly from `localStorage`.

This approach has known security tradeoffs:

- **XSS exposure**: Any successful XSS attack can read the token from `localStorage` and exfiltrate it. `localStorage` is synchronous, same-origin, and accessible to all JavaScript running on the page.
- **No automatic expiry enforcement by the browser**: Token lifetime is enforced only by application code (`isTokenExpired`), not by the storage medium.
- **Persistence across tabs and sessions**: `localStorage` survives tab close and browser restart, which extends the window of exposure for a leaked token.

The alternative (HttpOnly cookies) has its own tradeoffs that must be evaluated against Taskdeck's specific constraints.

## Decision Drivers

1. **Local-first, single-origin deployment**: Taskdeck runs as a local-first tool. The API and frontend are typically on the same host (or localhost). Cross-origin cookie complexity is not a primary concern.
2. **SignalR WebSocket auth**: The current SignalR hub authenticates via `access_token` query parameter on WebSocket upgrade. This pattern works with bearer tokens but requires explicit handling for cookie-based auth.
3. **Demo mode**: Demo mode bypasses real auth entirely and does not use tokens. Any storage change must not break demo mode.
4. **Simplicity**: Taskdeck is a developer tool with a small trust boundary. The attack surface is smaller than a public SaaS application.
5. **Existing CSP baseline**: The API already emits `Content-Security-Policy` headers, but includes `'unsafe-inline'` in `script-src`, which weakens XSS protection.

## Options Evaluated

### Option A: Keep localStorage with Near-Term Mitigations (Selected for near-term)

**Approach**: Retain `localStorage` for token storage but layer defensive mitigations:

- Tighten CSP to remove `'unsafe-inline'` from `script-src` on API responses
- Add strict input validation/sanitization for persisted session data on restore
- Reduce default token lifetime from 24h to a shorter window
- Add `sessionStorage` as an opt-in alternative for users who prefer tab-scoped sessions
- Enforce token structure validation before storing

**Pros**:
- Zero backend changes required for the storage mechanism
- SignalR auth continues working without modification
- No CSRF complexity introduced
- Fastest path to measurable improvement

**Cons**:
- Token remains accessible to JavaScript (fundamental `localStorage` limitation)
- Does not eliminate XSS token theft risk, only reduces likelihood and blast radius

### Option B: HttpOnly Cookie with CSRF Protection

**Approach**: Backend sets JWT in an HttpOnly, Secure, SameSite=Strict cookie. API reads token from cookie instead of Authorization header. Anti-CSRF token required for state-changing requests.

**Pros**:
- Token is inaccessible to JavaScript (eliminates XSS token theft)
- Browser enforces Secure and SameSite attributes

**Cons**:
- Requires CSRF protection (double-submit cookie or synchronizer token)
- SignalR WebSocket auth needs reworking (cookies are sent on upgrade, but hub authorization must be adapted)
- Cross-origin development setup (Vite dev proxy) adds cookie-domain complexity
- More backend changes, higher risk of auth regressions
- Taskdeck's local-first model means the cookie security benefit is reduced (attacker with XSS on localhost already has broad access)

### Option C: Hybrid (HttpOnly Cookie + Short-Lived Access Token)

**Approach**: Long-lived refresh token in HttpOnly cookie, short-lived access token in memory only (not persisted). Access token refreshed transparently via cookie-authenticated endpoint.

**Pros**:
- Best security posture (token never in storage, refresh token not accessible to JS)
- Short access token lifetime limits blast radius

**Cons**:
- Most complex implementation
- Requires refresh endpoint, token rotation logic, and retry-on-401 interceptor
- SignalR reconnection must handle token refresh
- Significant backend and frontend changes

## Decision

**Near-term (now)**: Implement Option A mitigations immediately. These provide measurable security improvement with minimal risk and no breaking changes.

**Future (if needed)**: Option C is the recommended migration target if Taskdeck moves to a multi-user hosted deployment model where XSS token theft becomes a higher-severity risk. This would be tracked as a phased migration in separate issues.

**Rationale for deferring cookie migration**: Taskdeck is a local-first developer tool. The primary threat model is a compromised dependency or XSS via malicious board content. The near-term mitigations (CSP hardening, token lifetime reduction, input validation) address the highest-likelihood attack vectors. The incremental security gain from cookie-based storage does not justify the complexity and regression risk for the current deployment model.

## Near-Term Mitigations (Implemented)

### 1. CSP Hardening — Remove `'unsafe-inline'` from `script-src`

The API's default CSP included `script-src 'self' 'unsafe-inline'`. The `'unsafe-inline'` directive substantially weakens XSS protection because it allows injected inline scripts to execute. Since the Taskdeck API serves JSON responses (not HTML pages with inline scripts), this directive is unnecessary for API responses.

**Change**: Default `script-src` is now `'self'` only (no `'unsafe-inline'`). The `'unsafe-inline'` remains in `style-src` because some CSS-in-JS patterns and Swagger UI require it.

### 2. Session Data Sanitization on Restore

The `restoreSession()` function in `sessionStore.ts` now performs stricter validation of persisted session data:

- Token format validation (must be a valid three-part JWT structure)
- Session metadata field length limits (prevents storage of maliciously large values)
- Type validation for all fields before use

### 3. Token Structure Validation Before Storage

The `setSession()` function now validates that the received token has valid JWT structure before persisting to `localStorage`. Malformed tokens are rejected.

### 4. Storage Abstraction for Session Tokens

A `tokenStorage` utility module provides a thin abstraction over the storage mechanism, enabling future migration to alternative storage (sessionStorage, cookie, or in-memory) without modifying consuming code. The abstraction includes:

- `getToken()` / `setToken()` / `removeToken()` with consistent behavior
- `getSession()` / `setSession()` / `removeSession()` for session metadata
- Centralized storage key management (eliminates duplicated key constants)

## Migration Path (If Cookie Model Is Selected)

If Taskdeck moves to a hosted multi-user model, the following phased migration is recommended:

### Phase 1: Backend Cookie Endpoint
- Add `/auth/login` variant that sets HttpOnly cookie instead of returning token in body
- Implement CSRF double-submit cookie pattern
- Add `/auth/refresh` endpoint for token rotation
- Keep existing bearer token flow working in parallel

### Phase 2: Frontend Opt-In
- Update `tokenStorage` abstraction to use cookie-aware mode
- Modify Axios interceptor to omit Authorization header when in cookie mode
- Add CSRF token handling to state-changing requests
- Update SignalR connection to work with cookie auth

### Phase 3: Deprecate Bearer Token Flow
- Mark localStorage token flow as deprecated
- Add migration prompt for existing sessions
- Remove bearer token code paths after transition period

## Security Regression Checks

- Existing `SecurityHeadersApiTests` verify CSP header presence on API responses
- Session restore validates token structure and session data shape
- Token expiry check continues to work identically
- Demo mode is unaffected (does not use token storage)

## References

- OWASP Session Management Cheat Sheet
- OWASP XSS Prevention Cheat Sheet
- `docs/security/SECURITY_OWASP_BASELINE.md` — existing security headers baseline
- `backend/src/Taskdeck.Api/Middleware/SecurityHeadersMiddleware.cs` — security headers implementation
- `frontend/taskdeck-web/src/store/sessionStore.ts` — session management
- `frontend/taskdeck-web/src/api/http.ts` — HTTP client with auth interceptor
