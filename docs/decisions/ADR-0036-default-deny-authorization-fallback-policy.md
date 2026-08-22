# ADR-0036: Default-Deny Authorization via a Global FallbackPolicy

- Status: Accepted
- Date: 2026-06-05
- Deciders: Repository maintainers
- Related: #1132 (AC4), ADR-0002 (Claims-First Identity), #1110/#1116 (per-action authz architecture guards), #1971 (machine-facing 404 fallbacks, amended 2026-08-22)

## Context

Every API controller already declares explicit `[Authorize]` or `[AllowAnonymous]`, enforced by the
`ApiControllerBoundaryTests` architecture guard. But that guard only covers MVC controllers. A future
minimal-API endpoint, a `MapX` endpoint, or a controller action that forgot its attribute would be
**open by default** — there was no global safety net requiring authentication.

#1132 AC4 calls for a global `AuthorizationOptions.FallbackPolicy = RequireAuthenticatedUser`.
This is a security-posture and cross-cutting-convention change (per `CLAUDE.md`, that warrants an ADR),
and it interacts non-trivially with the API-key-authenticated MCP endpoint.

## Decision

Set a global `FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()`
(Program.cs `AddAuthorizationBuilder`). Any endpoint **without** explicit authorization metadata now
requires an authenticated user — default-deny defense-in-depth beyond the per-controller attributes.

Endpoints that must remain anonymous opt out explicitly:

- **login / register / health / OAuth callback** controllers already carry `[AllowAnonymous]`.
- **SPA fallback** (`MapFallbackToFile`) → `.AllowAnonymous()` so the app shell loads to reach the login page.
- **`/mcp`** is authenticated by `ApiKeyMiddleware`, which now sets an **authenticated `ClaimsPrincipal`**
  (NameIdentifier = userId, `AuthenticationType` = `ApiKey`) for a valid key. A valid API key *is*
  authentication, so the fallback policy is satisfied; invalid/missing/revoked keys are still 401'd
  before routing. The MCP SDK endpoint does **not** honor an `.AllowAnonymous()` endpoint convention,
  so the principal — not endpoint metadata — is the opt-in. This is load-bearing: editing
  `ApiKeyMiddleware` to drop the principal would break MCP under the fallback policy.
- **Machine-facing 404 fallbacks** (amendment, 2026-08-22, #1971) → `.AllowAnonymous()`. Four
  `MapFallback` catch-alls — one each for the `/api`, `/hubs`, `/health`, and `/mcp` prefixes, scoped to
  `GET`/`HEAD` — answer an unmatched path under those prefixes with the standard `ApiErrorResponse` 404
  contract (`errorCode`/`message`, `application/json`) and **terminate there**: a literal first segment
  outranks the SPA catch-all in route precedence, so such a path never reaches the `MapFallbackToFile`
  opt-out above and can no longer be answered with `200 OK` + `index.html`. Anonymous is deliberate and
  is the same judgement as the SPA opt-out: an unknown path has no resource to protect, and under this
  policy the alternative is a 401 that re-hides "this endpoint does not exist" from exactly the
  unauthenticated scripts and probes the fix serves. The accepted cost is that route *existence* becomes
  anonymously distinguishable (404 vs 401) — information the published OpenAPI document already carries,
  and the fallbacks themselves are excluded from that document. `/mcp` is additionally gated by
  `ApiKeyMiddleware` before routing, so its 404 is reachable only with a valid key; the `AllowAnonymous`
  on it opens no unauthenticated window.
- The standalone `--mcp` HTTP host has no `UseAuthorization` and is unaffected.

`TokenValidationMiddleware` short-circuits for `AuthenticationType == ApiKey` principals (they carry no
JWT `iat`, and `ApiKeyMiddleware` already validated freshness/active state) to avoid a redundant
per-request user load.

## Alternatives Considered

- **Rely only on the per-controller architecture guard.** Rejected: it does not cover non-controller
  endpoints, leaving a default-open gap for future minimal APIs / mapped endpoints.
- **`.AllowAnonymous()` on the MCP endpoint instead of a principal.** Rejected: verified that the MCP
  SDK endpoint ignores the `.AllowAnonymous()` convention (the SPA `MapFallbackToFile` opt-out works,
  the MCP one did not), so a valid-key request hit the fallback and 401'd. The principal approach also
  models a valid API key as genuine authentication.

## Consequences

- A future endpoint that forgets explicit authorization metadata is denied by default rather than open.
- `ApiKeyMiddleware`'s principal is now load-bearing for MCP under the fallback policy (documented here
  and in code comments); a regression test asserts the fallback policy is registered.
- A minor extra consideration for any future non-JWT authentication path: it must either set an
  authenticated principal or `[AllowAnonymous]` to coexist with the fallback policy.

## References

- #1132 — Security gate + config hardening (AC4)
- ADR-0002 — Claims-First Identity Model
- ADR-0035 — Required Security-Scan Merge Gate (sibling AC of #1132)
