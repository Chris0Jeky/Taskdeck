# Frontend Performance Budgets

Last updated: 2026-07-14

## Interaction Latency Budgets

These budgets define the maximum acceptable latency for premium-critical frontend interactions. They are enforced via lightweight `performance.mark()`/`performance.measure()` instrumentation and logged as console warnings when exceeded.

| Interaction | Budget (ms) | Scope |
|---|---|---|
| Route transition | 300 | Navigation start (beforeEach) to completion (afterEach) |
| Board load | 500 | Mount to board data fetched + realtime connected |
| Inbox load | 400 | Mount to capture items fetched + virtual list ready |
| Review load | 400 | Mount to proposals fetched and rendered |
| Home load | 400 | Mount to home summary fetched |
| Modal open | 150 | Trigger to modal visible (generic target) |
| Proposal diff render | 200 | Diff request to diff text rendered |

Budget values are defined in `frontend/taskdeck-web/src/composables/usePerformanceMark.ts` as the `PERF_BUDGETS` constant.

## Instrumentation

### Composable: `usePerformanceMark`

Located at `frontend/taskdeck-web/src/composables/usePerformanceMark.ts`.

```ts
import { usePerformanceMark } from '../composables/usePerformanceMark'

const perf = usePerformanceMark('board-load')
perf.start()        // places performance.mark('td:board-load:start')
await fetchBoard()
perf.end()           // places end mark, creates measure, checks budget
console.log(perf.duration.value)    // elapsed ms
console.log(perf.overBudget.value)  // true/false/null
```

All marks use the `td:` prefix (e.g., `td:board-load:start`, `td:board-load:end`) so they are easy to filter in browser DevTools Performance tab or via `PerformanceObserver`.

### Instrumented Surfaces

| Surface | File | Mark name |
|---|---|---|
| Route transitions | `src/router/index.ts` | `route-transition` |
| Board view | `src/views/BoardView.vue` | `board-load` |
| Inbox view | `src/views/InboxView.vue` | `inbox-load` |
| Review view | `src/views/ReviewView.vue` | `review-load` |
| Review diff toggle | `src/views/ReviewView.vue` | `proposal-diff-render` |
| Home view | `src/views/HomeView.vue` | `home-load` |
| Capture modal | `src/components/common/CaptureModal.vue` | `modal-open` |

### Observing in DevTools

1. Open Chrome/Edge DevTools > Performance tab
2. Record a session while navigating through the app
3. In the Timings lane, look for entries prefixed with `td:`
4. Alternatively, use the console: `performance.getEntriesByType('measure').filter(e => e.name.startsWith('td:'))`

### Programmatic Observation

```js
const observer = new PerformanceObserver((list) => {
  for (const entry of list.getEntries()) {
    if (entry.name.startsWith('td:')) {
      console.log(`${entry.name}: ${entry.duration.toFixed(1)}ms`)
    }
  }
})
observer.observe({ type: 'measure', buffered: true })
```

## Optimizations

### Lazy Route Splitting

All workspace views are lazy-loaded via dynamic `import()` in the router (`src/router/index.ts`). Only `LoginView` and `RegisterView` are eagerly loaded since they are the entry points for unauthenticated users. This reduces the initial JS bundle size and speeds up first paint.

**Before**: All 18 view components bundled eagerly in the main chunk.
**After**: Only 2 views (Login, Register) in the main chunk; 16 views loaded on demand.

### Reactive Depth Control

Large lists in Inbox and Activity views already use `@tanstack/vue-virtual` via the `useVirtualList` composable, limiting the number of reactive DOM nodes to the visible window plus overscan. This prevents Vue reactivity overhead from scaling linearly with list size.

## CI Enforcement (Performance Regression Gate)

The performance regression gate runs in ci-extended (label: `performance`) and nightly CI. It enforces two categories of budgets.

### k6 API Thresholds

Enforced via `tests/load/k6/board-heavy-load.js` thresholds:

| Metric | Gate (fail) | Warning |
|---|---|---|
| HTTP p95 latency | < 2000 ms | > 1200 ms aspirational target |
| HTTP p99 latency | < 2500 ms | — |
| HTTP error rate | < 1% | > 0.8% (within 20% of gate) |
| Check pass rate | > 99% | — |
| Board-read p95 | < 900 ms | — |
| Board-write p95 (SQLite, 20 VUs) | < 2200 ms | >= 2000 ms measured capacity |

k6 exits non-zero on threshold breach, failing the CI step. The tagged board-write profile measures SQLite's sustained 20-VU capacity at about 2000 ms p95; its 2200 ms hard gate is that measured capacity plus an explicit 10% CI jitter allowance, not an aspirational target. Both reusable workflows independently require a non-empty, parseable k6 summary with `scripts/ci/require-k6-summary.mjs`, so an export failure cannot silently green the lane. The `scripts/ci/check-k6-thresholds.mjs` script parses the k6 JSON summary and emits a near-capacity `::warning` at or above 2000 ms while retaining the aggregate, read, error-rate, and check-rate hard gates.

### Frontend Bundle Size Thresholds

Enforced via `scripts/ci/check-bundle-size.mjs`:

| Metric | Warning | Error |
|---|---|---|
| Entry chunk (index-*.js) | > 120 KB | > 150 KB |
| Largest single chunk | > 200 KB | > 250 KB |
| Total JS size | > 1000 KB | > 1200 KB |

Override thresholds with environment variables: `BUNDLE_MAX_ENTRY_KB`, `BUNDLE_MAX_SINGLE_KB`, `BUNDLE_MAX_TOTAL_JS_KB`, `BUNDLE_WARN_ENTRY_KB`, `BUNDLE_WARN_SINGLE_KB`, `BUNDLE_WARN_TOTAL_JS_KB`.

### Artifacts

Both checks produce JSON reports uploaded as the `performance-regression-gate-results` artifact:
- `bundle-size-report.json` — chunk inventory and threshold results
- `k6-summary.json` — raw k6 metrics
- `k6-threshold-report.json` — parsed threshold analysis

These artifacts enable historical trend tracking when downloaded across runs.

## Verification Workflow

### Automated

```bash
cd frontend/taskdeck-web
npx vitest --run --reporter=verbose   # includes usePerformanceMark tests
npm run typecheck                      # type safety
npm run build                          # production build (verifies lazy splitting)
```

### Manual Regression Check

1. Start the dev server: `npm run dev`
2. Open DevTools console
3. Navigate Home > Inbox > Review > Board
4. Check console for any `[perf]` warnings (indicates budget exceeded)
5. Run `performance.getEntriesByType('measure').filter(e => e.name.startsWith('td:'))` to inspect timings

### Build Output Verification

After `npm run build`, check `dist/assets/` for multiple chunk files — each lazy-loaded view produces a separate chunk. A single monolithic JS file would indicate lazy splitting regression.
