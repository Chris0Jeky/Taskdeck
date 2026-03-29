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

  it('emits error with string message when handler throws non-Error value', async () => {
    const trace = buildTrace([{ label: 'Bad', offsetMs: 0 }])
    const states: ReplayState[] = []
    const engine = createReplayEngine(trace)
    engine.onAction(() => {
      throw 'string error'
    })
    engine.onStateChange((s) => states.push({ ...s }))

    engine.play()
    await vi.advanceTimersByTimeAsync(0)

    const errorState = states.find(s => s.status === 'error')
    expect(errorState).toBeDefined()
    expect(errorState!.error).toBe('string error')
  })

  it('restarts from beginning when play is called after completion', async () => {
    const trace = buildTrace([
      { label: 'A', offsetMs: 0 },
    ])

    const executed: string[] = []
    const states: ReplayState[] = []
    const engine = createReplayEngine(trace)
    engine.onAction((action) => executed.push(action.label))
    engine.onStateChange((s) => states.push({ ...s }))

    // Play to completion
    engine.play()
    await vi.advanceTimersByTimeAsync(0)
    expect(states[states.length - 1].status).toBe('completed')

    // Play again after completion - should restart
    executed.length = 0
    engine.play()
    await vi.advanceTimersByTimeAsync(0)
    expect(executed).toContain('A')
  })

  it('seekTo pauses playback when seeking during active play', async () => {
    const trace = buildTrace([
      { label: 'A', offsetMs: 0 },
      { label: 'B', offsetMs: 100 },
      { label: 'C', offsetMs: 200 },
    ])

    const engine = createReplayEngine(trace)
    engine.onAction(() => {})

    engine.play()
    await vi.advanceTimersByTimeAsync(0)
    // Engine is now playing, currentIndex = 1

    engine.seekTo(2)
    expect(engine.getState().status).toBe('paused')
    expect(engine.getState().currentIndex).toBe(2)
  })

  it('pause is a no-op when not playing', () => {
    const trace = buildTrace([{ label: 'A', offsetMs: 0 }])
    const engine = createReplayEngine(trace)

    // Pause from idle - should be a no-op
    engine.pause()
    expect(engine.getState().status).toBe('idle')
  })

  it('reports elapsed time based on last executed action', async () => {
    const trace = buildTrace([
      { label: 'A', offsetMs: 50 },
      { label: 'B', offsetMs: 200 },
    ])

    const engine = createReplayEngine(trace)
    engine.onAction(() => {})

    engine.play()
    await vi.advanceTimersByTimeAsync(50)
    // After executing action 0 (offsetMs=50), currentIndex should be 1
    const state = engine.getState()
    expect(state.elapsedMs).toBe(50)
  })

  it('respects playback speed for action timing', async () => {
    const trace = buildTrace([
      { label: 'A', offsetMs: 0 },
      { label: 'B', offsetMs: 200 },
    ])

    const executed: string[] = []
    const engine = createReplayEngine(trace)
    engine.onAction((action) => executed.push(action.label))
    engine.setSpeed(2) // 2x speed

    engine.play()
    await vi.advanceTimersByTimeAsync(0)
    expect(executed).toEqual(['A'])

    // At 2x speed, 200ms delay becomes 100ms
    await vi.advanceTimersByTimeAsync(100)
    expect(executed).toEqual(['A', 'B'])
  })

  it('seekTo preserves current status when not playing', () => {
    const trace = buildTrace([
      { label: 'A', offsetMs: 0 },
      { label: 'B', offsetMs: 100 },
    ])

    const engine = createReplayEngine(trace)
    // Seek from idle - status should remain idle
    engine.seekTo(1)
    expect(engine.getState().status).toBe('idle')
    expect(engine.getState().currentIndex).toBe(1)
  })

  it('stop clears pending timer during active playback', async () => {
    const trace = buildTrace([
      { label: 'A', offsetMs: 0 },
      { label: 'B', offsetMs: 500 },
    ])

    const executed: string[] = []
    const engine = createReplayEngine(trace)
    engine.onAction((action) => executed.push(action.label))

    engine.play()
    await vi.advanceTimersByTimeAsync(0)
    expect(executed).toEqual(['A'])

    // Stop while waiting for B
    engine.stop()
    await vi.advanceTimersByTimeAsync(600)
    expect(executed).toEqual(['A']) // B should not execute
    expect(engine.getState().status).toBe('idle')
    expect(engine.getState().currentIndex).toBe(0)
  })

  it('calls multiple registered action handlers', async () => {
    const trace = buildTrace([{ label: 'A', offsetMs: 0 }])

    const handler1Calls: string[] = []
    const handler2Calls: string[] = []
    const engine = createReplayEngine(trace)
    engine.onAction((action) => handler1Calls.push(action.label))
    engine.onAction((action) => handler2Calls.push(action.label))

    engine.play()
    await vi.advanceTimersByTimeAsync(0)

    expect(handler1Calls).toEqual(['A'])
    expect(handler2Calls).toEqual(['A'])
  })

  it('resume from paused continues remaining actions', async () => {
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

    // Resume
    engine.play()
    await vi.advanceTimersByTimeAsync(0)
    expect(executed).toContain('B')
  })

  it('seekTo while paused stays paused', async () => {
    const trace = buildTrace([
      { label: 'A', offsetMs: 0 },
      { label: 'B', offsetMs: 100 },
      { label: 'C', offsetMs: 200 },
    ])

    const engine = createReplayEngine(trace)
    engine.onAction(() => {})

    engine.play()
    await vi.advanceTimersByTimeAsync(0)
    engine.pause()
    expect(engine.getState().status).toBe('paused')

    engine.seekTo(0)
    expect(engine.getState().status).toBe('paused')
    expect(engine.getState().currentIndex).toBe(0)
  })

  it('buildState reports zero elapsed when no actions executed', () => {
    const trace = buildTrace([{ label: 'A', offsetMs: 100 }])
    const engine = createReplayEngine(trace)

    const state = engine.getState()
    expect(state.elapsedMs).toBe(0)
    expect(state.currentIndex).toBe(0)
  })
})
