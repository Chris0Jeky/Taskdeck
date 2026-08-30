# CLAUDE.md — frontend/taskdeck-web

Vue 3 + Vite + TypeScript + Tailwind SPA (script-setup SFCs, Pinia stores, vue-router,
axios, `@microsoft/signalr` board realtime). Orientation only.

## Invariants
- Review-first: every proposal mutation (approve/reject/defer/execute/dismiss) is gated by
  explicit status-based actionability checks (`composables/useReviewProposals.ts`) + an explicit
  user action. `execute` and `reject` are gated by in-app `TdDialog`s
  (`components/review/ApplyToBoardDialog.vue` / `RejectProposalDialog.vue`), requested through
  `useReviewActions.ts` — only the dialog's accept reaches the API, and High/Critical `reject`
  requires a reason. **No native `prompt`/`confirm`/`alert`** on these surfaces; enforced by
  `src/tests/guards/nativeBrowserDialogs.spec.ts` — a source-text scan over
  `src/views/paper/**`, `src/composables/**`, `src/components/review/**` and
  `src/views/*Review*.vue`, with `useCardModal.ts` quarantined (GH-1997). It proves the literal
  call is absent from those paths, not that the surfaces are dialog-free.
  approve/defer/dismiss are status-gated, not dialog-gated. Never auto-apply.
- Thin-shell decomposition is real for extracted sub-components/modals (e.g. `CardModal.vue`
  191 lines + `useCardModal`), but top-level route shells (`BoardView`/`TodayView`/`HomeView`)
  still run 600-900 total lines (inline template + scoped CSS). Keep new logic in
  composables/stores regardless of file size. `InboxView`/`ReviewView` are ~12-line Paper/Legacy
  switches — follow that pattern for new Paper-skin work.
- Board realtime is per-board via `composables/useBoardRealtime.ts` (auto-reconnect, 30s polling
  fallback), NOT a global connection — check that composable before adding another hub.
- `store/boardStore.ts` is a thin facade over `store/board/*.ts` factories — extend via a new
  factory module + barrel export, not by growing the facade.
- `api/http.ts` centralizes auth, request-id, 401 redirect, and retry — don't bypass it with
  raw axios/fetch.
- `storeToRefs` copies a key **only** when the raw store value is a computed (`value?.effect`) or
  passes `isRef`/`isReactive` — so *any* bare value is omitted from the returned object, not just
  nullish ones: `null`, `undefined`, `5` and `'str'` all destructure as `undefined` while `store.x`
  still reads the real value (`ref(...)` is unaffected). Wrap such state in a `ref`. What is
  pinia-**4**-specific is only the `?.` guard: pinia 3.0.4's `if (value.effect)` *threw* a
  TypeError on a bare `null` instead of skipping it. Zero usages today; the note is preventive.

## Verify
- `npm run typecheck`, `npm run build`
- `npx vitest --run` — full local run can OOM; prefer `--maxWorkers=2` or a targeted spec / `-t`.
- `npx playwright test tests/e2e/<file>.spec.ts --reporter=line` for flow changes.

Seam map: `autodoc/AGENT_INDEX.md`
