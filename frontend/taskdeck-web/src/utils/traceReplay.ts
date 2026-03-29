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
  /** Pause playback. */
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
  /** Clean up timers. */
  dispose: () => void
}

export function createReplayEngine(trace: Trace): TraceReplayEngine {
  let status: ReplayStatus = 'idle'
  let currentIndex = 0
  let playbackSpeed = 1
  let timerId: ReturnType<typeof setTimeout> | null = null

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

  function emitStateChange(): void {
    const state = buildState()
    for (const handler of stateHandlers) {
      handler(state)
    }
  }

  async function executeAction(index: number): Promise<void> {
    if (index >= trace.actions.length) {
      status = 'completed'
      emitStateChange()
      return
    }

    const action = trace.actions[index]
    for (const handler of actionHandlers) {
      try {
        await handler(action, index)
      } catch (err) {
        status = 'error'
        const state = buildState()
        state.error = err instanceof Error ? err.message : String(err)
        for (const h of stateHandlers) {
          h(state)
        }
        return
      }
    }

    currentIndex = index + 1
    emitStateChange()

    if (status !== 'playing') return

    // Schedule next action based on timing delta
    if (currentIndex < trace.actions.length) {
      const nextAction = trace.actions[currentIndex]
      const delay = (nextAction.offsetMs - action.offsetMs) / playbackSpeed
      timerId = setTimeout(() => executeAction(currentIndex), Math.max(0, delay))
    } else {
      status = 'completed'
      emitStateChange()
    }
  }

  function play(): void {
    if (trace.actions.length === 0) {
      status = 'completed'
      emitStateChange()
      return
    }

    if (status === 'completed') {
      currentIndex = 0
    }

    status = 'playing'
    emitStateChange()

    if (currentIndex < trace.actions.length) {
      const action = trace.actions[currentIndex]
      const delay = currentIndex === 0
        ? action.offsetMs / playbackSpeed
        : 0
      timerId = setTimeout(() => executeAction(currentIndex), Math.max(0, delay))
    }
  }

  function pause(): void {
    if (status !== 'playing') return
    if (timerId !== null) {
      clearTimeout(timerId)
      timerId = null
    }
    status = 'paused'
    emitStateChange()
  }

  function stop(): void {
    if (timerId !== null) {
      clearTimeout(timerId)
      timerId = null
    }
    status = 'idle'
    currentIndex = 0
    emitStateChange()
  }

  function seekTo(index: number): void {
    if (index < 0 || index >= trace.actions.length) return
    const wasPlaying = status === 'playing'
    if (timerId !== null) {
      clearTimeout(timerId)
      timerId = null
    }
    currentIndex = index
    status = wasPlaying ? 'paused' : status
    emitStateChange()
  }

  function setSpeed(speed: number): void {
    if (speed <= 0) return
    playbackSpeed = speed
    emitStateChange()
  }

  function onAction(handler: ReplayActionHandler): void {
    actionHandlers.push(handler)
  }

  function onStateChange(handler: (state: ReplayState) => void): void {
    stateHandlers.push(handler)
  }

  function dispose(): void {
    if (timerId !== null) {
      clearTimeout(timerId)
      timerId = null
    }
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
