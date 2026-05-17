import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'

let unmountCallback: (() => void) | null = null

vi.mock('vue', () => ({
  ref: (val: unknown) => ({ value: val }),
  readonly: (r: unknown) => r,
  onBeforeUnmount: (fn: () => void) => { unmountCallback = fn },
}))

const mockDetectReducedMotion = vi.fn(() => false)

vi.mock('../../composables/inkBleedMotion', () => ({
  INK_BLEED_PHASE_SCHEDULE: [
    { at: 0, phase: 'drop' },
    { at: 400, phase: 'bloom' },
    { at: 1400, phase: 'compose' },
    { at: 3400, phase: 'settle' },
    { at: 4200, phase: 'stamp' },
    { at: 4600, phase: 'dried' },
  ],
  INK_BLEED_TOTAL_MS: 4600,
  detectInkBleedReducedMotion: () => mockDetectReducedMotion(),
}))

import { useInkBleed } from '../../composables/useInkBleed'

describe('useInkBleed', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('starts in dried phase', () => {
    const { phase } = useInkBleed()
    expect(phase.value).toBe('dried')
  })

  it('start() transitions to drop phase', () => {
    const { start, phase } = useInkBleed()
    start()
    expect(phase.value).toBe('drop')
  })

  it('advances through phases on schedule', () => {
    const { start, phase } = useInkBleed()
    start()

    expect(phase.value).toBe('drop')

    vi.advanceTimersByTime(400)
    expect(phase.value).toBe('bloom')

    vi.advanceTimersByTime(1000)
    expect(phase.value).toBe('compose')

    vi.advanceTimersByTime(2000)
    expect(phase.value).toBe('settle')

    vi.advanceTimersByTime(800)
    expect(phase.value).toBe('stamp')

    vi.advanceTimersByTime(400)
    expect(phase.value).toBe('dried')
  })

  it('fires onDone when finish() called before schedule ends (early finish)', () => {
    const onDone = vi.fn()
    const { start, finish } = useInkBleed({ onDone })
    const runId = start()

    finish(runId)
    expect(onDone).not.toHaveBeenCalled()

    vi.advanceTimersByTime(4600)
    expect(onDone).toHaveBeenCalledOnce()
  })

  it('fires onDone immediately when finish() called after schedule ends (late finish)', () => {
    const onDone = vi.fn()
    const { start, finish, loop } = useInkBleed({ onDone })
    const runId = start()

    vi.advanceTimersByTime(4600)
    expect(loop.value).toBe(true)
    expect(onDone).not.toHaveBeenCalled()

    finish(runId)
    expect(onDone).toHaveBeenCalledOnce()
    expect(loop.value).toBe(false)
  })

  it('sets loop=true when dried phase is reached without finish()', () => {
    const { start, loop } = useInkBleed()
    start()

    vi.advanceTimersByTime(4600)
    expect(loop.value).toBe(true)
  })

  it('cancel() resets to dried and does not fire onDone', () => {
    const onDone = vi.fn()
    const { start, cancel, phase } = useInkBleed({ onDone })
    start()

    vi.advanceTimersByTime(1000)
    cancel()

    expect(phase.value).toBe('dried')
    vi.advanceTimersByTime(5000)
    expect(onDone).not.toHaveBeenCalled()
  })

  it('start() cancels previous bleed (singleton guard)', () => {
    const onDone = vi.fn()
    const { start, finish } = useInkBleed({ onDone })
    const runId1 = start()

    vi.advanceTimersByTime(1000)
    const runId2 = start()

    finish(runId1)
    vi.advanceTimersByTime(5000)
    expect(onDone).not.toHaveBeenCalled()

    finish(runId2)
    expect(onDone).toHaveBeenCalledOnce()
  })

  it('finish() with wrong runId is a no-op', () => {
    const onDone = vi.fn()
    const { start, finish } = useInkBleed({ onDone })
    start()

    finish(999)
    vi.advanceTimersByTime(5000)
    expect(onDone).not.toHaveBeenCalled()
  })

  it('returns incrementing runIds', () => {
    const { start } = useInkBleed()
    const id1 = start()
    const id2 = start()
    expect(id2).toBeGreaterThan(id1)
  })

  describe('reduced motion', () => {
    beforeEach(() => {
      mockDetectReducedMotion.mockReturnValue(true)
    })

    afterEach(() => {
      mockDetectReducedMotion.mockReturnValue(false)
    })

    it('start() stays in dried phase and skips timers', () => {
      const { start, phase } = useInkBleed()
      start()
      expect(phase.value).toBe('dried')

      vi.advanceTimersByTime(5000)
      expect(phase.value).toBe('dried')
    })

    it('finish() fires onDone immediately in reduced-motion mode', () => {
      const onDone = vi.fn()
      const { start, finish } = useInkBleed({ onDone })
      const runId = start()

      finish(runId)
      expect(onDone).toHaveBeenCalledOnce()
    })
  })

  describe('onBeforeUnmount cleanup', () => {
    it('clears timers and prevents onDone after unmount', () => {
      const onDone = vi.fn()
      const { start, finish } = useInkBleed({ onDone })
      const runId = start()

      vi.advanceTimersByTime(1000)
      unmountCallback?.()

      finish(runId)
      vi.advanceTimersByTime(5000)
      expect(onDone).not.toHaveBeenCalled()
    })
  })
})
