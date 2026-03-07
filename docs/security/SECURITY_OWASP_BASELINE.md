# OWASP Baseline Hardening

Last Updated: 2026-02-26
Owner: Taskdeck maintainers
Linked issue: `#80` (SEC-05)

## Scope

This document records the baseline OWASP-oriented hardening controls now enforced in the API runtime.
It is intentionally narrow and focuses on headers, CSRF posture, and explicit follow-up gaps.

Related active security docs:

- `docs/security/SECURITY_LOGGING_REDACTION.md` for capture/auth-sensitive logging and telemetry redaction policy (`#212`).
- `docs/security/SECURITY_DEPENDENCY_VULNERABILITY_POLICY.md` for dependency scan cadence, severity handling, and exception policy (`#106`).

## Enforced Security Headers

API middleware now emits these headers for API responses:

- `X-Frame-Options: DENY`
- `X-Content-Type-Options: nosniff`
- `Referrer-Policy: no-referrer`
- `Content-Security-Policy` with a deny-by-default baseline:
  - `default-src 'none'`
  - `frame-ancestors 'none'`
  - `base-uri 'self'`
  - `form-action 'self'`
  - `connect-src 'self'`
  - `img-src 'self'`
  - `style-src 'self' 'unsafe-inline'`
  - `script-src 'self' 'unsafe-inline'`

Environment-aware behavior:

- `Strict-Transport-Security` is enabled for HTTPS requests and disabled by default in development.
- Swagger paths are excluded from CSP by default to avoid local developer tooling breakage.

Configuration section:

- `backend/src/Taskdeck.Api/appsettings.json` -> `SecurityHeaders`

## CSRF Posture

Current authentication mode is JWT bearer token via `Authorization` header.
Taskdeck does not use cookie-authenticated browser sessions for protected API routes.

Baseline position:

- CSRF risk is reduced because browsers do not auto-attach bearer tokens.
- If future flows introduce cookie-based auth, anti-forgery protection becomes required for state-changing routes before rollout.

## XSS and Input Safety Notes

- CSP is now emitted by default for API routes.
- API responses remain JSON-first with stable error contracts.
- Input validation remains server-side in application services and controllers.

## OWASP Baseline Checklist

- [x] Security headers applied in API middleware.
- [x] Environment-aware HSTS behavior documented and test-covered.
- [x] CSRF posture documented for current bearer-token model.
- [x] API integration tests verify header presence on success and auth-failure responses.

## Follow-up Gaps (Tracked)

No additional net-new high-severity gaps were identified in this baseline pass beyond already-seeded security work:

- `#81` API rate limiting and abuse protection
- `#82` SSO/OIDC + optional MFA
- `#83` data portability/deletion workflow
- `#106` dependency vulnerability management policy
- `#110` secrets/configuration management baseline

## Verification

- `dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release --filter "FullyQualifiedName~SecurityHeadersApiTests"`
- `dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release`
