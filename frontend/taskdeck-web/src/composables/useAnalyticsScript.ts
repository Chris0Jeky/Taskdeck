import { watch, onUnmounted } from 'vue'
import { useTelemetryStore } from '../store/telemetryStore'

const SCRIPT_ID = 'taskdeck-analytics-script'

/**
 * Injects a self-hosted analytics script (Plausible or Umami) when:
 * 1. The user has given telemetry consent
 * 2. The server has provided analytics configuration
 *
 * Cookie-free, no-PII analytics only. The script is removed if consent
 * is revoked or the component unmounts.
 */
export function useAnalyticsScript() {
  const telemetry = useTelemetryStore()

  function injectScript() {
    if (document.getElementById(SCRIPT_ID)) return

    const config = telemetry.analyticsConfig
    if (!config) return

    const script = document.createElement('script')
    script.id = SCRIPT_ID
    script.src = config.scriptUrl
    script.defer = true
    script.async = true

    // Provider-specific attributes
    const provider = config.provider.toLowerCase()
    if (provider === 'plausible') {
      script.setAttribute('data-domain', config.siteId)
    } else if (provider === 'umami') {
      script.setAttribute('data-website-id', config.siteId)
    }

    document.head.appendChild(script)
  }

  function removeScript() {
    const existing = document.getElementById(SCRIPT_ID)
    if (existing) {
      existing.remove()
    }
  }

  const stopWatch = watch(
    () => telemetry.analyticsConfig,
    (config) => {
      if (config) {
        injectScript()
      } else {
        removeScript()
      }
    },
    { immediate: true },
  )

  onUnmounted(() => {
    stopWatch()
    removeScript()
  })

  return { injectScript, removeScript }
}
