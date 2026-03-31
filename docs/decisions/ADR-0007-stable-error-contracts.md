# ADR-0007: Stable Error Contracts (ApiErrorResponse)

- **Status**: Accepted
- **Date**: 2026-01 (cross-cutting refinement)
- **Deciders**: Project maintainers

## Context

API consumers (frontend, CLI, external integrations) need predictable error shapes. Inconsistent payloads across controllers — some returning `ProblemDetails`, some plain strings, some nothing — made error handling fragile and logging unreliable. Middleware-level responses (JWT challenge, forbidden) also needed standardization.

## Decision

Mandate a single error response contract across all API surfaces:

```json
{
  "errorCode": "string",
  "message": "non-empty string"
}
```

- All domain/application errors map to `ApiErrorResponse` via `ResultExtensions`.
- JWT challenge (401) and forbidden (403) handlers emit `ApiErrorResponse` payloads.
- Global unhandled-exception middleware returns `ApiErrorResponse` with `UnexpectedError` code.
- Cross-user existence policy: 403 for authenticated-but-unauthorized, 404 for truly missing resources.

Formalized as Golden Principle GP-03.

## Alternatives Considered

- **RFC 7807 ProblemDetails**: Standard but more complex; `type` URI field is often meaningless for internal APIs; `ApiErrorResponse` is simpler and sufficient.
- **No standard (ad-hoc)**: Current state before the decision; caused frontend error-handling inconsistencies.

## Consequences

- **Positive**: Frontend can rely on consistent `errorCode` + `message` shape; logging is uniform; API integration tests can assert contract shape mechanically.
- **Negative**: Every new controller/endpoint must follow the convention (but architecture tests enforce this).
- **Neutral**: Correlation ID (`X-Request-Id`) propagates through error responses for traceability.

## References

- `docs/GOLDEN_PRINCIPLES.md` — GP-03 Stable Error Contracts
- API-06 in `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/STATUS.md` — cross-cutting API consistency section
