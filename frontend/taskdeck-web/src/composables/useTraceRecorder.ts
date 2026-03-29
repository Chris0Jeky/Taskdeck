/**
 * Composable for recording user action traces during normal use.
 * Internal tooling — captures route navigations, store actions, and custom events.
 *
 * Usage:
 *   const recorder = useTraceRecorder()
 *   recorder.start('my-trace')
 *   recorder.recordAction({ type: 'click', label: 'Clicked button', payload: { selector: '#btn' } })
 *   const trace = recorder.stop()
 */

import { ref, readonly, type DeepReadonly, type Ref } from 'vue'
import type {
  Trace,
  TraceAction,
  TraceActionType,
  TraceActionPayload,
  TraceMetadata,
} from '../types/trace'

let idCounter = 0
function nextId(): string {
  return `trace-action-${++idCounter}-${Date.now()}`
}

function generateTraceId(): string {
  return `trace-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`
}

export interface RecordActionInput {
  type: TraceActionType
  label: string
  payload: TraceActionPayload
}

export interface UseTraceRecorderReturn {
  /** Whether a recording is currently in progress. */
  isRecording: DeepReadonly<Ref<boolean>>
  /** The current trace being recorded (null if not recording). */
  currentTrace: DeepReadonly<Ref<Trace | null>>
  /** Start recording a new trace. */
  start: (name: string) => void
  /** Record a single action into the current trace. */
  recordAction: (input: RecordActionInput) => void
  /** Stop recording and return the completed trace. */
  stop: () => Trace | null
  /** Get the number of recorded actions. */
  actionCount: DeepReadonly<Ref<number>>
}

export function useTraceRecorder(): UseTraceRecorderReturn {
  const isRecording = ref(false)
  const currentTrace = ref<Trace | null>(null)
  const actionCount = ref(0)

  let recordingStartTime = 0

  function buildMetadata(): TraceMetadata {
    return {
      userAgent: typeof navigator !== 'undefined' ? navigator.userAgent : undefined,
      screenWidth: typeof window !== 'undefined' ? window.innerWidth : undefined,
      screenHeight: typeof window !== 'undefined' ? window.innerHeight : undefined,
    }
  }

  function start(name: string): void {
    if (isRecording.value) {
      console.warn('[trace-recorder] Already recording. Stop the current trace first.')
      return
    }

    const now = new Date()
    recordingStartTime = now.getTime()

    currentTrace.value = {
      id: generateTraceId(),
      name,
      startedAt: now.toISOString(),
      endedAt: null,
      durationMs: 0,
      actions: [],
      metadata: buildMetadata(),
    }

    actionCount.value = 0
    isRecording.value = true
  }

  function recordAction(input: RecordActionInput): void {
    if (!isRecording.value || !currentTrace.value) {
      return
    }

    const now = new Date()
    const action: TraceAction = {
      id: nextId(),
      type: input.type,
      timestamp: now.toISOString(),
      offsetMs: now.getTime() - recordingStartTime,
      label: input.label,
      payload: input.payload,
    }

    currentTrace.value.actions.push(action)
    currentTrace.value.durationMs = action.offsetMs
    actionCount.value = currentTrace.value.actions.length
  }

  function stop(): Trace | null {
    if (!isRecording.value || !currentTrace.value) {
      return null
    }

    const now = new Date()
    currentTrace.value.endedAt = now.toISOString()
    currentTrace.value.durationMs = now.getTime() - recordingStartTime

    const completedTrace = { ...currentTrace.value }
    isRecording.value = false
    currentTrace.value = null
    actionCount.value = 0
    recordingStartTime = 0

    return completedTrace
  }

  return {
    isRecording: readonly(isRecording),
    currentTrace: readonly(currentTrace) as DeepReadonly<Ref<Trace | null>>,
    start,
    recordAction,
    stop,
    actionCount: readonly(actionCount),
  }
}
