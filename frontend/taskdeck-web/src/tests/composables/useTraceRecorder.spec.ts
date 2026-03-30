import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'
import { useTraceRecorder } from '../../composables/useTraceRecorder'

describe('useTraceRecorder', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('starts in a non-recording state', () => {
    const recorder = useTraceRecorder()
    expect(recorder.isRecording.value).toBe(false)
    expect(recorder.currentTrace.value).toBeNull()
    expect(recorder.actionCount.value).toBe(0)
  })

  it('starts recording with a name', () => {
    const recorder = useTraceRecorder()
    recorder.start('Test Trace')
    expect(recorder.isRecording.value).toBe(true)
    expect(recorder.currentTrace.value).not.toBeNull()
    expect(recorder.currentTrace.value!.name).toBe('Test Trace')
    expect(recorder.currentTrace.value!.actions).toHaveLength(0)
  })

  it('records actions while recording', () => {
    const recorder = useTraceRecorder()
    recorder.start('Test Trace')

    vi.advanceTimersByTime(100)
    recorder.recordAction({
      type: 'route-navigation',
      label: 'Navigate to home',
      payload: { from: '/login', to: '/workspace/home' },
    })

    expect(recorder.currentTrace.value!.actions).toHaveLength(1)
    expect(recorder.actionCount.value).toBe(1)

    const action = recorder.currentTrace.value!.actions[0]
    expect(action.type).toBe('route-navigation')
    expect(action.label).toBe('Navigate to home')
    expect(action.offsetMs).toBeGreaterThanOrEqual(0)
  })

  it('ignores recordAction when not recording', () => {
    const recorder = useTraceRecorder()
    recorder.recordAction({
      type: 'click',
      label: 'Click button',
      payload: { selector: '#btn' },
    })
    expect(recorder.actionCount.value).toBe(0)
  })

  it('stops recording and returns completed trace', () => {
    const recorder = useTraceRecorder()
    recorder.start('Test Trace')

    recorder.recordAction({
      type: 'click',
      label: 'Click button',
      payload: { selector: '#btn' },
    })

    vi.advanceTimersByTime(500)

    const trace = recorder.stop()
    expect(trace).not.toBeNull()
    expect(trace!.name).toBe('Test Trace')
    expect(trace!.actions).toHaveLength(1)
    expect(trace!.endedAt).not.toBeNull()
    expect(trace!.durationMs).toBeGreaterThanOrEqual(0)

    // After stop, state is reset
    expect(recorder.isRecording.value).toBe(false)
    expect(recorder.currentTrace.value).toBeNull()
    expect(recorder.actionCount.value).toBe(0)
  })

  it('returns null when stopping without recording', () => {
    const recorder = useTraceRecorder()
    const trace = recorder.stop()
    expect(trace).toBeNull()
  })

  it('warns when starting while already recording', () => {
    const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const recorder = useTraceRecorder()
    recorder.start('Trace 1')
    recorder.start('Trace 2')
    expect(warnSpy).toHaveBeenCalledOnce()
    expect(recorder.currentTrace.value!.name).toBe('Trace 1')
    warnSpy.mockRestore()
  })

  it('includes metadata in the trace', () => {
    const recorder = useTraceRecorder()
    recorder.start('Test Trace')
    expect(recorder.currentTrace.value!.metadata).toBeDefined()
  })

  it('records multiple actions with increasing offsets', () => {
    const recorder = useTraceRecorder()
    recorder.start('Multi Action')

    vi.advanceTimersByTime(100)
    recorder.recordAction({ type: 'click', label: 'First', payload: { selector: '#a' } })

    vi.advanceTimersByTime(200)
    recorder.recordAction({ type: 'click', label: 'Second', payload: { selector: '#b' } })

    const actions = recorder.currentTrace.value!.actions
    expect(actions).toHaveLength(2)
    expect(actions[1].offsetMs).toBeGreaterThanOrEqual(actions[0].offsetMs)
  })

  it('generates unique trace ids', () => {
    const recorder1 = useTraceRecorder()
    recorder1.start('Trace A')
    const id1 = recorder1.currentTrace.value!.id
    recorder1.stop()

    const recorder2 = useTraceRecorder()
    recorder2.start('Trace B')
    const id2 = recorder2.currentTrace.value!.id
    recorder2.stop()

    expect(id1).not.toBe(id2)
  })

  it('updates durationMs on each recorded action', () => {
    const recorder = useTraceRecorder()
    recorder.start('Duration Test')

    vi.advanceTimersByTime(100)
    recorder.recordAction({ type: 'click', label: 'A', payload: { selector: '#a' } })
    expect(recorder.currentTrace.value!.durationMs).toBeGreaterThanOrEqual(0)

    vi.advanceTimersByTime(200)
    recorder.recordAction({ type: 'click', label: 'B', payload: { selector: '#b' } })
    expect(recorder.currentTrace.value!.durationMs).toBeGreaterThanOrEqual(recorder.currentTrace.value!.actions[0].offsetMs)
  })

  it('sets endedAt timestamp on stop', () => {
    const recorder = useTraceRecorder()
    recorder.start('End Test')

    vi.advanceTimersByTime(50)
    const trace = recorder.stop()
    expect(trace).not.toBeNull()
    expect(trace!.endedAt).not.toBeNull()
    expect(new Date(trace!.endedAt!).getTime()).toBeGreaterThan(0)
  })

  it('generates unique action ids', () => {
    const recorder = useTraceRecorder()
    recorder.start('Unique Actions')

    recorder.recordAction({ type: 'click', label: 'A', payload: { selector: '#a' } })
    recorder.recordAction({ type: 'click', label: 'B', payload: { selector: '#b' } })

    const ids = recorder.currentTrace.value!.actions.map(a => a.id)
    expect(new Set(ids).size).toBe(ids.length)
  })

  it('metadata contains expected browser fields', () => {
    const recorder = useTraceRecorder()
    recorder.start('Metadata Test')

    const metadata = recorder.currentTrace.value!.metadata
    expect(metadata).toBeDefined()
    // In happy-dom environment, these should be defined
    expect(metadata!.userAgent).toBeDefined()
    expect(metadata!.screenWidth).toBeDefined()
    expect(metadata!.screenHeight).toBeDefined()
  })

  it('records action types correctly', () => {
    const recorder = useTraceRecorder()
    recorder.start('Type Test')

    recorder.recordAction({ type: 'route-navigation', label: 'Nav', payload: { to: '/home' } })
    recorder.recordAction({ type: 'store-action', label: 'Action', payload: { store: 'boardStore' } })
    recorder.recordAction({ type: 'click', label: 'Click', payload: { selector: '#btn' } })

    const actions = recorder.currentTrace.value!.actions
    expect(actions[0].type).toBe('route-navigation')
    expect(actions[1].type).toBe('store-action')
    expect(actions[2].type).toBe('click')
  })
})
