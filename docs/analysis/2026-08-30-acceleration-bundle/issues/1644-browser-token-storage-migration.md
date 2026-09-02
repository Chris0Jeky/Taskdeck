# Browser token storage — migrate before hosted multi-user (#1644)

Last Updated: 2026-09-02

> Curated from the v0.3/v0.4 acceleration bundle (grounded `221aa88c8`, 2026-08-30) and validated against `main` `de488fea0` on 2026-09-02 under tracker #2376 (follow-up to #2348). This issue has **no bundle issue pack** — it is curated from the live issue and the bundle's `HOSTED_BETA_READINESS_MODEL.md`, because the hosted gate ladder cannot be read without it. Planning input, not authority: the live issue, ADR-0009 and `docs/STATUS.md` win.

## Outcome

Before a stranger can register on a hosted Taskdeck, the browser stops holding a long-lived bearer token in `localStorage`. Two deployment profiles exist and are explicit: `LocalBearer` for the desktop/local install that ADR-0009 accepted, and `HostedSession` for anything multi-user — which fails closed at startup if HTTPS, cookie and origin requirements are absent.

## Live dependencies (verified 2026-09-02)

| Dependency | State | Note |
| --- | --- | --- |
| Current posture | **unchanged** | `frontend/taskdeck-web/src/utils/tokenStorage.ts` persists the JWT (`localStorage.setItem(TOKEN_KEY, token)`, line 95) and the session metadata (line 123) in `localStorage`. Its own header comment at line 8 anticipates "migrating from localStorage to HttpOnly cookies or sessionStorage" — the migration seam is deliberate |
| Consumption seam | single | `frontend/taskdeck-web/src/api/http.ts` reads the token in one request interceptor (line 52), checks `isTokenExpired`, and sets `Authorization: Bearer …` at line 57; `tokenStorage.clearAll()` on expiry and on 401. One interceptor is the whole read surface |
| CodeQL alerts #44/#45 | **dismissed, not fixed** | `js/clear-text-storage-of-sensitive-data`. The dismissal *is* the documented ADR-0009 acceptance this issue exists to revisit |
| ADR-0009 | accepted for local-first | Requires reassessment before any hosted multi-user deployment |
| Scoped risk acceptance (2026-08-19, q-6) | recorded | Covers **only** the private two-person self-hosted instance under #1772. It does not extend to public or open-registration hosting |
| Trigger | **armed** | "Before Taskdeck is offered as a hosted multi-user service." #1772 has still never been deployed, so the trigger has not fired — but #2243's public hosted beta is what fires it |
| Milestone / priority | **v0.4 / Priority I** (moved 2026-08-30 by the #2349 engineering-review reconciliation) | This is the existing owner of the external brief's SEC-002; no duplicate issue was created |
| #2350 | open | Must land first: removes the cross-session service-worker replay independently of the session redesign |
| #2243 | open | The consumer. A hosted beta cannot silently inherit the local-installation bearer posture |
| #2012 | open | v0.4 hosted open beta stays non-commercial regardless |

## Child slices (one PR each, in order)

| Id | Outcome | Depends on | Startable before predecessors merge? |
| --- | --- | --- | --- |
| `TOK-0-adr` | An ADR settling the reverse-proxy, desktop/local-HTTP, WebSocket, revocation and recovery contracts. The in-memory access token plus rotating HttpOnly refresh cookie is a **candidate, not a ruling** — the 2026-08-30 comment says so explicitly | — | **Yes for the draft.** This is the startable-now slice and everything else is blocked behind it |
| `TOK-1-sw-replay` | #2350's service-worker replay removal | — | Separate issue; sequence it first |
| `TOK-2-profiles` | Explicit `LocalBearer` and `HostedSession` deployment profiles; hosted startup fails closed without HTTPS/cookie/origin configuration | TOK-0 | No |
| `TOK-3-hosted-session` | In-memory access token + rotating HttpOnly refresh cookie behind `HostedSession`, with CSRF/origin rejection | TOK-2 | No |
| `TOK-4-signalr` | SignalR expiry, reconnect and revocation under the cookie session | TOK-3 | No |
| `TOK-5-codeql` | Close or re-evaluate alerts #44/#45 against the new posture; update ADR-0009 | TOK-3 | No |

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| Token persistence | `frontend/taskdeck-web/src/utils/tokenStorage.ts` (`TOKEN_KEY`, `SESSION_KEY`) | **exists** | Getters already validate and clear on a bad value, so the seam has a fail-closed habit to preserve |
| Token attachment | the `http.ts` request interceptor | **exists** | A single choke point — the migration's best asset |
| Expiry handling | `isTokenExpired(token)` + `tokenStorage.clearAll()` on expiry and on 401 | **exists** | Client-side expiry checking becomes meaningless once the access token is in memory only; the refresh path replaces it |
| Deployment profiles, refresh-cookie rotation, CSRF token, origin rejection, revocation propagation to SignalR | — | **new** | None exists |
| Authentication vs resource authorization | separate today | **exists** | Keep it separate; entitlements are a later, distinct concern |

## Implementation plan

**Preflight.** Read all three comments. The 2026-08-19 acceptance is *scoped* and the 2026-08-23 realignment confirms nothing has widened underneath it. The 2026-08-30 comment is the execution contract and it is unambiguous that the recommended design is a candidate, not a decision.

**Order.** #2350 first, then the ADR, then profiles, then the session. Do not start the session redesign while the service worker can still replay a cross-session request — the two would be debugged together and neither would be provable alone.

**The compatibility constraint is the hard part.** The desktop/local bearer path must keep working, over plain HTTP, with no cookie domain, while the hosted path is cookie-only. Two profiles, chosen by configuration, both fail-closed: the hosted one refuses to start without HTTPS/cookie/origin settings, the local one refuses to accept a cookie session. A single code path with runtime sniffing is the failure mode.

**Do not** widen the 2026-08-19 acceptance by implication. It covers one private two-person instance and nothing else.

## Test plan

- [ ] Cookie attributes: `HttpOnly`, `Secure`, `SameSite` asserted on the refresh cookie in the hosted profile
- [ ] CSRF: a cross-origin form post and a cross-origin `fetch` are both rejected; a same-origin request with the token succeeds
- [ ] Rotation: a refresh rotates the cookie; replaying the previous refresh token is rejected **and** revokes the family
- [ ] Revocation: a password change and an MFA change invalidate live sessions; logout and account switching leave no reusable material
- [ ] SignalR: expiry mid-connection, reconnect after rotation, and revocation while connected
- [ ] Local profile: the desktop/local bearer path still authenticates over plain HTTP with no cookie
- [ ] Fail-closed startup: the hosted profile refuses to boot without HTTPS/cookie/origin configuration — assert the failure, not just the happy path
- [ ] Frontend: `cd frontend/taskdeck-web; npm run typecheck; npx vitest --run --maxWorkers=2 <auth specs>` (bare `vitest --run` OOMs on this box)
- [ ] CodeQL alerts #44/#45 re-evaluated against the new storage

## Edge cases

- A desktop install served over `http://localhost` where `Secure` cookies are refused.
- A reverse proxy that terminates TLS and forwards a spoofable origin header.
- Two browser tabs refreshing concurrently — one rotation must win and the other must not be treated as a replay attack against the user.
- A service worker replaying a request across sessions (#2350).
- Clock skew between the browser's expiry check and the server's.
- An existing user upgrading with a valid `localStorage` token — migrate or invalidate, but never silently accept both mechanisms at once.
- Account deletion or deactivation while a refresh cookie is live.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| Blueprint | `docs/analysis/2026-08-30-acceleration-bundle/architecture/HOSTED_BETA_READINESS_MODEL.md` §4 "Identity and secrets" and §2 Stage 3 | Where session security sits in the gate ladder: Stage 3 (controlled untrusted cohort) is the first rung that requires it, and Stage 4 requires no 0/1 score in a critical domain | Read its validation preface. The blueprint never names browser token storage explicitly — this issue is the concrete instance of its "identity" domain |
| Diagram | `.../diagrams/hosted-beta-gates.svg` | Why this is a v0.4 Priority I rather than a v0.3 item | Explanatory only |

## Corrections to the bundle

1. **The bundle has no issue pack for `#1644`.** Its `01_MILESTONE_5` set covers 23 issues and this is not among them, even though the readiness blueprint's own public-gate checklist depends on it. **Consequence:** the bundle's hosted-beta dependency graph is incomplete in a Priority-I security dimension; #2243's curated file records the same gap.
2. **Bundle `HOSTED_BETA_READINESS_MODEL.md` §4:** the identity checklist lists registration mode, e-mail verification, TOTP encryption, connector-key encryption and rotation — but **not** the browser session model. **True:** `localStorage`-persisted bearer tokens are the concrete identity gap on `main`. **Consequence:** add a session-storage row to the checklist before scoring the identity domain.
3. **External brief SEC-002** (referenced by the 2026-08-30 comment): proposes the in-memory access token plus rotating HttpOnly refresh cookie as the design. **True:** the live comment classifies it as "a candidate, not a ruling" and requires the ADR to settle reverse-proxy, desktop/local-HTTP, WebSocket, revocation and recovery contracts first. **Consequence:** do not treat the brief's recommendation as the decision.
