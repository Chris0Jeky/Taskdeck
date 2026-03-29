import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'
import { usePerformanceMark, PERF_BUDGETS } from '../../composables/usePerformanceMark'

describe('usePerformanceMark', () => {
  // happy-dom provides a minimal performance API; spy on it for verification
  let markSpy: ReturnType<typeof vi.spyOn>
  let measureSpy: ReturnType<typeof vi.spyOn>
  let clearMarksSpy: ReturnType<typeof vi.spyOn>
  let clearMeasuresSpy: ReturnType<typeof vi.spyOn>

  beforeEach(() => {
    markSpy = vi.spyOn(performance, 'mark')
    clearMarksSpy = vi.spyOn(performance, 'clearMarks')
    clearMeasuresSpy = vi.spyOn(performance, 'clearMeasures')
    measureSpy = vi.spyOn(performance, 'measure')
  })

  afterEach(() => {
    vi.restoreAllMocks()
    // Clean up any marks/measures created during tests
    performance.clearMarks()
    performance.clearMeasures()
  })

  it('returns reactive duration and overBudget refs', () => {
    const perf = usePerformanceMark('test-interaction')
    expect(perf.duration.value).toBeNull()
    expect(perf.overBudget.value).toBeNull()
    expect(typeof perf.start).toBe('function')
    expect(typeof perf.end).toBe('function')
  })

  it('places start and end marks with td: prefix', () => {
    const perf = usePerformanceMark('board-load')
    perf.start()
    expect(markSpy).toHaveBeenCalledWith('td:board-load:start')

    perf.end()
    expect(markSpy).toHaveBeenCalledWith('td:board-load:end')
  })

  it('clears stale marks on start', () => {
    const perf = usePerformanceMark('board-load')
    perf.start()
    expect(clearMarksSpy).toHaveBeenCalledWith('td:board-load:start')
    expect(clearMarksSpy).toHaveBeenCalledWith('td:board-load:end')
    expect(clearMeasuresSpy).toHaveBeenCalledWith('td:board-load')
  })

  it('creates a measure and populates duration', () => {
    const perf = usePerformanceMark('board-load')
    perf.start()
    perf.end()

    expect(measureSpy).toHaveBeenCalledWith('td:board-load', 'td:board-load:start', 'td:board-load:end')
    expect(perf.duration.value).toBeTypeOf('number')
    expect(perf.duration.value).toBeGreaterThanOrEqual(0)
  })

  it('reports overBudget when exceeding custom budget', () => {
    // Use a budget of 0ms so the measurement is guaranteed to exceed it
    const perf = usePerformanceMark('custom-op', 0)
    perf.start()
    perf.end()

    expect(perf.overBudget.value).toBe(true)
  })

  it('reports overBudget false when within budget', () => {
    // Use a very large budget so the near-instant measurement is within it
    const perf = usePerformanceMark('custom-op', 999999)
    perf.start()
    perf.end()

    expect(perf.overBudget.value).toBe(false)
  })

  it('uses PERF_BUDGETS lookup when no custom budget provided', () => {
    const perf = usePerformanceMark('board-load')
    perf.start()
    perf.end()

    // The measurement was near-instant, so it should be within the 500ms budget
    expect(perf.overBudget.value).toBe(false)
  })

  it('returns null overBudget when name has no budget and none provided', () => {
    const perf = usePerformanceMark('unknown-interaction-no-budget')
    perf.start()
    perf.end()

    expect(perf.duration.value).toBeTypeOf('number')
    expect(perf.overBudget.value).toBeNull()
  })

  it('handles end() without start() gracefully', () => {
    const perf = usePerformanceMark('no-start')
    // Should not throw
    perf.end()
    expect(perf.duration.value).toBeNull()
    expect(perf.overBudget.value).toBeNull()
  })

  it('logs a dev error when end() runs without a start mark', () => {
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {})
    const perf = usePerformanceMark('no-start')

    perf.end()

    expect(errorSpy).toHaveBeenCalledTimes(1)
    expect(errorSpy.mock.calls[0][0]).toContain("usePerformanceMark('no-start').end()")
  })

  it('exports PERF_BUDGETS with expected keys', () => {
    expect(PERF_BUDGETS).toHaveProperty('route-transition')
    expect(PERF_BUDGETS).toHaveProperty('board-load')
    expect(PERF_BUDGETS).toHaveProperty('inbox-load')
    expect(PERF_BUDGETS).toHaveProperty('review-load')
    expect(PERF_BUDGETS).toHaveProperty('home-load')
    expect(PERF_BUDGETS).toHaveProperty('modal-open')
    expect(PERF_BUDGETS).toHaveProperty('proposal-diff-render')

    // All budgets should be positive numbers
    for (const [key, value] of Object.entries(PERF_BUDGETS)) {
      expect(value, `${key} budget`).toBeTypeOf('number')
      expect(value, `${key} budget`).toBeGreaterThan(0)
    }
  })

  it('logs a warning when over budget', () => {
    const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const perf = usePerformanceMark('test-warn', 0)
    perf.start()
    perf.end()

    expect(warnSpy).toHaveBeenCalledTimes(1)
    expect(warnSpy.mock.calls[0][0]).toContain('[perf]')
    expect(warnSpy.mock.calls[0][0]).toContain('test-warn')
  })
})
