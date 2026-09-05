# PWA Offline Behavior

This document defines what works, what queues, and what is blocked when Taskdeck is used offline as a Progressive Web App.

## Service Worker Strategy

Taskdeck uses **Workbox** (via `vite-plugin-pwa`) with a `generateSW` approach.

### Precaching

All app shell assets (JS, CSS, HTML, icons, fonts) are precached on first load. This means the application UI loads instantly on subsequent visits, even without a network connection.

### Runtime Caching

| Resource | Strategy | TTL | Notes |
|----------|----------|-----|-------|
| API responses (default `/api/*` or the configured API base) | Network only | N/A | Not stored by the service worker: runtime routes reject both the default and configured API paths, and `ApiCacheControlMiddleware` stamps `no-store, private` on every `/api` response so the browser cache does not hold it either. |
| Lazy `it`/`es` locale chunks | StaleWhileRevalidate | Content-versioned | Cached after first use so the selected language remains available offline |
| Static assets under `/assets/` and `/icons/` | CacheFirst | 30 days | Served from cache after a miss; there is no Google Fonts runtime route |

The static-asset route is anchored on the directories the build emits, not on the file extension.
The API base is a deployment choice: `VITE_API_BASE_URL` may be prefixed, such as `/taskdeck/api`,
or nested under an emitted directory, such as `/assets/api`. The build normalizes that configured
path and serializes both it and the default `/api` boundary into each runtime matcher. A malformed
or ambiguous configured base produces a match-nothing predicate, so runtime caching fails closed.

**The boundary is configuration-aware but not origin-anchored.** The generated regular expressions
match the complete request URL but deliberately allow any HTTP or HTTPS authority. Together with
`cacheableResponse` statuses `[0, 200]`, this means an opaque third-party response under a matching
static path can still be admitted. Nothing identity-bound leaks because both API path boundaries are
excluded, but `taskdeck-static-assets` is not first-party-only.

### Retiring the pre-#2350 worker

An installation that predates this change still runs a worker with a NetworkFirst API route, and it
repopulates the authenticated cache after any page-side purge. `registerType: 'prompt'` lets that
worker wait indefinitely - the update banner is never shown on the public login route, and a user can
dismiss it - so the migration is not left to the update UI.

Before any session is established, the page asks the controlling worker for the retirement policy over
a `MessageChannel` (`src/pwa/legacyApiCacheWorker.ts`). A pre-#2350 worker has no listener, while the
#2350 worker reports an older policy marker whose static-asset rule is not configuration-aware. The page
accepts only the current versioned marker, so either installed predecessor causes
`registration.update()`. It then follows the replacement through
`updatefound` and `statechange` until it reaches `installed`, at which point it is messaged to skip
waiting. Following it matters: `registration.update()` resolves inside Install, *before* the install
event's lifetime promises settle, so `registration.waiting` is normally still null when it returns and
a single read taken *at that moment* would not deliver the message. The code still reads
`registration.waiting` once up front, deliberately, for the case where a replacement is already
waiting because the user dismissed the update banner; that read is not the anti-pattern. The replacement claims open clients on
activation, so the switch does not need a reload.

Every step is bounded, and the whole migration has a hard 12-second ceiling, because session restore
and the router guard both await it - an unbounded update fetch would pin the app on its loading state
with a reload that re-enters the same wait. `controllerchange` is latched from the start of the
attempt rather than subscribed to at the end, so a replacement that another tab's migration used to
claim this page is not missed.

If nothing takes over inside the wait, the attempt **fails closed**. It unregisters only when the
registration holds nothing that could still become a compliant controller; when a replacement is
installing or waiting, unregistering would destroy it, so the page reports failure and a reload lets
it activate. Either way the legacy worker still controls the current page, so session establishment
stays refused until reload - and a *missing* registration is not treated as success while a
non-answering controller is still intercepting, because `unregister()` never releases a page the
worker already controls.

The migration invalidates the whole `taskdeck-static-assets` runtime cache rather than trying to
reconstruct the build-time API base inside the public worker script. The old extension-only matcher
could have stored an authenticated response there under a prefixed API base, where it would otherwise
survive an account switch for 30 days. Normal assets are cached again on their next successful
request; the share-target queue is preserved.

The sweep runs when `public/api-cache-cleanup.js` is **evaluated**, not only from its `activate`
listener. A `taskdeck-pwa-cache-policy-v2` marker cache, written only after the sweep completes,
keeps that evaluation-time pass a one-time migration instead of a purge on every worker restart,
and a failed sweep is not memoised, so a later restart retries it.

The `activate` listener always re-sweeps **unconditionally** - it does not reuse the memoised
evaluation-time promise and does not short-circuit on the marker cache. That matters because the
evaluation-time sweep runs during *install*, while the old vulnerable worker is still the controller
and can still store an identity-bound response in `taskdeck-static-assets`. Reusing the completed
sweep would let anything cached in that install-to-activation window survive the migration, which is
exactly the threat model. The forced sweep leaves the share-target queue and the Workbox precache
untouched.

What the forced sweep's `event.waitUntil` does and does not buy, precisely. It holds the worker in
`activating`, and Handle Fetch waits for `activated` before it lets the worker answer a request, so
nothing can be **served** out of a half-swept cache. It does **not** make activation fail: the spec
aborts only on a rejected *install*, never on a rejected activate, so a sweep that cannot complete
is surfaced (console warning plus an unhandled rejection) and the worker still activates with the
old entries in place. It also does not hide the window from page script: the registration's active
worker is swapped to the replacement *before* `activate` is dispatched, so a page reading
`CacheStorage` directly - as the browser regression below does - can observe the pre-sweep state
until the worker reaches `activated`.

The generated worker loads this file with `importScripts()`, which `vite-plugin-pwa` emits from
inside an asynchronous AMD `define()` factory - a promise continuation, not the worker's synchronous
initial evaluation. `#2411` (PR `#2416`) and `#2639` recorded that as the reason the `activate`
listener never fired. **Re-measured 2026-09-05 with breadcrumb caches in Chromium 151.0.7922.34
(Playwright 1.62.1): it does fire**, on first install, on the waiting-then-skip-waiting migration
path, and on a worker killed over CDP and restarted straight into activation - 3 of 3 runs each. In
that engine the factory's microtask drains before the lifecycle event is dispatched. No
specification requires that, so `vite.config.ts` now hoists the `importScripts` call to the top of
the emitted `dist/sw.js` (`src/pwa/hoistWorkerImportScripts.ts`): the listener is attached during
initial evaluation and the ordering stops being a race. The rewrite fails the build if the emitted
call is missing, duplicated, or not in statement position, so a `vite-plugin-pwa` upgrade cannot
silently put it back inside the factory.

`tests/pwa-generated-worker.spec.ts` pins this against the real emitted `dist/api-cache-cleanup.js`
using the cache names parsed out of the generated `dist/sw.js`. One case asserts the structure
directly: the cleanup script's `importScripts` call must sit at offset 0 of `dist/sw.js`, ahead of
the AMD shim and the `define()` call. Another deliberately never dispatches an `activate` event, so
a build that retires the caches only from that listener fails it; another re-seeds the static cache
after the evaluation-time sweep has written the marker and then dispatches `activate`, so a build
whose activation reuses that completed sweep fails too. Those dispatch cases are handler contracts,
not attachment proof - a fake event cannot show that the real listener exists in time, which is why
the structural case is the one that pins the build shape. A fourth asserts the marker cache name
carries the same version suffix as the policy handshake constant in `src/pwa/legacyApiCacheWorker.ts`,
so a future bump cannot silently leave the migration keyed on the old version.
`src/tests/pwa/hoistWorkerImportScripts.spec.ts` covers the rewrite itself, including the shapes it
refuses to touch.

At browser level, `tests/e2e/pwa-proof-strict.spec.ts` (gated on `TASKDECK_E2E_PWA_PREVIEW=1`, run
through `playwright.pwa-proof.config.ts`) holds the install-to-activation window open with a waiting
replacement, seeds `taskdeck-static-assets` through the old worker's own CacheFirst handler, and
asserts the entry is gone once the replacement reaches `activated`. It discriminates the forced
re-sweep. It does **not** discriminate the importScripts hoist: measured 2026-09-05, it is green
with and without it.

Normal (non-security) updates still go through the `SwUpdatePrompt` banner: only this migration sends
skip-waiting.

### Navigation Fallback

For SPA deep links (e.g. `/workspace/boards/{id}`), the service worker serves `index.html` when offline, allowing Vue Router to handle the route. API paths (`/api/*`) and MCP paths (`/mcp`) are excluded from this fallback.

## What Works Offline

These features function without backend data when the network is unavailable:

- **App shell and navigation**: All routes render. The sidebar, topbar, command palette, and keyboard shortcuts work normally.
- **Local UI interactions**: Sorting, filtering, searching within already-loaded data, toggling UI states, opening/closing modals.

## What Queues (Future)

These operations are not yet implemented but are planned for queued offline execution:

- **Capture submissions**: New capture entries could be stored in IndexedDB and synced when connectivity returns. (Not yet implemented; currently blocked offline.)
- **Board mutations**: Card moves, edits, and column reorders could be queued locally. (Not yet implemented; currently blocked offline.)

> **Note**: Queued offline writes require conflict resolution logic that is not yet built. The current implementation treats offline as a **read-only degraded mode**.

## What Is Blocked Offline

These features require an active network connection:

- **Real-time collaboration**: SignalR connections cannot be established offline. Board presence indicators and live updates are unavailable.
- **New data fetching**: Loading boards, cards, or other data not already in the cache will fail. The UI shows appropriate error states.
- **Authentication**: Login and registration require the backend. JWT refresh also requires connectivity.
- **LLM chat and automation**: Chat sessions, tool-calling, and proposal generation require backend LLM processing.
- **Write operations**: All create/update/delete operations currently require the backend and will fail offline with an error.
- **Data export/import**: Export and import operations require backend processing.
- **MCP server communication**: MCP tool and resource access requires the backend.

## Reconnection Behavior

When the browser transitions from offline to online:

1. The **offline banner** (`OfflineBanner.vue`) disappears automatically via the `useOnlineStatus` composable.
2. **SignalR** attempts automatic reconnection (handled by the existing `useBoardRealtime` composable).
3. **API reads** fetch from the server; an offline or failed read surfaces the application's normal error state rather than replaying data from a previous identity.
5. **Cache-boundary failures are reported by what already committed.** A failure before the credential
   is sent asks the user to retry. A failure after the server has already created an account or issued
   a token says so and asks for a reload and a fresh sign-in, because retrying the original call would
   collide on a duplicate username or a consumed invite.
4. **No automatic retry** of failed mutations. The user must re-trigger any write operations that failed while offline.

## Service Worker Updates

When a new version of the app is deployed:

1. The service worker detects the update during routine checks (typically on navigation or after 24 hours).
2. The new service worker installs in the background.
3. `SwUpdatePrompt.vue` displays a non-intrusive banner: "A new version of Taskdeck is available."
4. The user clicks "Update now" to activate the new version, which reloads the page.
5. If dismissed, the update activates on the next full page load or browser restart.

## Cache Versioning

Workbox handles cache versioning automatically via content hashing in precache manifests. When the app is rebuilt:

- Precached assets with changed hashes are fetched and updated.
- Stale caches from previous versions are cleaned up (`cleanupOutdatedCaches: true`).
- Runtime caches for lazy locale chunks and static assets have bounded expiration and entry counts. Every legacy `taskdeck-api-cache*` namespace is deleted on service-worker activation and identity transitions; the share-target queue is preserved.

## PWA Installability

The generated frontend assets meet Chrome's PWA installability criteria when the deployment serves
the same-origin manifest under its CSP. Exact packaged-desktop proof currently finds that the API
CSP omits `manifest-src`, so Chrome blocks `manifest.webmanifest` on that surface; issue `#2045`
tracks the directive and packaged reproof. The generated contract includes:

- Valid `manifest.webmanifest` with `name`, `short_name`, `start_url`, `display: standalone`, and icons (192x192 and 512x512 PNG).
- Service worker with fetch handler (provided by Workbox).
- Served over HTTPS (required for production; localhost is exempt during development).
- Separate `any` and `maskable` icon purposes (not combined, per Chrome deprecation).

## Mobile Viewport

The app uses responsive design tokens and mobile-specific CSS breakpoints:

- `AppShell.vue` includes a mobile topbar with hamburger menu at `max-width: 640px`.
- Content padding reduces on mobile viewports.
- Board views use horizontal scrolling for columns on narrow screens.
- Touch targets meet the 44x44px minimum size guideline.

## Limitations and Future Work

- **No IndexedDB queue**: Offline writes are blocked, not queued. A future iteration may add an IndexedDB-backed mutation queue with conflict resolution.
- **Cache size**: Static runtime caches are bounded (50 static assets); locale chunks are bounded separately. API responses are intentionally not cached.
- **Background sync**: The Background Sync API is not yet used. Failed requests are not automatically retried.
- **Push notifications**: Web Push is not implemented. Notifications require an active tab with SignalR connection.
