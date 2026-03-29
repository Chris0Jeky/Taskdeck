import { beforeAll, afterEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'

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
