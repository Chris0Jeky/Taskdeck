# ADR-0009: Session Token Storage — localStorage with Mitigations

- **Status**: Accepted (near-term; to be superseded for hosted deployment)
- **Date**: 2026-03-28
- **Deciders**: Project maintainers

## Context

JWT tokens stored in `localStorage` are vulnerable to XSS attacks. However, Taskdeck is currently a local-first application with a small trust boundary (single machine, single user). Moving to HttpOnly cookies adds CSRF complexity and changes the auth flow significantly.

## Decision

Keep `localStorage` for the local-first phase with layered mitigations:

1. Remove `'unsafe-inline'` from CSP `script-src` (reduces XSS blast radius).
2. Token validation on every API call.
3. Input sanitization across all user-facing surfaces.
4. Introduce `tokenStorage` abstraction for future pluggability.

Defer HttpOnly + refresh-token migration to the hosted cloud phase (v0.2.0+), where the trust boundary expands to multiple users over the network.

## Alternatives Considered

- **HttpOnly cookies immediately**: Strongest security but adds CSRF protection complexity, changes all API calls to cookie-based, and is premature for local-only deployment.
- **In-memory only (no persistence)**: Secure but forces re-login on every page refresh; unusable for a productivity tool.
- **IndexedDB with encryption**: More obscure than localStorage but still XSS-vulnerable; encryption key management adds complexity without meaningful security gain.

## Consequences

- **Positive**: Simple implementation; familiar pattern; no CSRF concerns; `tokenStorage` abstraction enables clean migration later.
- **Negative**: XSS vulnerability window exists (mitigated by CSP); must be revisited before hosted deployment.
- **Neutral**: The decision is explicitly time-boxed to the local-first phase.

## References

- `docs/analysis/session-token-storage-adr.md` — full analysis
- SEC-12 CSP hardening
- Platform expansion: `#531` (v0.2.0 triggers reassessment)
