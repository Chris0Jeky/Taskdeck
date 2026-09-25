/**
 * Trace replay engine for re-executing recorded action sequences.
 * Internal tooling — used for demo playback and test analysis.
 *
 * The replay engine processes a Trace and emits actions with timing,
 * supporting play/pause/stop and adjustable playback speed.
 */

import type { Trace, TraceAction, ReplayState, ReplayStatus } from '../types/trace'

export type ReplayActionHandler = (action: TraceAction, index: number) => void | Promise<void>

export interface TraceReplayEngine {
  /** Current replay state. */
  getState: () => ReplayState
  /** Start or resume playback. */
  play: () => void
  /** Pause after any already-running action settles, without starting a successor. */
  pause: () => void
  /** Stop playback and reset to the beginning. */
  stop: () => void
  /** Jump to a specific action index. */
  seekTo: (index: number) => void
  /** Set playback speed multiplier (e.g. 0.5 = half speed, 2 = double). */
  setSpeed: (speed: number) => void
  /** Register a handler called for each action during playback. */
  onAction: (handler: ReplayActionHandler) => void
  /** Register a handler called when replay state changes. */
  onStateChange: (handler: (state: ReplayState) => void) => void
  /** Permanently dispose timers/handlers and invalidate pending settlements. */
  dispose: () => void
}

export function createReplayEngine(trace: Trace): TraceReplayEngine {
  let status: ReplayStatus = 'idle'
  let currentIndex = 0
  let playbackSpeed = 1
  let timerId: ReturnType<typeof setTimeout> | null = null
  let generation = 0
  let stateRevision = 0
  let activeExecution: object | null = null
  let disposed = false

  const actionHandlers: ReplayActionHandler[] = []
  const stateHandlers: Array<(state: ReplayState) => void> = []

  function buildState(): ReplayState {
    return {
      status,
      currentIndex,
      totalActions: trace.actions.length,
      elapsedMs: currentIndex > 0 && currentIndex <= trace.actions.length
        ? trace.actions[currentIndex - 1].offsetMs
        : 0,
      playbackSpeed,
    }
  }

  function emitStateChange(error?: string): void {
    const revision = ++stateRevision
    const state = buildState()
    if (error !== undefined) state.error = error
    for (const handler of stateHandlers) {
      // Observers can synchronously stop, seek, pause, or dispose playback.
      if (disposed || stateRevision !== revision) return
      handler(state)
    }
  }

  function ownsPlayback(ownerGeneration: number): boolean {
    return !disposed && generation === ownerGeneration
  }

  function clearTimer(): void {
    if (timerId !== null) clearTimeout(timerId)
    timerId = null
  }

  function invalidatePlayback(): void {
    clearTimer()
    generation += 1
    activeExecution = null
  }

  function scheduleAction(index: number, delay: number, ownerGeneration: number): void {
    if (!ownsPlayback(ownerGeneration) || status !== 'playing' || timerId !== null || activeExecution) return
    timerId = setTimeout(() => {
      if (!ownsPlayback(ownerGeneration)) return
      timerId = null
      void executeAction(index, ownerGeneration)
    }, Math.max(0, delay))
  }

  async function executeAction(index: number, ownerGeneration: number): Promise<void> {
    if (!ownsPlayback(ownerGeneration) || status !== 'playing') return
    const owner = {}
    activeExecution = owner
    const action = trace.actions[index]
    for (const handler of actionHandlers) {
      try {
        await handler(action, index)
      } catch (err) {
        if (!ownsPlayback(ownerGeneration) || activeExecution !== owner) return
        activeExecution = null
        status = 'error'
        emitStateChange(err instanceof Error ? err.message : String(err))
        return
      }
      // Stop/seek/dispose cannot cancel the handler's external work, but they do
      // revoke its authority to deliver more handlers or commit replay state.
      if (!ownsPlayback(ownerGeneration) || activeExecution !== owner) return
    }

    currentIndex = index + 1
    emitStateChange()
    if (!ownsPlayback(ownerGeneration) || activeExecution !== owner) return
    activeExecution = null
    if (status !== 'playing') return

    if (currentIndex < trace.actions.length) {
      const delay = (trace.actions[currentIndex].offsetMs - action.offsetMs) / playbackSpeed
      scheduleAction(currentIndex, delay, ownerGeneration)
    } else {
      status = 'completed'
      emitStateChange()
    }
  }

  function play(): void {
    if (disposed || status === 'playing') return
    if (trace.actions.length === 0) {
      status = 'completed'
      emitStateChange()
      return
    }
    if (status === 'completed') currentIndex = 0

    const ownerGeneration = generation
    status = 'playing'
    emitStateChange()
    if (!ownsPlayback(ownerGeneration) || status !== 'playing' || activeExecution) return

    if (currentIndex < trace.actions.length) {
      const delay = currentIndex === 0 ? trace.actions[currentIndex].offsetMs / playbackSpeed : 0
      scheduleAction(currentIndex, delay, ownerGeneration)
    } else {
      status = 'completed'
      emitStateChange()
    }
  }

  function pause(): void {
    if (disposed || status !== 'playing') return
    clearTimer()
    status = 'paused'
    emitStateChange()
  }

  function stop(): void {
    if (disposed) return
    invalidatePlayback()
    status = 'idle'
    currentIndex = 0
    emitStateChange()
  }

  function seekTo(index: number): void {
    if (disposed || !Number.isInteger(index) || index < 0 || index >= trace.actions.length) return
    invalidatePlayback()
    currentIndex = index
    // A completed/error cursor must become resumable at the selected index.
    if (status !== 'idle') status = 'paused'
    emitStateChange()
  }

  function setSpeed(speed: number): void {
    if (disposed || !Number.isFinite(speed) || speed <= 0) return
    playbackSpeed = speed
    emitStateChange()
  }

  function onAction(handler: ReplayActionHandler): void {
    if (!disposed) actionHandlers.push(handler)
  }

  function onStateChange(handler: (state: ReplayState) => void): void {
    if (!disposed) stateHandlers.push(handler)
  }

  function dispose(): void {
    if (disposed) return
    disposed = true
    invalidatePlayback()
    actionHandlers.length = 0
    stateHandlers.length = 0
  }

  return {
    getState: buildState,
    play,
    pause,
    stop,
    seekTo,
    setSpeed,
    onAction,
    onStateChange,
    dispose,
  }
}
