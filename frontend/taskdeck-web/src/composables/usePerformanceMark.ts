/**
 * Lightweight performance instrumentation composable.
 *
 * Uses the standard Performance API (performance.mark / performance.measure)
 * to record interaction timing for key Taskdeck surfaces. Marks are
 * zero-cost when no observer is attached and degrade gracefully in
 * environments without Performance API support.
 *
 * Usage:
 *   const perf = usePerformanceMark('board-load')
 *   perf.start()          // places a start mark
 *   await fetchBoard(id)
 *   perf.end()            // places an end mark + creates a measure
 *   console.log(perf.duration.value) // elapsed ms (or null)
 */

import { ref, type Ref } from 'vue'

/** Interaction latency budgets (milliseconds). */
export const PERF_BUDGETS: Record<string, number> = {
  /** Route transition (navigation start to afterEach). */
  'route-transition': 300,
  /** Board data fetch and render. */
  'board-load': 500,
  /** Inbox surface load (fetch + virtual list ready). */
  'inbox-load': 400,
  /** Review surface load (proposals fetch + render). */
  'review-load': 400,
  /** Home summary load. */
  'home-load': 400,
  /** Modal open animation (generic). */
  'modal-open': 150,
  /** Proposal diff render. */
  'proposal-diff-render': 200,
}

export interface UsePerformanceMarkReturn {
  /** Place the start mark. */
  start: () => void
  /** Place the end mark and record the measure. */
  end: () => void
  /** The measured duration in ms, or null if not yet measured. */
  duration: Ref<number | null>
  /** Whether the measured duration exceeded its budget (null if no budget or no measurement). */
  overBudget: Ref<boolean | null>
}

/**
 * Returns helpers to bracket a named interaction with performance marks.
 *
 * @param name - A key from PERF_BUDGETS (or any custom string).
 * @param budgetMs - Override the default budget for this name.
 */
export function usePerformanceMark(
  name: string,
  budgetMs?: number,
): UsePerformanceMarkReturn {
  const duration = ref<number | null>(null)
  const overBudget = ref<boolean | null>(null)

  const startMark = `td:${name}:start`
  const endMark = `td:${name}:end`
  const measureName = `td:${name}`

  const hasPerf =
    typeof performance !== 'undefined' &&
    typeof performance.mark === 'function' &&
    typeof performance.measure === 'function'

  function start() {
    if (!hasPerf) return
    // Clear any stale marks from a previous run of the same name
    performance.clearMarks(startMark)
    performance.clearMarks(endMark)
    performance.clearMeasures(measureName)
    performance.mark(startMark)
  }

  function end() {
    if (!hasPerf) return
    performance.mark(endMark)
    try {
      const measure = performance.measure(measureName, startMark, endMark)
      duration.value = measure.duration
      const budget = budgetMs ?? PERF_BUDGETS[name]
      overBudget.value = budget != null ? measure.duration > budget : null

      if (overBudget.value) {
        console.warn(
          `[perf] "${name}" took ${measure.duration.toFixed(1)}ms (budget: ${budget}ms)`,
        )
      }
    } catch {
      // Missing start mark — caller error, but don't crash
      duration.value = null
      overBudget.value = null
    }
  }

  return { start, end, duration, overBudget }
}
