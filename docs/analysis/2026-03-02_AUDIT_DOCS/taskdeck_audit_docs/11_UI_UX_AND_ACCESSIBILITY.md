# UI/UX and Accessibility

Score: **7 / 10**  
(The frontend is cleanly structured and typed. The biggest UX gaps are around “rate limiting feedback”, “auth/session security tradeoffs”, and potentially accessibility polish.)

## 1) Frontend structure (dev-facing UX)

### Strengths
- Vue 3 + Router + Pinia is a standard, maintainable stack.
- Strict TypeScript configuration is enabled.
- API calls are centralized under `src/api/*`.
- Route guard checks session token and redirects to login.

### Risks
- Token in localStorage means any XSS becomes account takeover.
- Error handling is generic: “toast the message” often works but can feel rough.

## 2) User-facing flows (inferred from code)

- Login/register screen
- Board list and board view
- Card operations, comments, mentions
- Capture inbox and triage
- Automation proposals and ops console surfaces

The code suggests the product supports significant workflows.

## 3) Rate limiting UX (new feature area)

Backend now returns consistent 429 errors with:
- JSON error contract
- `Retry-After` header
- `X-RateLimit-Policy` header

Frontend currently:
- logs errors in Axios interceptor
- maps most errors to a message via `useErrorMapper` and `errorMessage`

**Gap**
- There is no first-class handling for 429:
  - no “please wait 12s” message that reads `Retry-After`
  - no UI lockout to prevent spamming the user with toasts
  - no backoff/retry logic

**Recommendation**
- In Axios interceptor:
  - if status == 429:
    - read `Retry-After`
    - show a single toast with countdown
    - temporarily disable the triggering action/button
- Consider a global “cooldown” store keyed by endpoint or policy name.

## 4) Accessibility and keyboard UX

I can’t run the UI here, so this is “code-only inference”.

Things to check:
- keyboard navigation for board and card interactions
- focus management for modals
- ARIA labels for icon buttons
- color contrast in Tailwind theme
- screen reader announcements for toast notifications

Given the repo includes Playwright E2E tests, adding an accessibility audit step is realistic:
- `@axe-core/playwright` basic checks on key routes

## 5) Frontend performance UX

Key UX factors for board apps:
- perceived latency when moving cards
- optimistic UI updates vs server confirmation
- realtime sync with other users

This repo appears to use SignalR for realtime updates; that’s good UX, but:
- multi-instance deployments require a backplane
- offline support is not obvious

## 6) UX recommendations (prioritized)

### P1
- Implement explicit 429 handling and a “cooldown UX”.
- Add better error messaging for common backend errors:
  - Conflict (stale update)
  - Forbidden (board access)
  - ValidationError (import parsing)

### P2
- Add skeleton loaders for board list and board view.
- Add “empty state” guidance for capture inbox and automation history.

### P3
- Add accessibility checks in E2E (axe).
- Add keyboard shortcut discoverability (cheat sheet modal).
