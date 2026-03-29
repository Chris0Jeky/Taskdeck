/**
 * Trace data model for recording and replaying user action sequences.
 * Internal tooling — used for demo analysis and test scenario authoring.
 */

export type TraceActionType =
  | 'route-navigation'
  | 'click'
  | 'input'
  | 'api-call'
  | 'store-action'
  | 'custom'

export interface TraceAction {
  /** Unique identifier for this action within the trace. */
  id: string
  /** The type of user action recorded. */
  type: TraceActionType
  /** ISO timestamp when the action occurred. */
  timestamp: string
  /** Milliseconds elapsed since the trace started. */
  offsetMs: number
  /** Human-readable label for the action. */
  label: string
  /** Action-specific payload (e.g., route path, element selector, API endpoint). */
  payload: TraceActionPayload
}

export interface RouteNavigationPayload {
  from: string
  to: string
}

export interface ClickPayload {
  selector: string
  text?: string
}

export interface InputPayload {
  selector: string
  value: string
}

export interface ApiCallPayload {
  method: string
  url: string
  status?: number
  durationMs?: number
}

export interface StoreActionPayload {
  store: string
  action: string
  args?: unknown[]
}

export interface CustomPayload {
  [key: string]: unknown
}

export type TraceActionPayload =
  | RouteNavigationPayload
  | ClickPayload
  | InputPayload
  | ApiCallPayload
  | StoreActionPayload
  | CustomPayload

export interface Trace {
  /** Unique identifier for this trace. */
  id: string
  /** Human-readable name for the trace. */
  name: string
  /** ISO timestamp when recording started. */
  startedAt: string
  /** ISO timestamp when recording ended (null if still recording). */
  endedAt: string | null
  /** Total duration of the trace in milliseconds. */
  durationMs: number
  /** Ordered list of recorded actions. */
  actions: TraceAction[]
  /** Optional metadata about the recording environment. */
  metadata?: TraceMetadata
}

export interface TraceMetadata {
  userAgent?: string
  screenWidth?: number
  screenHeight?: number
  appVersion?: string
}

export type ReplayStatus = 'idle' | 'playing' | 'paused' | 'completed' | 'error'

export interface ReplayState {
  status: ReplayStatus
  currentIndex: number
  totalActions: number
  elapsedMs: number
  playbackSpeed: number
  error?: string
}
