# Mobile Platform Strategy

**Date:** 2026-03-29
**Scope:** Android, iOS, and mobile-responsive access for Taskdeck
**Status:** Strategic planning document — not yet executed

---

## 1. Why Mobile Matters

The thesis is "near-zero-friction capture." But capture happens everywhere — at a coffee shop, in transit, during a meeting. If Taskdeck only works at a desk with a browser, the capture promise has a ceiling.

**Scenarios that require mobile:**
- Quick thought capture while away from computer (the #1 use case)
- Reviewing proposals on the go (approve/reject a batch before a meeting)
- Checking board status for a quick reference
- Responding to notifications (mention, proposal ready)
- Light card editing (update status, add a comment)

**What mobile does NOT need to do (initially):**
- Full board editing (drag-drop columns, complex card creation)
- Automation configuration
- Ops/admin surfaces
- Starter pack management

**Design principle: Mobile is a capture-and-review companion, not a replacement for the desktop experience.**

---

## 2. Approach Comparison

### 2.1 Option A: Progressive Web App (PWA) — Recommended

**How it works:** Add a web manifest, service worker, and responsive CSS to the existing Vue 3 app. Users "install" it from their browser to their home screen.

**Pros:**
- Works with existing Vue 3 codebase (no rewrite)
- Single codebase for desktop and mobile
- Instant updates (no app store review)
- Installable on both Android and iOS
- Service worker enables offline caching
- Can be listed on Google Play (via TWA/Bubblewrap) and Microsoft Store (via PWABuilder)
- Near-zero additional maintenance burden

**Cons:**
- iOS Safari has PWA limitations (no background sync, limited push notifications pre-iOS 16.4, but push is now supported since iOS 16.4)
- No native share sheet integration (limited to Web Share API)
- No home screen widget support
- App-like feel requires careful CSS/UX work
- Not listable on Apple App Store as a PWA (Apple restricts web-only apps)

**Implementation effort:** 2-3 weeks for basic PWA, 4-6 weeks for polished mobile UX
**Ongoing maintenance:** Minimal (same codebase as desktop)

### 2.2 Option B: Capacitor (Ionic) Wrapper

**How it works:** Wraps the existing Vue 3 app in a native iOS/Android shell using Capacitor. The web app runs in a WebView but gains access to native APIs.

**Pros:**
- Uses existing Vue 3 codebase (minimal rewrite)
- Full native API access (push notifications, share sheet, biometrics, widgets)
- Can be published to both App Store and Google Play
- Capacitor has mature Vue support
- Same code serves web, iOS, and Android

**Cons:**
- Needs Xcode (macOS) for iOS builds
- App store review process and compliance
- Apple's $99/year developer fee
- Google's $25 one-time developer fee
- Slightly more complex build pipeline
- WebView performance is usually fine but can feel sluggish on older devices
- Need to maintain native project config (Xcode project, Gradle config)

**Implementation effort:** 3-5 weeks
**Ongoing maintenance:** Medium (native project config, app store updates)

### 2.3 Option C: React Native / Flutter (Full Rewrite)

**How it works:** Rewrite the frontend from scratch in React Native or Flutter.

**Pros:**
- True native performance and feel
- Full access to all native APIs
- Best possible mobile UX

**Cons:**
- Complete frontend rewrite (months of work)
- Two codebases to maintain (web + mobile)
- Duplicated business logic
- Solo developer maintaining two frontends is unsustainable
- React Native and Flutter both have their own ecosystems and learning curves

**Verdict: Not viable for a solo developer.** The maintenance burden alone makes this a non-starter until there's a team.

### 2.4 Option D: Tauri 2.0 Mobile Targets

**How it works:** Tauri 2.0 added iOS and Android build targets. The existing Vue 3 app runs in the platform's native WebView with Rust-based native bridges.

**Pros:**
- Same frontend codebase (Vue 3)
- Smaller than Capacitor/Cordova (Rust vs JavaScript runtime)
- Unified desktop + mobile pipeline if already using Tauri for desktop
- Native API access via Tauri plugins

**Cons:**
- Tauri mobile is relatively new (2024-2025), less battle-tested
- Rust knowledge needed for native plugins
- iOS builds require macOS + Xcode
- Smaller community than Capacitor

**Implementation effort:** 3-5 weeks (if already using Tauri for desktop)
**Ongoing maintenance:** Medium

### 2.5 Comparison Matrix

| Criterion | PWA | Capacitor | React Native | Tauri Mobile |
|-----------|-----|-----------|-------------|-------------|
| Rewrite needed | None | Minimal | Complete | Minimal |
| iOS App Store | No (web only) | Yes | Yes | Yes |
| Google Play | Yes (TWA) | Yes | Yes | Yes |
| Push notifications | Yes (since iOS 16.4) | Yes (native) | Yes (native) | Yes (plugin) |
| Offline support | Service worker | Service worker + native | Custom | Service worker |
| Share sheet | Web Share API (limited) | Full native | Full native | Plugin |
| Widgets | No | Yes (custom) | Yes (custom) | Possible |
| Build complexity | Low | Medium | High | Medium |
| Ongoing maintenance | Very low | Medium | Very high | Medium |
| Solo dev feasibility | Excellent | Good | Poor | Good |
| Time to ship | 2-3 weeks | 3-5 weeks | 3-6 months | 3-5 weeks |

---

## 3. Recommended Approach: PWA First, Capacitor Later

### Phase 1: Responsive Web + PWA (Month 2-3)

Make the existing Vue 3 app work well on mobile screens and installable as a PWA.

**What to build:**
1. **Web App Manifest** (`manifest.json`) — app name, icons, theme color, display mode
2. **Service Worker** (Workbox via vite-plugin-pwa) — cache static assets, offline shell
3. **Responsive layout** — mobile-first CSS for core flows
4. **Mobile navigation** — bottom tab bar for mobile, sidebar for desktop
5. **Touch-optimized interactions** — larger tap targets, swipe gestures

**What NOT to build yet:**
- Offline data sync (complex, save for later)
- Native push notifications (PWA push is sufficient initially)
- Home screen widgets
- Full board drag-drop on mobile (use simplified card list view)

### Phase 2: Mobile UX Optimization (Month 3-5)

Deepen the mobile experience within the PWA:
1. **Quick capture from home screen** — the hero mobile feature
2. **Pull-to-refresh** across all list views
3. **Swipe gestures** — swipe right to approve proposal, left to reject
4. **Board card view** — vertical card list (not column-based on small screens)
5. **Offline capture** — cache captures locally, sync when online
6. **Biometric auth** — Web Authentication API for fingerprint/face unlock

### Phase 3: Capacitor Wrapper (Month 6+, if demand justifies)

Wrap the PWA in Capacitor for native app store presence:
1. iOS App Store listing
2. Google Play Store listing
3. Native push notifications
4. Share sheet integration ("Share to Taskdeck" from any app)
5. Home screen widgets (Android)
6. Biometric authentication (native)

**Trigger for Phase 3:** >500 mobile PWA users requesting native features.

---

## 4. Mobile UX Restructuring

### 4.1 Navigation Pattern

**Desktop (current):** Sidebar navigation
```
[Sidebar]
  Home
  Today
  Inbox
  Projects (Boards)
  Review
  Settings
```

**Mobile:** Bottom tab bar (5 tabs max)
```
[Bottom Tabs]
  Home | Inbox | Capture (+) | Review | Boards
```

The center "Capture" button is the hero action — always accessible, one tap.

### 4.2 Board View on Mobile

Desktop kanban (horizontal columns) doesn't work on phone screens.

**Mobile board options:**

| Approach | UX | Complexity |
|----------|-----|-----------|
| **Column list** — tap a column to see its cards | Simple, clear | Low |
| **Swimlane view** — horizontal swipe between columns | Feels native | Medium |
| **Card list** — flat list of all cards, filterable by column | Simplest | Very low |
| **Priority view** — cards sorted by priority/due date | Task-focused | Low |

**Recommendation:** Start with **column list** (tap column → see cards) with a **priority/due view** as default. Add horizontal swipe between columns as a polish step.

### 4.3 Capture on Mobile

The #1 mobile use case. Must be <3 seconds from unlock to captured.

**Flows:**
1. **From app:** Tap center (+) button → text input → send → done
2. **From notification:** Long-press notification → "Quick capture" action → text input → send
3. **From share sheet (Capacitor only):** Share text from any app → Taskdeck capture → done
4. **From home screen shortcut:** "Quick Capture" shortcut icon → opens directly to capture modal

### 4.4 Review on Mobile

Proposals need to be reviewable on mobile. The current ReviewView is 1,022 LOC — it needs a mobile-specific simplified view.

**Mobile review pattern:**
```
[Proposal Card]
  📋 "Add 3 tasks from meeting notes"
  Board: Project Alpha
  Source: Capture #42
  Risk: Low

  [Approve ✓]  [Reject ✗]  [Details →]
```

Tinder-style swipe: right to approve, left to reject. Tap for details.

### 4.5 Card Editing on Mobile

Keep it simple:
- Title (editable)
- Description (editable, basic Markdown)
- Column/status (dropdown)
- Labels (checkboxes)
- Comments (add/view)
- Due date (date picker)

No drag-drop card reordering on mobile. Use explicit "Move to column" dropdown instead.

### 4.6 Responsive Breakpoints

```css
/* Mobile (phone) */
@media (max-width: 640px) { /* Single column, bottom tabs */ }

/* Tablet */
@media (min-width: 641px) and (max-width: 1024px) { /* Sidebar + content */ }

/* Desktop */
@media (min-width: 1025px) { /* Full layout (current) */ }
```

---

## 5. PWA Technical Implementation

### 5.1 Web App Manifest

```json
{
  "name": "Taskdeck",
  "short_name": "Taskdeck",
  "description": "Capture anything, automate safely, stay in control",
  "start_url": "/workspace/home",
  "display": "standalone",
  "background_color": "#0f172a",
  "theme_color": "#3b82f6",
  "orientation": "any",
  "icons": [
    { "src": "/icons/icon-192.png", "sizes": "192x192", "type": "image/png" },
    { "src": "/icons/icon-512.png", "sizes": "512x512", "type": "image/png" },
    { "src": "/icons/icon-maskable.png", "sizes": "512x512", "type": "image/png", "purpose": "maskable" }
  ],
  "screenshots": [
    { "src": "/screenshots/mobile-home.png", "sizes": "390x844", "type": "image/png", "form_factor": "narrow" },
    { "src": "/screenshots/desktop-board.png", "sizes": "1920x1080", "type": "image/png", "form_factor": "wide" }
  ]
}
```

### 5.2 Service Worker (via vite-plugin-pwa)

```ts
// vite.config.ts addition
import { VitePWA } from 'vite-plugin-pwa'

export default defineConfig({
  plugins: [
    vue(),
    VitePWA({
      registerType: 'autoUpdate',
      workbox: {
        globPatterns: ['**/*.{js,css,html,ico,png,svg,woff2}'],
        runtimeCaching: [
          {
            urlPattern: /^https:\/\/.*\/api\//,
            handler: 'NetworkFirst',
            options: { cacheName: 'api-cache', expiration: { maxEntries: 50, maxAgeSeconds: 300 } }
          }
        ]
      }
    })
  ]
})
```

### 5.3 Offline Strategy

| Data | Caching Strategy | Rationale |
|------|-----------------|-----------|
| App shell (HTML/CSS/JS) | Cache-first | App loads instantly offline |
| Board data | Network-first, cache fallback | Fresh data preferred, offline readable |
| Capture submissions | Queue offline, sync when online | Never lose a capture |
| Images/assets | Cache-first | Static, rarely change |
| API responses | Network-first, short TTL cache | Balance freshness vs offline |

### 5.4 Push Notifications (PWA)

Since iOS 16.4 (March 2023), PWAs support Web Push on iOS. This covers:
- "New proposal ready for review"
- "Your capture was triaged"
- "@mention in a comment"
- "Board shared with you"

Implementation: Web Push API + VAPID keys + backend notification service (already exists).

---

## 6. Testing Mobile

### 6.1 Device Testing Matrix

| Category | Devices | Priority |
|----------|---------|----------|
| iOS (latest) | iPhone 15/16 (Safari) | High |
| iOS (older) | iPhone 12/13 (Safari) | Medium |
| Android (latest) | Pixel 8/9 (Chrome) | High |
| Android (mid-range) | Samsung Galaxy A-series (Chrome) | High |
| Android (older) | Various Android 12+ (Chrome) | Medium |
| Tablet | iPad (Safari), Samsung Tab (Chrome) | Low (Phase 2) |

### 6.2 Testing Approach

- **Existing Playwright E2E:** Add mobile viewport configurations
  ```ts
  // playwright.config.ts
  projects: [
    { name: 'mobile-chrome', use: { ...devices['Pixel 7'] } },
    { name: 'mobile-safari', use: { ...devices['iPhone 14'] } },
  ]
  ```
- **Existing issue #87:** "Cross-browser and mobile-responsive E2E matrix expansion" — directly relevant
- **Real device testing:** BrowserStack or Sauce Labs (free for open source)
- **Chrome DevTools device mode:** For rapid iteration

### 6.3 Performance Budget (Mobile)

| Metric | Target | Rationale |
|--------|--------|-----------|
| First Contentful Paint | <1.5s on 4G | Mobile attention span |
| Largest Contentful Paint | <2.5s on 4G | Core Web Vitals |
| Time to Interactive | <3.5s on 4G | Usable quickly |
| Total bundle size (gzipped) | <500KB | Mobile data constraints |
| Memory usage | <150MB | Mid-range device headroom |

---

## 7. App Store Considerations

### 7.1 Google Play (via TWA or Capacitor)

- **TWA (Trusted Web Activity):** Wraps a PWA in an Android shell. Can be built with Bubblewrap CLI. Minimal native code. $25 one-time developer fee.
- **Capacitor:** Full native shell, more capabilities. Same $25 fee.
- **Review process:** 1-3 days typically. Less strict than Apple.
- **Requirements:** Privacy policy, content rating, screenshots, description.

### 7.2 Apple App Store

- **PWA cannot be listed** on the App Store (Apple does not accept web-only apps).
- **Capacitor/native wrapper required** for App Store listing.
- **$99/year** Apple Developer Program.
- **Review process:** 1-7 days. Stricter than Google. Web-wrapped apps have been rejected if they don't offer "sufficient native experience."
- **EU regulatory changes (DMA):** Since iOS 17.4, EU users can install PWAs from alternative browsers, reducing the App Store requirement for EU users.
- **Recommendation:** Don't submit to Apple App Store until the mobile UX is genuinely good. A rejected app wastes time and Apple remembers rejections.

### 7.3 Recommendation

| Timing | Action |
|--------|--------|
| Phase 1 (Month 2-3) | PWA only (installable via browser) |
| Phase 2 (Month 4-5) | Google Play via TWA (easy, $25) |
| Phase 3 (Month 6+) | Apple App Store via Capacitor (when UX is polished) |

---

## 8. What Competitors Did

| Product | Mobile Approach | Notes |
|---------|----------------|-------|
| **Trello** | Native iOS + Android (separate apps) | Big team, years of investment |
| **Notion** | Native iOS + Android | Massive engineering team |
| **Linear** | PWA + native (later) | Started web-only, added native later |
| **Todoist** | Native iOS + Android | Cross-platform from early days |
| **Obsidian** | Native mobile (custom renderer) | Significant investment, plugin compat issues |
| **TickTick** | Native iOS + Android | Full feature parity |
| **Plane** (open source) | Responsive web only | No native mobile yet |
| **Focalboard** | Responsive web only | No native mobile |

**Pattern:** Open-source / indie tools start web-responsive and add native later when demand is proven. Enterprise tools invest in native from the start because they have the team.

---

## 9. Implementation Priority

| Priority | Task | Effort | Impact |
|----------|------|--------|--------|
| **P1** | Add web app manifest (`manifest.json`) | 2-3 hours | PWA installable |
| **P1** | Add service worker (vite-plugin-pwa) | 4-8 hours | Offline shell, installable |
| **P1** | Create app icons (192px, 512px, maskable) | 2-4 hours | PWA requirement |
| **P2** | Mobile-responsive CSS for Home, Inbox, Review | 2-3 weeks | Core mobile UX |
| **P2** | Bottom tab navigation for mobile breakpoint | 1-2 weeks | Mobile nav pattern |
| **P2** | Mobile capture modal (touch-optimized) | 1 week | Hero mobile feature |
| **P2** | Mobile board view (column list, not kanban) | 1-2 weeks | Board on mobile |
| **P3** | Mobile Playwright E2E viewport tests | 1-2 weeks | Quality gate |
| **P3** | Offline capture queue (sync when online) | 1-2 weeks | Offline UX |
| **P3** | Web Push notifications | 1-2 weeks | Engagement |
| **P4** | Google Play listing via TWA | 1-2 days | Discovery |
| **P4** | Capacitor wrapper (iOS + Android) | 3-5 weeks | App Store presence |
| **P5** | Native share sheet integration | 1-2 weeks | Capture from anywhere |
| **P5** | Home screen widgets | 2-3 weeks | Ambient awareness |

---

## 10. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| iOS Safari PWA bugs | Medium | Medium | Test on real devices, document known limitations |
| Mobile UX feels "web-appy" | Medium | High | Invest in touch interactions, native-feeling animations |
| Board view doesn't work on mobile | Low | High | Alternative mobile view (card list, not kanban) |
| Apple rejects Capacitor app | Low-Medium | Medium | Don't submit until UX is genuinely mobile-native |
| Performance on mid-range Android | Medium | Medium | Bundle size budget, lazy loading, code splitting |
| Push notification setup complexity | Low | Low | Use existing notification service, add web push endpoint |

---

## Related Documents

- Issue `#95` — PWA/offline client readiness for local-first workflows
- Issue `#87` — Cross-browser and mobile-responsive E2E matrix expansion
- `docs/PERFORMANCE_BUDGETS.md` — Existing performance thresholds
- `frontend/taskdeck-web/vite.config.ts` — Current Vite configuration
- `docs/strategy/00_MASTER_STRATEGY.md` — Master strategy document
