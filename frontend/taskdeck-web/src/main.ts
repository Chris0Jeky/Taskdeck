import { createApp } from 'vue'
import { createPinia } from 'pinia'
import router from './router'
import { i18n } from './i18n'
import { useLocaleStore } from './store/localeStore'
import App from './App.vue'
import '@material-symbols/font-400/outlined.css'
import './style.css'
import './paper-fonts.css'
import './paper-tokens.css'
// Must come after paper-tokens.css: it remaps the legacy Obsidian substrate
// onto Paper values, scoped under `.paper` / `.paper-night` (ADR-0053, #1778).
import './paper-legacy-bridge.css'
import {
  installVueErrorHandler,
  installWindowErrorListeners,
} from './utils/errorReporting'

const app = createApp(App)
const pinia = createPinia()

app.use(pinia)
app.use(i18n)
app.use(router)

// Restore the persisted language preference (ADR-0054 §7) and push it into the
// i18n runtime + `<html lang>`. Statically imported and called synchronously on
// purpose: this must happen after `app.use(pinia)` (the store needs an active
// Pinia) and BEFORE `app.mount` below, so the first paint is already in the
// user's language instead of flashing English. A dynamic import would resolve
// after mount and produce exactly that flash.
useLocaleStore(pinia).apply()

// Install global crash-prevention hooks before mount so early errors are
// captured. The Vue handler is the top-level backstop for render/lifecycle
// errors; the window listeners catch async rejections and non-Vue errors.
installVueErrorHandler(app)
installWindowErrorListeners()

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
