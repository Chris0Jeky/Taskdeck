import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { telemetryApi } from '../api/telemetryApi'
import type {
  ClientTelemetryConfig,
  TelemetryEventPayload,
} from '../api/telemetryApi'

const CONSENT_KEY = 'taskdeck_telemetry_consent'
const FLUSH_INTERVAL_MS = 30_000 // 30 seconds
const MAX_BUFFER_SIZE = 200
type PrivacyAwareNavigator = Navigator & {
  globalPrivacyControl?: boolean
}

/**
 * Checks whether the browser signals Do Not Track (DNT) or
 * Global Privacy Control (GPC). When either is active, telemetry
 * consent should not be auto-restored from localStorage.
 */
function browserSignalsPrivacy(): boolean {
  if (typeof navigator === 'undefined') return false
  // GPC has legal force under CCPA — respect it unconditionally
  const browserNavigator = navigator as PrivacyAwareNavigator
  if (browserNavigator.globalPrivacyControl === true) return true
  // DNT is advisory but we respect it as a privacy-first product
  if (browserNavigator.doNotTrack === '1') return true
  return false
}

/**
 * Generates a random UUID v4 for anonymous session identification.
 * This ID is rotated on every app load — it is NOT tied to user identity.
 */
function generateSessionId(): string {
  if (typeof crypto !== 'undefined' && crypto.randomUUID) {
    return crypto.randomUUID()
  }
  // Fallback for environments without crypto.randomUUID
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0
    const v = c === 'x' ? r : (r & 0x3) | 0x8
    return v.toString(16)
  })
}

export const useTelemetryStore = defineStore('telemetry', () => {
  // ── State ──────────────────────────────────────────────────────────

  /** User has explicitly opted in to telemetry */
  const consentGiven = ref(false)

  /** Server-side telemetry config (fetched from /api/telemetry/config) */
  const serverConfig = ref<ClientTelemetryConfig | null>(null)

  /** Whether we have fetched the server config */
  const configLoaded = ref(false)

  /** Anonymous session ID, rotated per app load */
  const sessionId = ref(generateSessionId())

  /** Whether the browser has DNT or GPC active */
  const privacySignalActive = ref(browserSignalsPrivacy())

  /** Buffered events waiting to be flushed */
  const eventBuffer = ref<TelemetryEventPayload[]>([])

  /** Timer ID for periodic flush */
  let flushTimerId: ReturnType<typeof setInterval> | null = null

  // ── Computed ────────────────────────────────────────────────────────

  /** Telemetry is active only when BOTH user consents AND server enables it */
  const isActive = computed(
    () => consentGiven.value && !!serverConfig.value?.telemetry.enabled,
  )

  /** Sentry is available when server provides a DSN and user consents */
  const sentryAvailable = computed(
    () =>
      consentGiven.value &&
      !!serverConfig.value?.sentry.enabled &&
      !!serverConfig.value?.sentry.dsn,
  )

  /** Analytics script config (only populated when enabled and consented) */
  const analyticsConfig = computed(() => {
    if (
      !consentGiven.value ||
      !serverConfig.value?.analytics.enabled ||
      !serverConfig.value?.analytics.scriptUrl
    ) {
      return null
    }
    return serverConfig.value.analytics
  })

  // ── Actions ─────────────────────────────────────────────────────────

  /** Restore consent from localStorage. Does NOT auto-restore if DNT/GPC is active. */
  function restoreConsent() {
    if (privacySignalActive.value) {
      // Browser signals privacy preference — do not auto-restore consent.
      // User must explicitly opt in again each session.
      consentGiven.value = false
      return
    }
    const stored = localStorage.getItem(CONSENT_KEY)
    consentGiven.value = stored === 'true'
  }

  /** Set user consent and persist */
  function setConsent(value: boolean) {
    consentGiven.value = value
    localStorage.setItem(CONSENT_KEY, String(value))

    if (!value) {
      // User revoked consent — clear buffer and stop flushing
      eventBuffer.value = []
      stopFlushTimer()
    } else {
      startFlushTimer()
    }
  }

  /** Fetch server-side telemetry config */
  async function loadConfig() {
    try {
      serverConfig.value = await telemetryApi.getConfig()
      configLoaded.value = true
    } catch {
      // Config fetch failure is non-fatal — telemetry simply stays disabled
      configLoaded.value = true
    }
  }

  /**
   * Emit a telemetry event. The event is buffered locally and flushed
   * periodically. No-op if telemetry is not active.
   */
  function emit(
    eventName: string,
    properties?: Record<string, unknown>,
  ) {
    if (!isActive.value) {
      return
    }

    const event: TelemetryEventPayload = {
      event: eventName,
      timestamp: new Date().toISOString(),
      sessionId: sessionId.value,
      workspaceMode: 'guided', // Will be overridden by caller when available
      appVersion: '0.1.0', // Will be set from build config in future
      platform: 'web',
      properties,
    }

    eventBuffer.value.push(event)

    // Prevent unbounded memory growth
    if (eventBuffer.value.length > MAX_BUFFER_SIZE) {
      eventBuffer.value = eventBuffer.value.slice(-MAX_BUFFER_SIZE)
    }
  }

  /** Flush buffered events to the server */
  async function flush() {
    if (!isActive.value || eventBuffer.value.length === 0) {
      return
    }

    const eventsToSend = [...eventBuffer.value]
    eventBuffer.value = []

    try {
      await telemetryApi.sendEvents(eventsToSend)
    } catch {
      // Re-buffer events on failure (up to max size)
      eventBuffer.value = [...eventsToSend, ...eventBuffer.value].slice(
        -MAX_BUFFER_SIZE,
      )
    }
  }

  /** Start periodic flush timer */
  function startFlushTimer() {
    if (flushTimerId !== null) return
    flushTimerId = setInterval(() => {
      void flush()
    }, FLUSH_INTERVAL_MS)
  }

  /** Stop periodic flush timer */
  function stopFlushTimer() {
    if (flushTimerId !== null) {
      clearInterval(flushTimerId)
      flushTimerId = null
    }
  }

  /**
   * Initialize telemetry: restore consent, fetch config, start flush timer
   * if consent was previously given.
   */
  async function initialize() {
    restoreConsent()
    await loadConfig()
    if (consentGiven.value) {
      startFlushTimer()
    }
  }

  /** Clean up on unmount / page unload */
  function dispose() {
    stopFlushTimer()
    // Best-effort flush remaining events
    if (isActive.value && eventBuffer.value.length > 0) {
      void flush()
    }
  }

  return {
    // State
    consentGiven,
    serverConfig,
    configLoaded,
    sessionId,
    eventBuffer,
    privacySignalActive,

    // Computed
    isActive,
    sentryAvailable,
    analyticsConfig,

    // Actions
    restoreConsent,
    setConsent,
    loadConfig,
    emit,
    flush,
    initialize,
    dispose,
    startFlushTimer,
    stopFlushTimer,
  }
})
