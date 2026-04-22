import { ref, watch, onUnmounted, getCurrentInstance, readonly, type Ref } from 'vue'
import { useSessionStore } from '../store/sessionStore'
import { parseJwtPayload } from '../utils/jwt'

/**
 * How long before token expiry to show the warning (milliseconds).
 * Default: 5 minutes.
 */
export const WARNING_BEFORE_EXPIRY_MS = 5 * 60 * 1000

/**
 * How often to tick the countdown display (milliseconds).
 */
const COUNTDOWN_TICK_MS = 1_000

export interface SessionTimeoutState {
  /** Whether the warning banner is currently visible. */
  showWarning: Readonly<Ref<boolean>>
  /** Seconds remaining until the token expires. Null when no warning is active. */
  secondsRemaining: Readonly<Ref<number | null>>
  /** Whether a session extension attempt is in progress. */
  extending: Readonly<Ref<boolean>>
  /** Dismiss the warning banner without extending. */
  dismiss: () => void
  /** Attempt to extend the session (silent re-auth). */
  extend: () => Promise<void>
  /** Stop all timers (for cleanup/testing). */
  teardown: () => void
}

/**
 * Composable that monitors JWT expiry and shows a warning before the session expires.
 *
 * Features:
 * - Shows a warning `WARNING_BEFORE_EXPIRY_MS` before the token expires
 * - Live countdown in seconds
 * - "Extend Session" action that attempts a token refresh
 * - No duplicate warnings per token (deduplication by token string)
 * - Skips entirely in demo mode or when unauthenticated
 * - Cleans up all timers on unmount or token change
 *
 * @param deps Injectable dependencies for testing
 */
export function useSessionTimeout(deps?: {
  refreshToken?: () => Promise<{ token: string; user: { id: string; username: string; email: string; defaultRole: number; isActive: boolean; createdAt: string; updatedAt: string } }>
  nowFn?: () => number
}): SessionTimeoutState {
  const session = useSessionStore()

  const showWarning = ref(false)
  const secondsRemaining = ref<number | null>(null)
  const extending = ref(false)

  let warningTimer: ReturnType<typeof setTimeout> | null = null
  let countdownInterval: ReturnType<typeof setInterval> | null = null
  let warnedForToken: string | null = null
  let tokenExpiryMs: number | null = null

  const nowFn = deps?.nowFn ?? (() => Date.now())

  function clearTimers() {
    if (warningTimer !== null) {
      clearTimeout(warningTimer)
      warningTimer = null
    }
    if (countdownInterval !== null) {
      clearInterval(countdownInterval)
      countdownInterval = null
    }
  }

  function resetState() {
    clearTimers()
    showWarning.value = false
    secondsRemaining.value = null
    tokenExpiryMs = null
  }

  function startCountdown() {
    if (countdownInterval !== null) {
      clearInterval(countdownInterval)
    }
    updateCountdown()
    countdownInterval = setInterval(updateCountdown, COUNTDOWN_TICK_MS)
  }

  function updateCountdown() {
    if (tokenExpiryMs === null) {
      secondsRemaining.value = null
      return
    }
    const remaining = Math.max(0, Math.ceil((tokenExpiryMs - nowFn()) / 1000))
    secondsRemaining.value = remaining

    if (remaining <= 0) {
      // Token has expired — the HTTP interceptor / session store will handle logout
      resetState()
    }
  }

  function activateWarning() {
    showWarning.value = true
    startCountdown()
  }

  function scheduleWarning(token: string) {
    resetState()

    // Skip if we already warned for this exact token
    if (token === warnedForToken) return

    const payload = parseJwtPayload(token)
    if (!payload?.exp) return

    tokenExpiryMs = payload.exp * 1000
    const now = nowFn()
    const msUntilExpiry = tokenExpiryMs - now

    // Token already expired
    if (msUntilExpiry <= 0) return

    const msUntilWarning = msUntilExpiry - WARNING_BEFORE_EXPIRY_MS

    if (msUntilWarning <= 0) {
      // Already within warning window — show immediately
      warnedForToken = token
      activateWarning()
    } else {
      warningTimer = setTimeout(() => {
        // Double-check token hasn't changed while we waited
        if (session.token === token) {
          warnedForToken = token
          activateWarning()
        }
      }, msUntilWarning)
    }
  }

  function dismiss() {
    showWarning.value = false
    secondsRemaining.value = null
    if (countdownInterval !== null) {
      clearInterval(countdownInterval)
      countdownInterval = null
    }
  }

  async function extend() {
    if (extending.value) return

    extending.value = true
    try {
      const refreshToken = deps?.refreshToken
      if (!refreshToken) {
        // No refresh capability — dynamic import to avoid circular dependency
        const { authApi } = await import('../api/authApi')
        const response = await authApi.refreshToken()
        // The session store will pick up the new token via setSession,
        // which triggers the watcher and reschedules the warning timer
        const { useSessionStore: getStore } = await import('../store/sessionStore')
        const store = getStore()
        // Use internal setSession logic: update store state with new token
        store.token = response.token
        store.expiresAt = new Date(
          (parseJwtPayload(response.token)?.exp ?? 0) * 1000,
        ).toISOString()

        // Persist the new token
        const tokenStorage = await import('../utils/tokenStorage')
        tokenStorage.setToken(response.token)

        // Reset warning state for the new token
        warnedForToken = null
        dismiss()
        return
      }

      // Injected refreshToken (used in tests)
      const response = await refreshToken()
      session.token = response.token
      session.expiresAt = new Date(
        (parseJwtPayload(response.token)?.exp ?? 0) * 1000,
      ).toISOString()
      warnedForToken = null
      dismiss()
    } catch {
      // Refresh failed — show a fallback message.
      // The toast store will be used by the component layer.
      // We keep the warning visible so the user knows to save work.
      const { useToastStore } = await import('../store/toastStore')
      const toast = useToastStore()
      toast.warning(
        'Could not extend session. Please save your work and log in again.',
        8000,
      )
    } finally {
      extending.value = false
    }
  }

  function teardown() {
    resetState()
    warnedForToken = null
  }

  // Watch token changes to schedule/reset warning
  const stopWatch = watch(
    () => ({ token: session.token, isDemo: session.isDemo }),
    ({ token, isDemo }) => {
      if (isDemo || !token) {
        resetState()
        // Do not clear warnedForToken here — if the same token returns
        // (e.g. from a brief reactive fluctuation), we still suppress
        // the warning. warnedForToken is cleared only in teardown(),
        // extend(), or when scheduleWarning runs for a genuinely new token.
        return
      }
      scheduleWarning(token)
    },
    { immediate: true },
  )

  // Cleanup on unmount (only register if inside a component)
  if (getCurrentInstance()) {
    onUnmounted(() => {
      stopWatch()
      teardown()
    })
  }

  return {
    showWarning: readonly(showWarning),
    secondsRemaining: readonly(secondsRemaining),
    extending: readonly(extending),
    dismiss,
    extend,
    teardown,
  }
}
