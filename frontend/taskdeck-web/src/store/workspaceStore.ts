import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { workspaceApi } from '../api/workspaceApi'
import { useSessionStore } from './sessionStore'
import { useToastStore } from './toastStore'
import { getErrorMessage } from '../utils/errorMessage'
import { WORKSPACE_MODE_STORAGE_KEY } from '../utils/storageKeys'
import { isDemoMode } from '../utils/demoMode'
import { DEMO_ONBOARDING, buildDemoHomeSummary, buildDemoTodaySummary } from '../utils/demoData'
import type {
  HomeSummary,
  TodaySummary,
  WorkspaceMode,
  WorkspaceOnboarding,
  WorkspaceOnboardingAction,
  WorkspacePreference,
} from '../types/workspace'
import { isWorkspaceMode } from '../types/workspace'

function getLocalWorkspaceMode(): WorkspaceMode {
  const savedMode = localStorage.getItem(WORKSPACE_MODE_STORAGE_KEY)
  return isWorkspaceMode(savedMode) ? savedMode : 'guided'
}

export const useWorkspaceStore = defineStore('workspace', () => {
  const session = useSessionStore()
  const toast = useToastStore()

  const mode = ref<WorkspaceMode>(getLocalWorkspaceMode())
  const preferenceLoading = ref(false)
  const preferenceError = ref<string | null>(null)
  const preferencesHydrated = ref(false)
  const onboarding = ref<WorkspaceOnboarding | null>(null)
  const homeSummary = ref<HomeSummary | null>(null)
  const homeLoading = ref(false)
  const homeError = ref<string | null>(null)
  const todaySummary = ref<TodaySummary | null>(null)
  const todayLoading = ref(false)
  const todayError = ref<string | null>(null)
  let preferenceRequestVersion = 0
  let pendingPreferenceRequests = 0

  const hasHomeSummary = computed(() => homeSummary.value !== null)
  const hasTodaySummary = computed(() => todaySummary.value !== null)
  const inboxBadgeCount = computed(() => homeSummary.value?.workload.capturesNeedingTriage ?? 0)
  const reviewBadgeCount = computed(() => homeSummary.value?.workload.proposalsPendingReview ?? 0)

  function persistLocalMode(nextMode: WorkspaceMode) {
    localStorage.setItem(WORKSPACE_MODE_STORAGE_KEY, nextMode)
  }

  function applyMode(nextMode: WorkspaceMode) {
    mode.value = nextMode
    persistLocalMode(nextMode)
  }

  function syncOnboarding(nextOnboarding: WorkspaceOnboarding | null) {
    onboarding.value = nextOnboarding

    if (homeSummary.value && nextOnboarding) {
      homeSummary.value = {
        ...homeSummary.value,
        onboarding: nextOnboarding,
      }
    }

    if (todaySummary.value && nextOnboarding) {
      todaySummary.value = {
        ...todaySummary.value,
        onboarding: nextOnboarding,
      }
    }
  }

  function beginPreferenceLoading() {
    pendingPreferenceRequests += 1
    preferenceLoading.value = true
  }

  function finishPreferenceLoading() {
    pendingPreferenceRequests = Math.max(0, pendingPreferenceRequests - 1)
    preferenceLoading.value = pendingPreferenceRequests > 0
  }

  function startVersionedPreferenceRequest(): number {
    beginPreferenceLoading()
    return ++preferenceRequestVersion
  }

  function isCurrentPreferenceRequest(version: number) {
    return version === preferenceRequestVersion
  }

  async function hydratePreferences(): Promise<WorkspacePreference | null> {
    if (!session.isAuthenticated) {
      preferencesHydrated.value = false
      return null
    }

    if (isDemoMode) {
      applyMode('guided')
      syncOnboarding(DEMO_ONBOARDING)
      preferencesHydrated.value = true
      return null
    }

    const requestVersion = startVersionedPreferenceRequest()

    try {
      preferenceError.value = null
      const preference = await workspaceApi.getPreferences()

      if (isCurrentPreferenceRequest(requestVersion)) {
        applyMode(preference.workspaceMode)
        syncOnboarding(preference.onboarding)
        preferencesHydrated.value = true
      }

      return preference
    } catch (e: unknown) {
      if (isCurrentPreferenceRequest(requestVersion)) {
        preferenceError.value = getErrorMessage(e, 'Failed to load workspace preferences')
      }

      return null
    } finally {
      finishPreferenceLoading()
    }
  }

  async function updateMode(nextMode: WorkspaceMode): Promise<void> {
    applyMode(nextMode)

    if (isDemoMode) {
      preferencesHydrated.value = true
      return
    }

    const requestVersion = startVersionedPreferenceRequest()

    if (!session.isAuthenticated) {
      preferencesHydrated.value = false
      finishPreferenceLoading()
      return
    }

    try {
      preferenceError.value = null
      const preference = await workspaceApi.updatePreferences({ workspaceMode: nextMode })

      if (isCurrentPreferenceRequest(requestVersion)) {
        applyMode(preference.workspaceMode)
        syncOnboarding(preference.onboarding)
        preferencesHydrated.value = true
      }
    } catch (e: unknown) {
      if (isCurrentPreferenceRequest(requestVersion)) {
        preferenceError.value = getErrorMessage(e, 'Failed to save workspace mode')
        preferencesHydrated.value = false
        toast.warning(`${preferenceError.value}. Keeping the local selection for now.`)
      }
    } finally {
      finishPreferenceLoading()
    }
  }

  async function fetchHomeSummary(): Promise<HomeSummary> {
    if (isDemoMode) {
      homeLoading.value = true
      homeError.value = null
      const summary = buildDemoHomeSummary()
      homeSummary.value = summary
      applyMode(summary.workspaceMode)
      syncOnboarding(summary.onboarding)
      homeLoading.value = false
      return summary
    }

    try {
      homeLoading.value = true
      homeError.value = null
      const summary = await workspaceApi.getHomeSummary()
      homeSummary.value = summary
      applyMode(summary.workspaceMode)
      syncOnboarding(summary.onboarding)
      return summary
    } catch (e: unknown) {
      homeError.value = getErrorMessage(e, 'Failed to load workspace summary')
      throw e
    } finally {
      homeLoading.value = false
    }
  }

  async function fetchTodaySummary(): Promise<TodaySummary> {
    if (isDemoMode) {
      todayLoading.value = true
      todayError.value = null
      const summary = buildDemoTodaySummary()
      todaySummary.value = summary
      applyMode(summary.workspaceMode)
      syncOnboarding(summary.onboarding)
      todayLoading.value = false
      return summary
    }

    try {
      todayLoading.value = true
      todayError.value = null
      const summary = await workspaceApi.getTodaySummary()
      todaySummary.value = summary
      applyMode(summary.workspaceMode)
      syncOnboarding(summary.onboarding)
      return summary
    } catch (e: unknown) {
      todayError.value = getErrorMessage(e, 'Failed to load today agenda')
      throw e
    } finally {
      todayLoading.value = false
    }
  }

  async function updateOnboarding(action: WorkspaceOnboardingAction): Promise<WorkspaceOnboarding> {
    if (isDemoMode) {
      const next: WorkspaceOnboarding = {
        ...DEMO_ONBOARDING,
        visibility: action === 'dismiss' ? 'dismissed' : 'active',
      }
      syncOnboarding(next)
      return next
    }

    try {
      beginPreferenceLoading()
      preferenceError.value = null
      const nextOnboarding = await workspaceApi.updateOnboarding({ action })
      syncOnboarding(nextOnboarding)
      return nextOnboarding
    } catch (e: unknown) {
      preferenceError.value = getErrorMessage(e, 'Failed to update onboarding state')
      toast.warning(preferenceError.value)
      throw e
    } finally {
      finishPreferenceLoading()
    }
  }

  function clearHomeSummary() {
    homeSummary.value = null
    homeError.value = null
  }

  function clearTodaySummary() {
    todaySummary.value = null
    todayError.value = null
  }

  function resetForLogout() {
    preferencesHydrated.value = false
    preferenceError.value = null
    onboarding.value = null
    clearHomeSummary()
    clearTodaySummary()
    applyMode(getLocalWorkspaceMode())
  }

  return {
    mode,
    preferenceLoading,
    preferenceError,
    preferencesHydrated,
    onboarding,
    homeSummary,
    homeLoading,
    homeError,
    todaySummary,
    todayLoading,
    todayError,
    hasHomeSummary,
    hasTodaySummary,
    inboxBadgeCount,
    reviewBadgeCount,
    hydratePreferences,
    updateMode,
    fetchHomeSummary,
    fetchTodaySummary,
    updateOnboarding,
    clearHomeSummary,
    clearTodaySummary,
    resetForLogout,
  }
})
