import { watch, onUnmounted } from 'vue'
import { useTelemetryStore } from '../store/telemetryStore'

const SCRIPT_ID = 'taskdeck-analytics-script'

/** List of supported analytics providers */
const SUPPORTED_PROVIDERS = ['plausible', 'umami']

/** Pattern for valid site IDs (alphanumeric, dots, hyphens, underscores) */
const SITE_ID_PATTERN = /^[a-zA-Z0-9._-]+$/

/**
 * Validates the analytics script URL is HTTPS.
 */
function isValidScriptUrl(url: string): boolean {
  try {
    const parsed = new URL(url)
    return parsed.protocol === 'https:'
  } catch {
    return false
  }
}

/**
 * Validates the analytics provider is supported.
 */
function isValidProvider(provider: string): boolean {
  return SUPPORTED_PROVIDERS.includes(provider.toLowerCase())
}

/**
 * Validates the site ID format to prevent injection attacks.
 */
function isValidSiteId(siteId: string): boolean {
  return !!siteId && SITE_ID_PATTERN.test(siteId)
}

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

    // Only allow HTTPS URLs to prevent javascript:, data:, or blob: injection
    if (!isValidScriptUrl(config.scriptUrl)) {
      console.warn('[Taskdeck] Analytics script URL rejected: must be HTTPS', config.scriptUrl)
      return
    }

    // Validate provider to prevent arbitrary attribute injection
    if (!isValidProvider(config.provider)) {
      console.warn('[Taskdeck] Analytics provider rejected: unsupported provider', config.provider)
      return
    }

    // Validate siteId to prevent injection via data attributes
    if (!isValidSiteId(config.siteId)) {
      console.warn('[Taskdeck] Analytics siteId rejected: invalid format', config.siteId)
      return
    }

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

/**
 * Initializes the analytics script watcher outside of a Vue component context.
 * Called from main.ts during app bootstrap.
 */
export function initAnalyticsScriptWatcher() {
  const telemetry = useTelemetryStore()

  // Helper functions duplicated here to avoid component lifecycle requirements
  function injectScript() {
    if (document.getElementById(SCRIPT_ID)) return

    const config = telemetry.analyticsConfig
    if (!config) return

    if (!isValidScriptUrl(config.scriptUrl)) {
      console.warn('[Taskdeck] Analytics script URL rejected: must be HTTPS', config.scriptUrl)
      return
    }

    if (!isValidProvider(config.provider)) {
      console.warn('[Taskdeck] Analytics provider rejected: unsupported provider', config.provider)
      return
    }

    if (!isValidSiteId(config.siteId)) {
      console.warn('[Taskdeck] Analytics siteId rejected: invalid format', config.siteId)
      return
    }

    const script = document.createElement('script')
    script.id = SCRIPT_ID
    script.src = config.scriptUrl
    script.defer = true
    script.async = true

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

  // Watch analyticsConfig and inject/remove script accordingly
  watch(
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
}
