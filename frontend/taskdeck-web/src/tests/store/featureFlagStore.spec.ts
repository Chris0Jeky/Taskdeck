import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useFeatureFlagStore } from '../../store/featureFlagStore'
import { defaultFeatureFlags } from '../../types/feature-flags'

describe('featureFlagStore', () => {
  let store: ReturnType<typeof useFeatureFlagStore>

  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
    store = useFeatureFlagStore()
  })

  describe('default flags', () => {
    it('should match configured defaults', () => {
      for (const key of Object.keys(defaultFeatureFlags) as (keyof typeof defaultFeatureFlags)[]) {
        expect(store.isEnabled(key)).toBe(defaultFeatureFlags[key])
      }
    })
  })

  describe('setFlag', () => {
    it('should change a specific flag', () => {
      store.setFlag('newAuth', false)

      expect(store.isEnabled('newAuth')).toBe(false)
      expect(store.isEnabled('newShell')).toBe(true)
    })
  })

  describe('resetAll', () => {
    it('should restore all flags to defaults', () => {
      store.setFlag('newAuth', false)
      store.setFlag('newShell', false)

      store.resetAll()

      expect(store.isEnabled('newAuth')).toBe(true)
      expect(store.isEnabled('newShell')).toBe(true)
    })
  })

  describe('persist and restore', () => {
    it('should round-trip flags via localStorage', () => {
      store.setFlag('newAccess', false)
      store.setFlag('newOps', false)

      // Create a new store instance to simulate app reload
      setActivePinia(createPinia())
      const store2 = useFeatureFlagStore()
      store2.restore()

      expect(store2.isEnabled('newAccess')).toBe(false)
      expect(store2.isEnabled('newOps')).toBe(false)
      expect(store2.isEnabled('newShell')).toBe(true)
    })

    it('should handle invalid JSON in localStorage gracefully', () => {
      localStorage.setItem('taskdeck_feature_flags', 'not-valid-json')

      store.restore()

      // Should fall back to defaults
      for (const key of Object.keys(defaultFeatureFlags) as (keyof typeof defaultFeatureFlags)[]) {
        expect(store.isEnabled(key)).toBe(defaultFeatureFlags[key])
      }
    })

    it('should use defaults when no flags are saved in localStorage', () => {
      // localStorage is clear from beforeEach
      store.restore()

      for (const key of Object.keys(defaultFeatureFlags) as (keyof typeof defaultFeatureFlags)[]) {
        expect(store.isEnabled(key)).toBe(defaultFeatureFlags[key])
      }
    })
  })

  describe('allEnabled', () => {
    it('should be false when defaults include disabled flags', () => {
      expect(store.allEnabled).toBe(false)
    })

    it('should be true when all flags are explicitly enabled', () => {
      for (const key of Object.keys(defaultFeatureFlags) as (keyof typeof defaultFeatureFlags)[]) {
        store.setFlag(key, true)
      }

      expect(store.allEnabled).toBe(true)
    })

    it('should be false when any flag is disabled after full enablement', () => {
      for (const key of Object.keys(defaultFeatureFlags) as (keyof typeof defaultFeatureFlags)[]) {
        store.setFlag(key, true)
      }
      store.setFlag('newAuth', false)

      expect(store.allEnabled).toBe(false)
    })
  })
})
