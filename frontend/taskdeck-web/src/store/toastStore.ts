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

export const useToastStore = defineStore('toast', () => {
  const toasts = ref<Toast[]>([])

  function show(
    message: string,
    type: Toast['type'] = 'info',
    duration = 3000,
    options: { title?: string; action?: ToastAction } = {},
  ) {
    const id = `toast-${Date.now()}-${Math.random()}`
    const toast: Toast = { id, message, type, duration, ...options }

    toasts.value.push(toast)

    if (duration > 0) {
      setTimeout(() => {
        remove(id)
      }, duration)
    }

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
  }

  function clear() {
    toasts.value = []
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
  }
})
