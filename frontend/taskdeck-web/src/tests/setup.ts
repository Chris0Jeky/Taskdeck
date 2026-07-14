import { beforeAll, beforeEach, afterEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'

// Decision retained 2026-07-14 for #1274: keep the global unit-suite Legacy pin while core
// browser journeys move to Paper. The focused component suite intentionally asserts frozen
// Legacy DOM; migrating it is a separate coverage project, not a prerequisite for production-
// aligned E2E defaults. Paper-variant specs continue to opt in explicitly.
// Default the Paper theme to 'off' (Legacy DOM) for unit tests. After the Wave-3 flip (ADR-0038)
// the production default is 'paper', which makes the view shells (InboxView/BoardView/ReviewView,
// `<PaperX v-if="paperTheme.isOn"/>`) render their Paper variants and break the many Legacy-DOM
// specs. Specs that exercise the store's default directly clear localStorage in their own
// beforeEach (which runs after this one); Paper-variant specs opt into 'paper' explicitly. This is
// a deliberate unit-only compatibility pin.
beforeEach(() => {
  try {
    window.localStorage.setItem('td.paper.mode.v2', 'off')
  } catch {
    // environments without localStorage — ignore
  }
})

// Create a fresh Pinia instance before all tests
beforeAll(() => {
  setActivePinia(createPinia())

  // happy-dom does not implement window.confirm/alert/prompt; define stubs so
  // vi.spyOn(window, 'confirm') can wrap them.
  if (typeof window.confirm !== 'function') {
    window.confirm = () => false
  }
  if (typeof window.alert !== 'function') {
    window.alert = () => undefined
  }
  if (typeof window.prompt !== 'function') {
    window.prompt = () => null
  }
})

// Clean up after each test
afterEach(() => {
  // Reset to a fresh Pinia instance for each test
  setActivePinia(createPinia())
})
