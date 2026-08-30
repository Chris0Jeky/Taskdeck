# ADR-0064: Machine-Facing Paths Are Exact Lowercase; Non-Canonical Spellings Are 404 at Every Layer

- **Status**: Accepted (maintainer ruling 2026-08-30, v0.3 RC deck reply q-10 A, decision map
  `map:v1:bec0a8dd8ba5839bc4816da9e46371e89fc866c5bc936add3d9aceb414dd9138`, recorded on `#1992`;
  not inferred from the implementation or its tests)
- **Date**: 2026-08-30
- **Deciders**: Chris0Jeky (maintainer)
- **Related**: `#1992`, ADR-0059 (the 404/405 half of the same contract), `#1971`, `#2029`, `#2030`,
  `#2065`, `#2079`

## Context

Taskdeck answers four request-path prefixes with machine contracts rather than the app shell:
`/api`, `/hubs`, `/health`, `/mcp`. Three independent layers decide whether a given URL is one of
them, and they did not agree on the spelling:

| Layer | Matcher | Case | Percent-encoding |
| --- | --- | --- | --- |
| nginx (`deploy/nginx/reverse-proxy.conf` and the Terraform-rendered twin) | `location ~ ^/api(?:/\|$)` | **sensitive** | matches on the **decoded** `$uri`, so `%2F` is a separator |
| Service worker (`navigateFallbackDenylist`) | JS regex over `pathname + search` | **sensitive** | matches the **raw** pathname, so `%2F` is literal text |
| ASP.NET Core (`PipelineConfiguration.NonSpaPathPrefixes`) | `PathString.StartsWithSegments` | **insensitive** | Kestrel decodes every escape **except** `%2F`, so `%2F` is literal text |

Spellings therefore had contradictory answers depending on which layer saw them first. The two that
were measured on `main` and recorded on `#1992` when the question was put to the maintainer:

| URL | nginx | Service worker | API |
| --- | --- | --- | --- |
| `/API/boards` | no match → SPA container → `200` + `index.html` | no match → app shell served from precache | matches the real board controller → `401`/`200` |
| `/mcp%2Fmessages` | decoded to `/mcp/messages` → proxied to the API | denied (the `%2[fF]` branch added in `#2079`) | one opaque segment `mcp%2Fmessages` → not machine-facing → bypasses `ApiKeyMiddleware`, the machine fallbacks and the 404/405 contract → SPA shell |

The `/mcp%2Fmessages` row is the sharper one: a path the proxy classifies as MCP surface reached the
app as a path the API classifies as SPA surface, past the middleware that authenticates MCP.

Two dispositions were put to the maintainer as `#1992`'s open policy question:

- **A — fail closed.** The prefixes are exact lowercase; every other spelling is `404` everywhere.
- **B — normalize.** Lowercase-fold and decode in all three layers so the variants resolve to the
  canonical path.

## Decision

**A.** A machine-facing prefix is the exact lowercase literal at a segment boundary. A spelling in
one of the four enumerated variant classes below — a case variant, a prefix-boundary encoded slash, a
leading duplicate/encoded separator, or percent-encoded prefix letters — is neither machine surface
nor a client-side route: it answers `404` at nginx, is kept off the service worker's navigation fallback so that `404` reaches
the user, and answers the JSON `404` error contract at the API. Nothing is rewritten,
lowercase-folded, or decoded into the canonical form.

The four classes are the ones where a layer's own normalization creates the disagreement, and the
decision covers exactly them — it is not a general claim that every conceivable alias is already
handled. A newly discovered class (a further normalization in some layer, a proxy that rewrites) is
a new slice against this ADR, not a silent gap in it.

Concretely, and pinned end to end by `scripts/deploy/Test-TaskdeckReverseProxyConfig.ps1` (run by
required container CI before compose validation), `SpaFallbackRoutingApiTests` plus
`MachinePathCanonicalFormTests`, and `PwaMachinePathDenylist.spec.ts`:

1. **Case variants** — `/API`, `/Api/boards`, `/Mcp`, `/HEALTH/live`. nginx declares the four exact
   lowercase locations first (they are case-sensitive, so they still match the real machine surface)
   and a case-*insensitive* `location ~* ^/(?:api|hubs|health|mcp)(?:/|$) { return 404; }` after
   them, before the SPA catch-all. The API rejects them in a middleware that runs ahead of static
   files, MCP telemetry and every authentication middleware, so a variant of a real route never
   reaches the route and never answers `401`. The service-worker denylist regexes carry the `i` flag.
2. **Prefix-boundary encoded slashes** — `/mcp%2Fmessages`, `/api%2Fboards`, `/mcp%2F`. nginx cannot
   see these once `$uri` is decoded, so the check is a server-level guard on the raw `$request_uri`
   (`if ($request_uri ~* "^/(?:api|hubs|health|mcp)%2f") { return 404; }`) evaluated before location
   selection. The API applies the same rule to `Request.Path`, where Kestrel has left `%2F` intact.
   The denylist already covered these (`#2079`).
3. **Leading duplicate or encoded separators** — `//api/boards`, `/%2fapi/boards`, `//API/x`. nginx
   percent-decodes and then applies `merge_slashes` (on by default), so the leading run collapses and
   these select the machine location; `proxy_pass` carries no URI, so the API receives the client's
   raw form, keeps the empty first segment, and reads it as an SPA path — `#1971`'s shape on a URL
   the proxy had already classified as machine surface. Both alternations therefore live in the same
   raw-`$request_uri` guard, with two or more leading separators as the discriminator (exactly one
   plain slash is canonical). The API's guard consumes the same leading run and requires the same
   segment boundary after the prefix, and the denylist regexes open with `(?:\/|%2[fF])+`. The
   boundary is what keeps `//apidocs` a client-side route in all three layers.
4. **Percent-encoded prefix letters** — `/%61pi/boards`, `/%6Dcp/messages`. These decode to the
   canonical path in *both* nginx and Kestrel, so by the time either matches, the encoding is gone
   and the request is on the real controller; `proxy_pass` carries no URI, so the raw form is what
   the API receives. The rule needs the raw request *and* the decoded path at once, which one nginx
   `if` cannot express, so the proxy states it as a conjunction of three `map`s: a percent escape
   anywhere in the **first raw path segment** (`~^/[^/?]*%`) **and** a decoded path that is
   machine-facing. The API applies the same conjunction against `IHttpRequestFeature.RawTarget`. The
   service worker cannot decode, so its denylist spells each prefix letter as itself or its escape
   (`(?:a|%61|%41)(?:p|%70|%50)(?:i|%69|%49)`), which matches every spelling that decodes to the
   prefix and nothing else. Scoping to machine-facing decoded paths is what keeps `/caf%C3%A9`
   working; scoping to the first segment is what keeps `/api/board%20s` ordinary route data.

Scope is the prefix boundary. An encoded slash *inside* a machine path (`/api/boards%2Fx`), and a
duplicated slash inside one (`/api//boards`), are ordinary route data: already machine surface to all
three layers, and already answering the `404` contract when no route matches. They are untouched.

The **standalone MCP host** (`--mcp --transport http`) runs the same guard. It builds its own
pipeline rather than calling `ConfigureTaskdeckPipeline`, so the guard is registered there
explicitly, ahead of `ApiKeyMiddleware`. It has no SPA fallback to leak, but without the guard `/MCP`
with a valid key reached the real endpoint — one URL meaning two different things depending on which
host serves it.

The guard runs **ahead of the CORS middleware** in both pipelines. `CorsMiddleware` short-circuits a
preflight with `204` as soon as it sees `Access-Control-Request-Method`, so a guard behind it would
answer `OPTIONS /API/boards` with a success a browser reads as "this endpoint exists". Correlation
ID, the unhandled-exception wrapper and the security headers moved ahead of CORS with it, so the
guard still runs inside all three.

## Consequences

- A client that reaches `/API/...` — a hand-typed URL, a case-mangling proxy, a client that upcases
  paths — gets `404` rather than the resource. This is the intended cost of A: exactly one spelling
  works, and it is the one every layer agrees on.
- **One residual divergence is accepted rather than removed.** A double-encoded slash
  (`/mcp%252Fmessages`) reaches the API as `/mcp%2Fmessages`, because the host decodes `%25` and
  leaves `%2F` — the two spellings are one path there. nginx and the service worker see the raw form
  and keep it SPA-side. So the layers differ for this one input: the proxy routes it to the SPA while
  the single-container API answers `404`. The divergence points at the closed answer, and closing it
  would mean decoding `%25` differently from every other escape. Recorded, not fixed.
- nginx answers these with its own default `404` body, not the `ApiErrorResponse` JSON the API uses.
  A client that parses the error body sees two shapes for the same status depending on topology. Not
  worth a second error-page contract in the proxy for a path that does not exist.
- Route existence is unchanged as a disclosure surface: a variant discloses nothing a canonical path
  did not already disclose under ADR-0059.
- The guard runs on every request. It is four `StartsWithSegments` probes plus a bounded character
  test on a string already in memory, ahead of any I/O.
- Normalization (option B) stays rejected. It would put a second path parser in front of the router
  whose agreement with the first is a standing correctness obligation across three codebases —
  a class of defect this issue is itself an instance of.

## Alternatives Considered

**Normalize in all three layers (option B).** Rejected by the ruling. Beyond the standing-agreement
cost above, it makes the alias *work*, so a caller can come to depend on `/API/boards` and any layer
that later loses its rewrite becomes a silent behavior change rather than a `404`.

**Make ASP.NET routing case-sensitive instead of adding a guard.** Route matching is
case-insensitive by framework default and there is no supported switch that changes it for literal
segments without replacing the matcher. Even with one, `/API/typo` would then fall to the SPA
catch-all and answer `200` + `index.html` — reintroducing `#1971`'s defect for exactly the paths this
ADR is about. The guard rejects before routing so both halves stay correct.

**Fix only the API.** The variant would still be answered `200` + the app shell by the reverse proxy
in the split topology, and from the precache by an installed PWA. The defect is a disagreement
*between* layers, so a one-layer fix relocates it rather than removing it.
