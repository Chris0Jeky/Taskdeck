import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createReplayEngine, type TraceReplayEngine } from '../../utils/traceReplay'
import type { Trace } from '../../types/trace'

function deferred() {
  let resolve!: () => void
  let reject!: (reason: Error) => void
  const promise = new Promise<void>((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}

const engines: TraceReplayEngine[] = []
function createEngine() {
  const trace: Trace = {
    id: 'lifecycle', name: 'Lifecycle', startedAt: '2026-09-21T00:00:00Z',
    endedAt: '2026-09-21T00:00:01Z', durationMs: 200,
    actions: [0, 1, 2].map(index => ({
      id: `a${index}`, type: 'click', timestamp: '2026-09-21T00:00:00Z',
      offsetMs: index * 100, label: `Action ${index}`, payload: {},
    })),
  }
  const engine = createReplayEngine(trace)
  engines.push(engine)
  return engine
}

describe('replay execution ownership', () => {
  beforeEach(() => { vi.useFakeTimers() })
  afterEach(() => {
    for (const engine of engines.splice(0)) engine.dispose()
    vi.useRealTimers()
  })

  it('does not duplicate delivery when Play is pressed twice', async () => {
    const engine = createEngine()
    const executed: string[] = []
    engine.onAction(action => { executed.push(action.id) })
    engine.play()
    engine.play()
    await vi.advanceTimersByTimeAsync(0)
    expect(executed).toEqual(['a0'])
    await vi.advanceTimersByTimeAsync(100)
    expect(executed).toEqual(['a0', 'a1'])
  })

  it('does not let a stopped action advance or emit state after settlement', async () => {
    const engine = createEngine()
    const pending = deferred()
    const stateChanged = vi.fn()
    engine.onAction(() => pending.promise)
    engine.onStateChange(stateChanged)
    engine.play()
    await vi.advanceTimersByTimeAsync(0)
    engine.stop()
    const snapshot = engine.getState()
    stateChanged.mockClear()
    pending.resolve()
    await vi.advanceTimersByTimeAsync(500)
    expect(engine.getState()).toEqual(snapshot)
    expect(stateChanged).not.toHaveBeenCalled()
  })

  it.each(['resolve', 'reject'] as const)(
    'ignores an obsolete action that will %s after a replacement run starts', async (settlement) => {
      const engine = createEngine()
      const pending = deferred()
      const executed: string[] = []
      engine.onAction(action => {
        executed.push(action.id)
        if (executed.length === 1) return pending.promise
      })
      engine.play()
      await vi.advanceTimersByTimeAsync(0)
      engine.stop()
      engine.play()
      await vi.advanceTimersByTimeAsync(0)
      expect(engine.getState().currentIndex).toBe(1)
      if (settlement === 'resolve') pending.resolve()
      else pending.reject(new Error('obsolete run'))
      await vi.advanceTimersByTimeAsync(0)
      expect(engine.getState().status).toBe('playing')
      await vi.advanceTimersByTimeAsync(100)
      expect(executed).toEqual(['a0', 'a0', 'a1'])
    },
  )

  it('preserves a seek target when a previous action settles', async () => {
    const engine = createEngine()
    const pending = deferred()
    engine.onAction(() => pending.promise)
    engine.play()
    await vi.advanceTimersByTimeAsync(0)
    engine.seekTo(2)
    pending.resolve()
    await vi.advanceTimersByTimeAsync(0)
    expect(engine.getState()).toMatchObject({ status: 'paused', currentIndex: 2 })
  })

  it('freezes a disposed engine even when an action is pending', async () => {
    const engine = createEngine()
    const pending = deferred()
    engine.onAction(() => pending.promise)
    engine.play()
    await vi.advanceTimersByTimeAsync(0)
    engine.dispose()
    const snapshot = engine.getState()
    pending.resolve()
    await vi.advanceTimersByTimeAsync(500)
    expect(engine.getState()).toEqual(snapshot)
    expect(vi.getTimerCount()).toBe(0)
  })

  it('does not restart or register handlers after disposal', async () => {
    const engine = createEngine()
    engine.dispose()
    const snapshot = engine.getState()
    const action = vi.fn()
    const state = vi.fn()
    engine.onAction(action)
    engine.onStateChange(state)
    engine.play()
    engine.seekTo(1)
    engine.setSpeed(2)
    engine.stop()
    engine.pause()
    await vi.advanceTimersByTimeAsync(500)
    expect(engine.getState()).toEqual(snapshot)
    expect(action).not.toHaveBeenCalled()
    expect(state).not.toHaveBeenCalled()
    expect(vi.getTimerCount()).toBe(0)
  })

  it('resumes a pending action without invoking its handler again', async () => {
    const engine = createEngine()
    const pending = deferred()
    const executed: string[] = []
    engine.onAction(action => {
      executed.push(action.id)
      if (action.id === 'a0') return pending.promise
    })
    engine.play()
    await vi.advanceTimersByTimeAsync(0)
    engine.pause()
    engine.play()
    await vi.advanceTimersByTimeAsync(0)
    expect(executed).toEqual(['a0'])
    pending.resolve()
    await vi.advanceTimersByTimeAsync(100)
    expect(executed).toEqual(['a0', 'a1'])
  })

  it('lets a paused action finish once without starting the next action', async () => {
    const engine = createEngine()
    const pending = deferred()
    const executed: string[] = []
    engine.onAction(action => {
      executed.push(action.id)
      if (action.id === 'a0') return pending.promise
    })
    engine.play()
    await vi.advanceTimersByTimeAsync(0)
    engine.pause()
    pending.resolve()
    await vi.advanceTimersByTimeAsync(500)
    expect(engine.getState()).toMatchObject({ status: 'paused', currentIndex: 1 })
    expect(executed).toEqual(['a0'])
    engine.play()
    await vi.advanceTimersByTimeAsync(0)
    expect(executed).toEqual(['a0', 'a1'])
  })

  it('stops delivery to the remaining handlers when a handler stops playback', async () => {
    const engine = createEngine()
    const nextHandler = vi.fn()
    engine.onAction(() => engine.stop())
    engine.onAction(nextHandler)
    engine.play()
    await vi.advanceTimersByTimeAsync(0)
    expect(nextHandler).not.toHaveBeenCalled()
    expect(engine.getState()).toMatchObject({ status: 'idle', currentIndex: 0 })
  })

  it('honours a state observer that pauses before the first timer is installed', async () => {
    const engine = createEngine()
    const action = vi.fn()
    engine.onAction(action)
    engine.onStateChange(state => { if (state.status === 'playing') engine.pause() })
    engine.play()
    await vi.advanceTimersByTimeAsync(500)
    expect(action).not.toHaveBeenCalled()
    expect(vi.getTimerCount()).toBe(0)
  })

  it('schedules only one successor after an observer pauses and resumes', async () => {
    const engine = createEngine()
    const executed: string[] = []
    let toggled = false
    engine.onAction(action => { executed.push(action.id) })
    engine.onStateChange(state => {
      if (!toggled && state.currentIndex === 1) {
        toggled = true
        engine.pause()
        engine.play()
      }
    })
    engine.play()
    await vi.advanceTimersByTimeAsync(100)
    expect(executed).toEqual(['a0', 'a1'])
  })

  it('honours a seek after completion instead of restarting at zero', async () => {
    const engine = createEngine()
    const executed: string[] = []
    engine.onAction(action => { executed.push(action.id) })
    engine.play()
    await vi.advanceTimersByTimeAsync(200)
    expect(engine.getState().status).toBe('completed')
    engine.seekTo(1)
    engine.play()
    await vi.advanceTimersByTimeAsync(0)
    expect(executed).toEqual(['a0', 'a1', 'a2', 'a1'])
  })

  it.each([NaN, Infinity, -Infinity, 0.5])('ignores an invalid seek index %s', (index) => {
    const engine = createEngine()
    const snapshot = engine.getState()
    engine.seekTo(index)
    expect(engine.getState()).toEqual(snapshot)
  })

  it.each([NaN, Infinity, -Infinity])('ignores a non-finite speed %s', (speed) => {
    const engine = createEngine()
    engine.setSpeed(speed)
    expect(engine.getState().playbackSpeed).toBe(1)
  })

  it('does not deliver obsolete state to later observers after a reentrant stop', async () => {
    const engine = createEngine()
    const statuses: string[] = []
    engine.onStateChange(state => { if (state.status === 'playing') engine.stop() })
    engine.onStateChange(state => { statuses.push(state.status) })
    engine.play()
    await vi.advanceTimersByTimeAsync(0)
    expect(statuses).toEqual(['idle'])
  })

})
