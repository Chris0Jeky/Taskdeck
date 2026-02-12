import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { FeatureFlags } from '../types/feature-flags'
import { defaultFeatureFlags } from '../types/feature-flags'

const FLAGS_KEY = 'taskdeck_feature_flags'

export const useFeatureFlagStore = defineStore('featureFlags', () => {
  const flags = ref<FeatureFlags>({ ...defaultFeatureFlags })

  function isEnabled(flag: keyof FeatureFlags): boolean {
    return flags.value[flag]
  }

  function setFlag(flag: keyof FeatureFlags, value: boolean) {
    flags.value[flag] = value
    persist()
  }

  function resetAll() {
    flags.value = { ...defaultFeatureFlags }
    persist()
  }

  function persist() {
    localStorage.setItem(FLAGS_KEY, JSON.stringify(flags.value))
  }

  function restore() {
    const saved = localStorage.getItem(FLAGS_KEY)
    if (saved) {
      try {
        const parsed = JSON.parse(saved)
        flags.value = { ...defaultFeatureFlags, ...parsed }
      } catch {
        flags.value = { ...defaultFeatureFlags }
      }
    }
  }

  const allEnabled = computed(() =>
    Object.values(flags.value).every(v => v)
  )

  return {
    flags,
    isEnabled,
    setFlag,
    resetAll,
    restore,
    allEnabled,
  }
})
