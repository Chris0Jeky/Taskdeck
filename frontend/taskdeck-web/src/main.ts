import { createApp } from 'vue'
import { createPinia } from 'pinia'
import router from './router'
import App from './App.vue'
import './style.css'

const app = createApp(App)
const pinia = createPinia()

app.use(pinia)
app.use(router)

app.mount('#app')

// Initialize telemetry after mount (non-blocking, opt-in).
// This restores user consent from localStorage and fetches server config.
// No events are emitted unless the user has explicitly opted in.
import('./store/telemetryStore').then(({ useTelemetryStore }) => {
  const telemetry = useTelemetryStore()
  void telemetry.initialize()
})
