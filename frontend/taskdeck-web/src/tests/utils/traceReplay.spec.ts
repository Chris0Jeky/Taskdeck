import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'
import { createReplayEngine } from '../../utils/traceReplay'
import type { Trace, TraceAction, ReplayState } from '../../types/trace'

function buildTrace(actions: Partial<TraceAction>[]): Trace {
  return {
    id: 'test-trace',
    name: 'Test',
    startedAt: new Date().toISOString(),
    endedAt: new Date().toISOString(),
    durationMs: actions.length > 0 ? actions[actions.length - 1].offsetMs ?? 0 : 0,
    actions: actions.map((a, i) => ({
      id: `action-${i}`,
      type: a.type ?? 'click',
      timestamp: new Date().toISOString(),
      offsetMs: a.offsetMs ?? i * 100,
      label: a.label ?? `Action ${i}`,
      payload: a.payload ?? { selector: `#el-${i}` },
    })),
  }
}

describe('createReplayEngine', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('starts in idle state', () => {
    const engine = createReplayEngine(buildTrace([]))
    const state = engine.getState()
    expect(state.status).toBe('idle')
    expect(state.currentIndex).toBe(0)
  })

  it('completes immediately for empty trace', () => {
    const stateChanges: ReplayState[] = []
    const engine = createReplayEngine(buildTrace([]))
    engine.onStateChange((s) => stateChanges.push({ ...s }))
    engine.play()
    expect(stateChanges[stateChanges.length - 1].status).toBe('completed')
  })

  it('executes actions in order', async () => {
    const trace = buildTrace([
      { label: 'First', offsetMs: 0 },
      { label: 'Second', offsetMs: 100 },
      { label: 'Third', offsetMs: 200 },
    ])

    const executed: string[] = []
    const engine = createReplayEngine(trace)
    engine.onAction((action) => {
      executed.push(action.label)
    })

    engine.play()

    // First action executes immediately (offsetMs = 0)
    await vi.advanceTimersByTimeAsync(0)
    expect(executed).toContain('First')

    // Second action after 100ms
    await vi.advanceTimersByTimeAsync(100)
    expect(executed).toContain('Second')

    // Third action after another 100ms
    await vi.advanceTimersByTimeAsync(100)
    expect(executed).toContain('Third')
  })

  it('pauses and resumes', async () => {
    const trace = buildTrace([
      { label: 'A', offsetMs: 0 },
      { label: 'B', offsetMs: 100 },
      { label: 'C', offsetMs: 200 },
    ])

    const executed: string[] = []
    const engine = createReplayEngine(trace)
    engine.onAction((action) => executed.push(action.label))

    engine.play()
    await vi.advanceTimersByTimeAsync(0)
    expect(executed).toEqual(['A'])

    engine.pause()
    expect(engine.getState().status).toBe('paused')

    // Advancing time should not execute more actions while paused
    await vi.advanceTimersByTimeAsync(500)
    expect(executed).toEqual(['A'])
  })

  it('stops and resets to beginning', async () => {
    const trace = buildTrace([
      { label: 'A', offsetMs: 0 },
      { label: 'B', offsetMs: 100 },
    ])

    const engine = createReplayEngine(trace)
    engine.onAction(() => {})

    engine.play()
    await vi.advanceTimersByTimeAsync(0)
    engine.stop()

    const state = engine.getState()
    expect(state.status).toBe('idle')
    expect(state.currentIndex).toBe(0)
  })

  it('handles setSpeed', () => {
    const trace = buildTrace([{ label: 'A', offsetMs: 0 }])
    const engine = createReplayEngine(trace)
    engine.setSpeed(2)
    expect(engine.getState().playbackSpeed).toBe(2)
  })

  it('ignores invalid speed', () => {
    const trace = buildTrace([{ label: 'A', offsetMs: 0 }])
    const engine = createReplayEngine(trace)
    engine.setSpeed(0)
    expect(engine.getState().playbackSpeed).toBe(1) // unchanged
    engine.setSpeed(-1)
    expect(engine.getState().playbackSpeed).toBe(1)
  })

  it('seekTo updates current index', () => {
    const trace = buildTrace([
      { label: 'A', offsetMs: 0 },
      { label: 'B', offsetMs: 100 },
      { label: 'C', offsetMs: 200 },
    ])

    const engine = createReplayEngine(trace)
    engine.seekTo(2)
    expect(engine.getState().currentIndex).toBe(2)
  })

  it('seekTo ignores out-of-bounds index', () => {
    const trace = buildTrace([{ label: 'A', offsetMs: 0 }])
    const engine = createReplayEngine(trace)
    engine.seekTo(-1)
    expect(engine.getState().currentIndex).toBe(0)
    engine.seekTo(5)
    expect(engine.getState().currentIndex).toBe(0)
  })

  it('emits error state when action handler throws', async () => {
    const trace = buildTrace([{ label: 'Bad', offsetMs: 0 }])
    const states: ReplayState[] = []
    const engine = createReplayEngine(trace)
    engine.onAction(() => {
      throw new Error('handler failed')
    })
    engine.onStateChange((s) => states.push({ ...s }))

    engine.play()
    await vi.advanceTimersByTimeAsync(0)

    const errorState = states.find(s => s.status === 'error')
    expect(errorState).toBeDefined()
    expect(errorState!.error).toBe('handler failed')
  })

  it('dispose clears timers and handlers', async () => {
    const trace = buildTrace([
      { label: 'A', offsetMs: 0 },
      { label: 'B', offsetMs: 100 },
    ])

    const executed: string[] = []
    const engine = createReplayEngine(trace)
    engine.onAction((action) => executed.push(action.label))

    engine.play()
    await vi.advanceTimersByTimeAsync(0)
    engine.dispose()

    await vi.advanceTimersByTimeAsync(200)
    expect(executed).toEqual(['A']) // B should not have executed
  })
})
