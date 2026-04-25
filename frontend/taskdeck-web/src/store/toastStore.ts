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

export interface Toast {
  id: string
  message: string
  type: 'success' | 'error' | 'info' | 'warning'
  duration: number
  /** Optional title, used by paper-mode rendering for the strong line. */
  title?: string
  /** Optional inline action (e.g. "undo · 6h"). */
  action?: ToastAction
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
    options: { title?: string; action?: ToastAction } = {},
  ) {
    const id = `toast-${Date.now()}-${Math.random()}`
    const toast: Toast = { id, message, type, duration, ...options }

    toasts.value.push(toast)
    scheduleRemoval(id, duration)

    return id
  }

  function success(message: string, duration = 3000) {
    return show(message, 'success', duration)
  }

  function error(message: string, duration = 5000) {
    return show(message, 'error', duration)
  }

  function info(message: string, duration = 3000) {
    return show(message, 'info', duration)
  }

  function warning(message: string, duration = 4000) {
    return show(message, 'warning', duration)
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
