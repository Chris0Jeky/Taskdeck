# Frontend Performance & Responsiveness Playbook
Date: 2026-02-23
Status: Draft

Premium feel requires low interaction latency and stable frame rendering.

---

## Top performance risks in Taskdeck
- large lists (cards, activity logs, inbox items)
- reactive churn from large state objects
- drag-and-drop reflows
- expensive re-render cascades (AppShell, BoardView)

Vue’s performance guide explicitly recommends list virtualization for large lists.  
https://vuejs.org/guide/best-practices/performance

---

## Performance targets (practical)
- Modal open time: <100ms perceived (no network)
- Card drag interactions: no dropped frames during drag
- Board load: visible skeleton within 200ms; full load ASAP
- Inbox list: pagination + virtualization if needed

---

## Concrete techniques (high ROI)

### 1) Virtualize large lists
Options:
- TanStack Virtual (headless, framework agnostic)  
  https://tanstack.com/virtual/latest/docs/introduction
- vue-virtual-scroller  
  https://github.com/Akryum/vue-virtual-scroller

Rule of thumb:
- if you can render >200 items in a lane/list, virtualize.

### 2) Split and lazy-load routes
- Use Vue Router lazy routes for heavy screens (ops console, logs).
- Keep AppShell lightweight.

### 3) Reduce reactive depth
- Use `shallowRef` / `markRaw` for large immutable objects (only when needed).
- Avoid deep watchers on big objects.
- Prefer computed selectors.

### 4) Avoid layout thrash in drag
- use `transform` for movement when possible
- keep DOM stable during drag
- avoid expensive box-shadow changes on large lists

### 5) Cache server-state carefully
If API fetching logic grows:
- Consider TanStack Query for caching, stale times, and retries.  
  https://tanstack.com/query/v4/docs/vue/overview

---

## Measurement (do this early)
- Add lightweight “performance marks” in key interactions:
  - board load start/end
  - modal open
  - proposal diff render
- Use browser performance panel and track regressions.

---

## CI performance gates (future)
You can later add:
- bundle size budgets
- lighthouse CI
- Playwright “interaction timing” approximations

Do not add these until you have stable baseline behavior.
