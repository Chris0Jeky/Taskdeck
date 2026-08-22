import { defineStore } from 'pinia'
import { ref } from 'vue'

export interface ToastAction {
  /** Short label for the action (e.g. "undo", "open"). */
  label: string
  /** Optional kbd hint shown to the right of the label. */
  hint?: string
  /** Invoked when the user clicks the action.  Toast is removed after. */
  handler: () => void
}

/**
 * The outcome word a toast is stamped with (#1970).
 *
 * A toast's `type` is its SEVERITY — which colour to paint — not a statement
 * about what happened. Paper renders a word beside the message, and deriving
 * that word from the severity stamped "APPLIED" on every success, including
 * inbox saves and pre-apply approvals where "applied" is precisely the thing
 * that had NOT happened yet.
 *
 * `label` is that word's identity, chosen by the caller that knows which
 * action ran. `applied` is reserved for a proposal actually written to a board
 * (post-`/execute`) and must never be used as a generic success stamp.
 *
 * `done` / `noted` / `warning` / `failed` are the severity-generic fallbacks
 * used when a caller names no action; the renderer picks one from `type`, so
 * an unlabelled toast degrades to a neutral word, never to an action word.
 */
export type ToastLabel =
  | 'saved'
  | 'queued'
  | 'approved'
  | 'applied'
  | 'done'
  | 'noted'
  | 'warning'
  | 'failed'

export interface ToastOptions {
  /** Optional title, used by paper-mode rendering for the strong line. */
  title?: string
  /** Optional inline action (e.g. an "open" shortcut). */
  action?: ToastAction
  /** Outcome word for the paper-mode stamp; falls back to a severity word. */
  label?: ToastLabel
}

export interface Toast extends ToastOptions {
  id: string
  message: string
  type: 'success' | 'error' | 'info' | 'warning'
  duration: number
}

type ToastTimer = {
  timeout: ReturnType<typeof setTimeout> | null
  startedAt: number
  remaining: number
  paused: boolean
}

export const useToastStore = defineStore('toast', () => {
  const toasts = ref<Toast[]>([])
  const timers = new Map<string, ToastTimer>()

  function clearTimer(id: string) {
    const timer = timers.get(id)
    if (timer?.timeout) {
      clearTimeout(timer.timeout)
    }
    timers.delete(id)
  }

  function scheduleRemoval(id: string, duration: number) {
    if (duration <= 0) return

    const timer: ToastTimer = {
      timeout: null,
      startedAt: Date.now(),
      remaining: duration,
      paused: false,
    }

    timer.timeout = setTimeout(() => {
      remove(id)
    }, duration)

    timers.set(id, timer)
  }

  function show(
    message: string,
    type: Toast['type'] = 'info',
    duration = 3000,
    options: ToastOptions = {},
  ) {
    const id = `toast-${Date.now()}-${Math.random()}`
    const toast: Toast = { id, message, type, duration, ...options }

    toasts.value.push(toast)
    scheduleRemoval(id, duration)

    return id
  }

  function success(message: string, duration = 3000, options: ToastOptions = {}) {
    return show(message, 'success', duration, options)
  }

  function error(message: string, duration = 5000, options: ToastOptions = {}) {
    return show(message, 'error', duration, options)
  }

  function info(message: string, duration = 3000, options: ToastOptions = {}) {
    return show(message, 'info', duration, options)
  }

  function warning(message: string, duration = 4000, options: ToastOptions = {}) {
    return show(message, 'warning', duration, options)
  }

  function remove(id: string) {
    const index = toasts.value.findIndex((t) => t.id === id)
    if (index !== -1) {
      toasts.value.splice(index, 1)
    }
    clearTimer(id)
  }

  function clear() {
    for (const id of timers.keys()) {
      clearTimer(id)
    }
    toasts.value = []
  }

  function pause(id: string) {
    const timer = timers.get(id)
    if (!timer || timer.paused) return

    if (timer.timeout) {
      clearTimeout(timer.timeout)
      timer.timeout = null
    }

    timer.remaining = Math.max(0, timer.remaining - (Date.now() - timer.startedAt))
    timer.paused = true
  }

  function resume(id: string) {
    const timer = timers.get(id)
    if (!timer || !timer.paused) return

    timer.paused = false
    timer.startedAt = Date.now()

    if (timer.remaining <= 0) {
      remove(id)
      return
    }

    timer.timeout = setTimeout(() => {
      remove(id)
    }, timer.remaining)
  }

  return {
    toasts,
    show,
    success,
    error,
    info,
    warning,
    remove,
    clear,
    pause,
    resume,
  }
})
