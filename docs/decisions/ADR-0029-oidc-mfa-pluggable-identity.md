# ADR-0029: OIDC/SSO Integration with Optional TOTP MFA

- **Status**: Accepted
- **Date**: 2026-04-09
- **Deciders**: Project maintainers

## Context

Taskdeck has GitHub OAuth for external authentication but lacks support for enterprise identity providers (Microsoft Entra ID, Google Workspace, generic OIDC). As the platform moves toward hosted/cloud deployment (ADR-0014), organizations need SSO integration with their existing identity infrastructure. Additionally, sensitive actions (password change, account deletion) lack a second verification factor, leaving accounts vulnerable to session hijacking.

The existing authentication architecture uses JWT tokens with claims-first identity (ADR-0002) and already supports external login linking via the `ExternalLogin` entity. The challenge is extending this to support arbitrary OIDC providers while maintaining the security guarantees of the current system.

## Decision

### OIDC Provider Integration

Adopt a **pluggable OIDC provider factory** pattern:
- OIDC providers are configured via `appsettings.json` under the `Oidc:Providers` array
- Each provider specifies Authority, ClientId, ClientSecret, Scopes, and CallbackPath
- Providers are registered as ASP.NET Core `AddOpenIdConnect` authentication schemes at startup
- Provider naming convention: `Oidc_{ProviderName}` for the authentication scheme
- The existing `ExternalLoginAsync` flow handles user creation/linking for all providers (GitHub + OIDC)
- OIDC is **disabled by default** -- no configuration means no OIDC endpoints are active

### Identity Mapping

- External identity is mapped via `ExternalLogin` entity keyed by `(Provider, ProviderUserId)`
- **No auto-linking by email** -- an OIDC login with a matching email creates a new Taskdeck user to prevent account takeover (consistent with existing GitHub OAuth security posture)
- Username collisions are resolved by appending numeric suffixes (capped at 100 attempts, then GUID fallback)
- Provider-specific prefixing (`oidc_{ProviderName}`) isolates identity namespaces across providers

### MFA Integration

Adopt **TOTP-based MFA** (RFC 6238) with these properties:
- MFA is **always optional** unless the administrator enables `MfaPolicy:RequireMfaForSensitiveActions`
- MFA setup flow: generate secret -> display QR/secret -> user confirms with TOTP code -> credential saved
- 8 single-use recovery codes generated at setup time (bcrypt-hashed at rest)
- TOTP validation uses constant-time comparison with configurable time window tolerance (default: +/- 1 step)
- `MfaCredential` entity stores per-user TOTP secrets with confirmation state
- `User.MfaEnabled` flag tracks whether MFA is active for policy decisions
- MFA credential is cascade-deleted with its user

### Authorization Code Flow

Both OIDC and GitHub OAuth share the same short-lived authorization code pattern:
- Provider callback generates a 60-second, single-use code stored in a `ConcurrentDictionary`
- Frontend exchanges the code via POST for a JWT token
- JWT is never exposed in URLs

## Alternatives Considered

- **WebAuthn/FIDO2**: Superior security but significantly higher implementation complexity and requires client-side credential storage. Deferred to a future phase -- the TOTP infrastructure can coexist with WebAuthn later.
- **Session-based MFA (cookie)**: Would require session state infrastructure that conflicts with the JWT-stateless design. Rejected.
- **Auto-link OIDC accounts by email**: Tempting for UX but creates an account takeover vector since OIDC providers may not verify email ownership with sufficient rigor. Rejected (consistent with ADR-0002 claims-first security posture).
- **Single OIDC provider hardcoded**: Simpler but forces re-deployment to change providers. The pluggable array approach supports multi-tenant scenarios.

## Consequences

- **Positive**: Organizations can authenticate via their existing identity provider; MFA reduces session hijack risk; config-gated design means zero cost for local-first users.
- **Negative**: TOTP shared secrets stored in SQLite require careful rotation procedures if the database is compromised; the `ConcurrentDictionary` auth code store does not survive process restarts.
- **Neutral**: Frontend gains OIDC login buttons and MFA setup/challenge components; existing GitHub OAuth flow is unaffected.

## References

- ADR-0002: Claims-First Identity Model
- ADR-0009: Session Token Storage
- ADR-0014: Platform Expansion -- Four Pillars
- Issue #82: SEC-07: SSO/OIDC integration with optional MFA policy
- RFC 6238: TOTP: Time-Based One-Time Password Algorithm
