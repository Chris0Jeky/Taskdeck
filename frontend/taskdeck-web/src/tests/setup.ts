import { beforeAll, afterEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'

// Create a fresh Pinia instance before all tests
beforeAll(() => {
  setActivePinia(createPinia())
})

// Clean up after each test
afterEach(() => {
  // Reset to a fresh Pinia instance for each test
  setActivePinia(createPinia())
})
