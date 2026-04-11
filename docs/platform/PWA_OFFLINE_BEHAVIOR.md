# PWA Offline Behavior

This document defines what works, what queues, and what is blocked when Taskdeck is used offline as a Progressive Web App.

## Service Worker Strategy

Taskdeck uses **Workbox** (via `vite-plugin-pwa`) with a `generateSW` approach.

### Precaching

All app shell assets (JS, CSS, HTML, icons, fonts) are precached on first load. This means the application UI loads instantly on subsequent visits, even without a network connection.

### Runtime Caching

| Resource | Strategy | TTL | Notes |
|----------|----------|-----|-------|
| API responses (`/api/*`) | NetworkFirst | 24 hours | Fresh data preferred; falls back to cache when offline |
| Google Fonts CSS | StaleWhileRevalidate | 1 year | Cached stylesheets served immediately; revalidated in background |
| Static assets (images, fonts, icons) | CacheFirst | 30 days | Served from cache; only fetched on cache miss |

### Navigation Fallback

For SPA deep links (e.g. `/workspace/boards/{id}`), the service worker serves `index.html` when offline, allowing Vue Router to handle the route. API paths (`/api/*`) and MCP paths (`/mcp`) are excluded from this fallback.

## What Works Offline

These features function with cached data when the network is unavailable:

- **App shell and navigation**: All routes render. The sidebar, topbar, command palette, and keyboard shortcuts work normally.
- **Previously loaded board views**: Boards, columns, and cards that were loaded during the last online session are available from the API response cache.
- **Previously loaded data**: Inbox items, notifications, review proposals, and other data loaded while online remain accessible via cached API responses.
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
3. **Cached API responses** continue to be served until fresh data is fetched. The NetworkFirst strategy for API calls means the next navigation or data load will fetch fresh data from the server.
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
- Runtime caches (API responses, fonts, static assets) have TTL-based expiration and entry count limits.

## PWA Installability

The app meets Chrome's PWA installability criteria:

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
- **Cache size**: Runtime caches are bounded (100 API entries, 50 static assets). Heavy usage may evict older entries.
- **Background sync**: The Background Sync API is not yet used. Failed requests are not automatically retried.
- **Push notifications**: Web Push is not implemented. Notifications require an active tab with SignalR connection.
