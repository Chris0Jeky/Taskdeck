# ADR-0059: Machine-Facing Paths Answer 405 for a Wrong Verb and 404 Only for a Missing Route

- **Status**: Accepted (ratified by the maintainer in-session on 2026-08-24 — guided-walkthrough
  reply q-2 A, decision map `map:v1:b5c39272…`, recorded on `#1992`; not inferred from the
  implementation or its tests)
- **Date**: 2026-08-24
- **Deciders**: Chris0Jeky (maintainer)
- **Related**: `#1992`, `#1971`, `#1132` (AC4 auth-outcome ordering), `#1181` (bare `/` handling),
  ADR-0036 amendment 2026-08-22

## Context

`#1971` scoped the SPA fallback so an unknown `/api/*` path stops answering `200 OK` + `index.html`.
It did that by mapping a per-prefix catch-all under each machine-facing prefix (`/api`, `/hubs`,
`/health`, `/mcp`) that returns the JSON error contract, and by stamping those catch-alls with
`[GET, HEAD]` — the same methods `MapFallbackToFile` puts on the SPA catch-all.

That method scoping is load-bearing in one direction and harmful in the other. ASP.NET Core only
reaches its 405 endpoint when *every* candidate for the request method is method-mismatched, so an
all-verb catch-all would have been a valid candidate for `PUT /api/boards` and would have downgraded
that 405 to a 404. Scoped to `GET`/`HEAD`, verbs outside the pair still reach the framework's 405.

Inside the pair the same scoping produces three inconsistencies, measured on `main` at `6d45871ae`:

| Request | Answer before | Why |
| --- | --- | --- |
| `GET /api/import/notes/markdown` (route is POST-only) | `404` + error contract | The catch-all is a valid GET candidate, so routing never reaches its 405 endpoint. |
| `PUT /api/import/notes/markdown` | `405`, `Allow: GET, HEAD, POST` | Routing builds `Allow` from the union of every method at the node, and the catch-all contributes `GET, HEAD`. |
| `PUT /api/totally-unknown` | `405`, `Allow: GET, HEAD`, no body | The catch-all made the node method-constrained, so an unknown path answers 405 and advertises verbs for a path nothing is routed to. |

`#1971` recorded the third as an accepted trade and the first as a residual for `#1992`. The second
was not noticed. Together they mean two responses about the same URL contradict each other: `PUT`
said `GET` was allowed, `GET` said the route did not exist.

Routing cannot be asked to resolve this from inside a fallback. `HttpMethodMatcherPolicy` is applied
as a *node-builder* policy, so the candidate set is partitioned by verb inside the DFA: a `GET`
request never sees the `POST` endpoint that shares its path.

## Decision

Paths under `PipelineConfiguration.NonSpaPathPrefixes` answer on one rule, for every verb:

1. **A route exists at this path under some other verb → `405`**, with an `Allow` header listing
   exactly the methods that route declares. The body is empty, matching how the framework already
   answers `PUT /api/boards`.

   **No `HEAD` is inferred from `GET`.** An earlier draft of this ADR asserted that routing "serves
   `HEAD` implicitly" from a `GET` endpoint and added it to `Allow`. That was recorded as measured
   fact and was never measured; it is false here. Measured on this app, .NET 8:

   | Request | Result | What it establishes |
   | --- | --- | --- |
   | `GET /api/boards` (authenticated) | `200` | The `GET` action exists and serves. |
   | `HEAD /api/boards` (authenticated) | `405`, `Allow: GET, POST` | Routing does not serve `HEAD` from that `GET` action. |
   | `HEAD /api/boards` (anonymous) | `405` | The `AllowAnonymous` machine fallback matched `HEAD` — that is where it lands. |
   | `PUT /api/boards` (anonymous) | `405`, `Allow: GET, POST` | A verb the fallback does *not* accept reaches routing's synthetic 405 endpoint; the shipped pipeline replaces that metadata-less endpoint with an `AllowAnonymous` equivalent on machine paths, so the wrong-verb answer is verb-independent for anonymous callers. (Before that replacement this measured `401` — the global `FallbackPolicy` answered first — which is what distinguished the two paths and proved `HEAD` takes the fallback.) |

   Taskdeck declares no `[HttpHead]` anywhere, so `HEAD` on a `GET`-declaring machine route is not
   matched by that route at all. Inferring it produced `HEAD /api/boards` → `405` with
   `Allow: GET, HEAD, POST`: a response advertising the very method it had just rejected, which sends
   a client that honours `Allow` into a retry loop on the same 405. RFC 9110 requires `Allow` to name
   the methods the resource supports. `HEAD` on such a route now answers `405` with
   `Allow: GET, POST`, which is self-consistent.

   Making `HEAD` actually work is deliberately out of scope here — that is new API surface, not a
   correction to the 404/405 contract.
2. **No route exists at this path under any verb → `404`** with the `ApiErrorResponse` contract
   (`errorCode`/`message`, `application/json`), and no `Allow` header.

"A route exists" is decided against the endpoint graph, not the request that reached the fallback.
`MachineRouteMethodResolver` translates every non-fallback endpoint under a machine prefix into a
`TemplateMatcher` plus its resolved inline constraints, once, lazily, and matches the request path
against that set. **Constraints are evaluated**: `/api/abuse/actors/not-a-guid/evaluate` matches the
POST route's template but fails its `{actorUserId:guid}` constraint, so it stays a `404` rather than
being upgraded to a `405` for a resource that does not exist.

The `[GET, HEAD]` scoping on the catch-alls stays. It is what keeps other verbs reaching routing's
405 endpoint, where a thin middleware corrects the `Allow` header or converts the answer to the 404
contract. Anonymous access is unchanged: the fallbacks remain `AllowAnonymous`, so an unknown path
still answers 404 rather than 401, and a real route still answers 401 before anything else
(`#1132` AC4).

## Consequences

- Wrong-verb requests answer the same status regardless of which verb is wrong. A client that
  retries on the advertised `Allow` header is now told the truth.
- Route existence under a machine prefix is discoverable without credentials by the `404`/`405`
  split, on top of the `404`/`401` split `#1971` already accepted. The OpenAPI document publishes the
  same information, and every other verb already leaked it via the framework's 405.
- One code path answers both statuses, so the pipeline comment can state a property the code has.
  The previous comment claimed a wrong-verb request "mismatches every candidate and keeps its 405",
  which was false for exactly the two verbs the catch-all accepts — the same class of defect
  (a permanent comment recording a safety property the code lacks) that `#1971` was filed to fix.
- The resolver caches its translated route set for the process lifetime. Taskdeck registers no
  dynamic endpoints, but a future surface that mutates an endpoint data source after startup would
  need that cache invalidated.
- A route pattern the template/constraint machinery cannot express is logged and skipped, and
  wrong-verb requests on that one route keep answering `404`. No such pattern exists today.

## Alternatives Considered

**Accept and document the `404`.** The option `#1992` names first. Rejected because the surface
already answers `405` for every verb outside `GET`/`HEAD`, so `404` is not a posture — it is an
inconsistency, and the "harder to enumerate" argument does not hold when `PUT` on the same URL
returns `405` with an `Allow` header.

**Drop the method scoping and let one all-verb catch-all own everything.** Simplest, but it becomes a
valid candidate for every verb, so `PUT /api/boards` loses its framework `405`, and it displaces
`ConsumesMatcherPolicy` (two endpoints declare `[Consumes("multipart/form-data")]`, whose `415`
would become a `405`) and the CORS preflight endpoint selection.

**Register a `405` shim endpoint per real route at startup.** Routing would then do the matching and
the constraint evaluation for free. Rejected because it requires materializing the controller
endpoint data source during pipeline configuration — after which conventions can no longer be added
to it — and doubles the endpoint count for every machine route.
