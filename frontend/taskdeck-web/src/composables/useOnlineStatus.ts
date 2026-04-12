import { ref, onMounted, onUnmounted, readonly } from 'vue'

/**
 * Reactive composable that tracks `navigator.onLine` and fires
 * callbacks when the browser connectivity state changes.
 *
 * Usage:
 *   const { isOnline, lastChangedAt } = useOnlineStatus()
 *
 * The composable adds `online`/`offline` event listeners on mount
 * and cleans them up on unmount. It is safe to call outside of a
 * component setup context (e.g. in tests) — the listeners simply
 * won't be registered and you can call `_handleOnline`/`_handleOffline`
 * directly for testing.
 */
export function useOnlineStatus() {
  const isOnline = ref(typeof navigator !== 'undefined' ? navigator.onLine : true)
  const lastChangedAt = ref<Date | null>(null)

  function handleOnline() {
    isOnline.value = true
    lastChangedAt.value = new Date()
  }

  function handleOffline() {
    isOnline.value = false
    lastChangedAt.value = new Date()
  }

  onMounted(() => {
    window.addEventListener('online', handleOnline)
    window.addEventListener('offline', handleOffline)
  })

  onUnmounted(() => {
    window.removeEventListener('online', handleOnline)
    window.removeEventListener('offline', handleOffline)
  })

  return {
    isOnline: readonly(isOnline),
    lastChangedAt: readonly(lastChangedAt),
    /** @internal — exposed for testing only */
    _handleOnline: handleOnline,
    /** @internal — exposed for testing only */
    _handleOffline: handleOffline,
  }
}
