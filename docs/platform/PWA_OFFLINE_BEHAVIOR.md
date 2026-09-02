# PWA Offline Behavior

This document defines what works, what queues, and what is blocked when Taskdeck is used offline as a Progressive Web App.

## Service Worker Strategy

Taskdeck uses **Workbox** (via `vite-plugin-pwa`) with a `generateSW` approach.

### Precaching

All app shell assets (JS, CSS, HTML, icons, fonts) are precached on first load. This means the application UI loads instantly on subsequent visits, even without a network connection.

### Runtime Caching

| Resource | Strategy | TTL | Notes |
|----------|----------|-----|-------|
| API responses (`/api/*`) | Network only | N/A | Never stored by the service worker or browser cache because responses may be identity-bound |
| Lazy `it`/`es` locale chunks | StaleWhileRevalidate | Content-versioned | Cached after first use so the selected language remains available offline |
| Static assets under `/assets/` and `/icons/` | CacheFirst | 30 days | Served from cache after a miss; there is no Google Fonts runtime route |

The static-asset route is anchored on the directories the build emits, not on the file extension.
The API base is a deployment choice - `VITE_API_BASE_URL` may be prefixed, such as `/taskdeck/api` -
so denying `/api` alone would not stop an authenticated `GET /taskdeck/api/users/by-username/alice.png`
from being stored in the shared, cross-identity static cache. An unrecognised layout therefore loses
runtime caching for a static asset; it never admits an API response.

### Retiring the pre-#2350 worker

An installation that predates this change still runs a worker with a NetworkFirst API route, and it
repopulates the authenticated cache after any page-side purge. `registerType: 'prompt'` lets that
worker wait indefinitely - the update banner is never shown on the public login route, and a user can
dismiss it - so the migration is not left to the update UI.

Before any session is established, the page asks the controlling worker for the retirement policy over
a `MessageChannel` (`src/pwa/legacyApiCacheWorker.ts`). A pre-#2350 worker has no listener, so silence
identifies it. The page then calls `registration.update()` and messages the waiting worker to skip
waiting; the replacement claims open clients on activation, so the switch does not need a reload. If no
replacement takes over within the bounded wait, the registration is unregistered and the attempt
**fails closed**: the legacy worker still controls the current page, so session establishment is
refused until the page is reloaded. Normal (non-security) updates still go through the
`SwUpdatePrompt` banner.

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
