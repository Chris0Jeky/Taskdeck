# ADR-0021: JWT Invalidation via User-Active Middleware

- **Status**: Accepted
- **Date**: 2026-04-02
- **Deciders**: Project maintainers

## Context

When a user account is deleted (anonymized) or deactivated, any JWT tokens previously issued to that user remain cryptographically valid until their natural expiration (default: 24 hours). During this window an attacker who obtained a token -- or the user themselves -- could continue making authenticated requests against a deleted account.

Issue #671 (SEC-08 follow-up) required that active JWTs be invalidated immediately after account deletion.

## Decision

Implement a lightweight ASP.NET Core middleware (`TokenValidationMiddleware`) that runs on every authenticated request, after `UseAuthentication()` and before `UseAuthorization()`. The middleware:

1. Extracts the user ID from the JWT `sub` claim.
2. Loads the `User` entity from the database (SQLite).
3. Rejects the request with `401 Unauthorized` if:
   - The user record is missing or `IsActive == false`.
   - The user has a `TokenInvalidatedAt` timestamp and the JWT's `iat` (issued-at) claim is earlier than that timestamp.
4. Otherwise, passes the request to the next middleware.

A new nullable `TokenInvalidatedAt` field on the `User` entity is set by `AccountDeletionService` during account deletion, immediately before `Deactivate()`. This provides a precise cutoff: tokens issued before the deletion are rejected, while any token issued after reactivation (if applicable) would be accepted.

The JWT token generation in `AuthenticationService` now includes an explicit `iat` claim to support this comparison.

## Alternatives Considered

- **Token blocklist table**: A separate table recording revoked token JTIs (JWT IDs). More precise per-token revocation, but adds a table that grows with every login, requires cleanup jobs, and is over-engineered for a local-first SQLite app. Rejected due to unnecessary complexity.

- **Short-lived tokens + refresh tokens**: Reduce JWT lifetime to minutes and use refresh tokens for session continuity. The refresh token can be revoked instantly. This is the industry standard for multi-tenant cloud apps but adds significant complexity (refresh endpoint, token rotation, secure refresh token storage). Deferred to the hosted cloud phase (v0.2.0+), consistent with ADR-0009.

- **JWT event handler (OnTokenValidated)**: Wire the check into `JwtBearerEvents.OnTokenValidated` instead of a separate middleware. Functionally equivalent but couples the check to the JWT bearer handler, making it harder to test independently and less visible in the pipeline. Rejected for testability and clarity.

## Consequences

- **Positive**: Deleted/deactivated users are immediately locked out. No window of vulnerability between account deletion and token expiry. The approach is simple, testable, and leverages the existing `User` entity without new tables.
- **Negative**: Every authenticated request incurs a database read for the user record. For a local-first SQLite app this is acceptable (sub-millisecond on local disk). For a hosted multi-tenant deployment this should be revisited with caching or a switch to short-lived tokens.
- **Neutral**: The `ActivateUser_ShouldReturnNoContent_AfterSelfDeactivation` integration test was updated to expect `401` -- deactivated users can no longer self-reactivate with their existing token, which is the correct security behavior.

## References

- Issue: #671 (SEC-08 follow-up: JWT token invalidation after account deletion)
- ADR-0009: Session Token Storage (defers HttpOnly cookies to cloud phase)
- `backend/src/Taskdeck.Api/Middleware/TokenValidationMiddleware.cs`
- `backend/src/Taskdeck.Domain/Entities/User.cs` (`TokenInvalidatedAt`, `InvalidateTokens()`)
