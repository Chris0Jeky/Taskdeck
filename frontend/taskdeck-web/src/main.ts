import { createApp } from 'vue'
import { createPinia } from 'pinia'
import router from './router'
import { i18n } from './i18n'
import { useLocaleStore } from './store/localeStore'
import App from './App.vue'
import './style.css'
import './paper-fonts.css'
import './paper-tokens.css'
// Must come after paper-tokens.css: it remaps the legacy Obsidian substrate
// onto Paper values, scoped under `.paper` / `.paper-night` (ADR-0053, #1778).
import './paper-legacy-bridge.css'
import {
  installVueErrorHandler,
  installWindowErrorListeners,
  logError,
} from './utils/errorReporting'

const app = createApp(App)
const pinia = createPinia()

app.use(pinia)
app.use(i18n)
app.use(router)

// Install global crash-prevention hooks before mount so early errors are
// captured. The Vue handler is the top-level backstop for render/lifecycle
// errors; the window listeners catch async rejections and non-Vue errors.
installVueErrorHandler(app)
installWindowErrorListeners()

// Restore the persisted language preference (ADR-0054 §7) before mount. The
// store keeps the committed locale on English until a lazy catalog is ready,
// so waiting here preserves the no-flash guarantee without briefly claiming a
// language whose messages are unavailable. Catalog failure is reported by the
// mounted picker; it must not prevent the app from starting in English. The
// wait is bounded: a lazy catalog request that stalls instead of failing must
// not hold first paint hostage, so after LOCALE_RESTORE_MOUNT_BUDGET_MS the app
// mounts in English and the store commits the locale atomically if the catalog
// arrives later.
const LOCALE_RESTORE_MOUNT_BUDGET_MS = 1500

const localeRestore = useLocaleStore(pinia)
  .apply()
  .catch((error: unknown) => {
    logError('[main] locale restore rejected before mount', error)
  })

const mountBudget = new Promise<void>((resolve) => {
  window.setTimeout(resolve, LOCALE_RESTORE_MOUNT_BUDGET_MS)
})

void Promise.race([localeRestore, mountBudget])
  .finally(() => {
    app.mount('#app')

    // Initialize telemetry after mount (non-blocking, opt-in).
    // This restores user consent from localStorage and fetches server config.
    // No events are emitted unless the user has explicitly opted in.
    import('./store/telemetryStore').then(({ useTelemetryStore }) => {
      const telemetry = useTelemetryStore()
      void telemetry.initialize()
    })

    // Initialize analytics script watcher after mount (non-blocking).
    // This watches the telemetry store's analyticsConfig and injects/removes
    // the analytics script based on user consent and server configuration.
    import('./composables/useAnalyticsScript').then(({ initAnalyticsScriptWatcher }) => {
      initAnalyticsScriptWatcher()
    })
  })
