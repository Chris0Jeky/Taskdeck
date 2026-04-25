# PAPER-04 · Home / Reset surface

Part of the Paper overhaul (master tracker: PAPER-00). **Blocked by:** PAPER-01, PAPER-02, PAPER-03.

## Goal

Re-skin `HomeView` as the morning-reset surface — serif italic greeting, queued items, quick capture.

## Spec
- Greeting in serif italic 36–44px ("Good morning, Daniel."), period auto-derived from local clock.
- `tk-lede` subtitle summarizing today's queue count + focus block reminder.
- Three "queued for you" cards (proposals + carry-overs) styled as Paper cards with serial, serif title, mono metadata.
- Quick capture single-line at the bottom: serif italic placeholder, hairline border, ⌘; hint.

## Implementation
- Create `frontend/taskdeck-web/src/views/paper/PaperHomeView.vue`.
- Modify `HomeView.vue` to delegate when paper mode is on (lazy-loaded).
- Reuse existing home-queue composable; do not refetch.
- Quick capture wires through existing capture store.

## Tests
- vitest: greeting picks correct period from a fixed clock.
- vitest: empty queue shows the empty state primitive.
- Playwright: smoke load with seeded user.

## Adversarial review
- [ ] Greeting reads naturally with no first name (fallback "Hello").
- [ ] Quick capture doesn't dispatch on empty Enter.
- [ ] Cards keep ember accent only on the proposal subset.
- [ ] No layout shift when queue resolves.
