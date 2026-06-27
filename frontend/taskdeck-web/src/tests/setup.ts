import { beforeAll, beforeEach, afterEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'

// Default the Paper theme to 'off' (Legacy DOM) for unit tests. After the Wave-3 flip (ADR-0038)
// the production default is 'paper', which makes the view shells (InboxView/BoardView/ReviewView,
// `<PaperX v-if="paperTheme.isOn"/>`) render their Paper variants and break the many Legacy-DOM
// specs. Specs that exercise the store's default directly clear localStorage in their own
// beforeEach (which runs after this one); Paper-variant specs opt into 'paper' explicitly. This is
// the unit-test analog of the E2E authSession off-pin.
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
