import { createApp } from 'vue'
import { createPinia } from 'pinia'
import router from './router'
import App from './App.vue'
import './style.css'
import './paper-fonts.css'
import './paper-tokens.css'
import {
  installVueErrorHandler,
  installWindowErrorListeners,
} from './utils/errorReporting'

const app = createApp(App)
const pinia = createPinia()

app.use(pinia)
app.use(router)

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
