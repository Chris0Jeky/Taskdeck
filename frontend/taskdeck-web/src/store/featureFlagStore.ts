import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { FeatureFlags } from '../types/feature-flags'
import { defaultFeatureFlags } from '../types/feature-flags'

const FLAGS_KEY = 'taskdeck_feature_flags'
const FEATURE_FLAG_KEYS = Object.keys(defaultFeatureFlags) as Array<keyof FeatureFlags>

function normalizeFeatureFlags(raw: unknown): FeatureFlags {
  const normalized: FeatureFlags = { ...defaultFeatureFlags }
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return normalized

  const candidate = raw as Record<string, unknown>
  for (const key of FEATURE_FLAG_KEYS) {
    if (typeof candidate[key] === 'boolean') normalized[key] = candidate[key]
  }
  return normalized
}

export const useFeatureFlagStore = defineStore('featureFlags', () => {
  const flags = ref<FeatureFlags>({ ...defaultFeatureFlags })
  const persistenceError = ref<string | null>(null)
  let hasUnsavedChanges = false

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
    try {
      localStorage.setItem(FLAGS_KEY, JSON.stringify(normalizeFeatureFlags(flags.value)))
      hasUnsavedChanges = false
      persistenceError.value = null
    } catch {
      hasUnsavedChanges = true
      persistenceError.value = 'Feature flag changes are active for this session but could not be saved in browser storage.'
    }
  }

  function restore() {
    if (hasUnsavedChanges) return

    try {
      const saved = localStorage.getItem(FLAGS_KEY)
      if (!saved) return
      flags.value = normalizeFeatureFlags(JSON.parse(saved) as unknown)
    } catch {
      flags.value = { ...defaultFeatureFlags }
      persistenceError.value = 'Feature flags could not be loaded from browser storage; defaults are active.'
    }
  }

  const allEnabled = computed(() =>
    FEATURE_FLAG_KEYS.every((key) => flags.value[key])
  )

  return {
    flags,
    persistenceError,
    isEnabled,
    setFlag,
    resetAll,
    restore,
    allEnabled,
  }
})
