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
  let todayRequestVersion = 0
  // Session-scoped unsaved-local-choice flag (issue #1343). Set when an explicit
  // updateMode save FAILS: the store keeps the local selection (and says so via
  // the warning toast), so summary responses must not re-apply the server's
  // stale preference state until a later save succeeds or hydratePreferences
  // confirms the server matches the local choice. Deliberately not persisted:
  // a full reload starts a new session, which re-syncs from server truth.
  let modeDirty = false

  const hasHomeSummary = computed(() => homeSummary.value !== null)
  const hasTodaySummary = computed(() => todaySummary.value !== null)
  const inboxBadgeCount = computed(() => homeSummary.value?.workload?.capturesNeedingTriage ?? 0)
  const reviewBadgeCount = computed(() => homeSummary.value?.workload?.proposalsPendingReview ?? 0)

  function persistLocalMode(nextMode: WorkspaceMode) {
    localStorage.setItem(WORKSPACE_MODE_STORAGE_KEY, nextMode)
  }

  function applyMode(nextMode: WorkspaceMode) {
    mode.value = nextMode
    persistLocalMode(nextMode)
  }

  // Ordering guard for summary-derived preference state — mode AND onboarding —
  // (issue #1343). A Home/Today summary must not overwrite newer explicit
  // preference actions. Its mode/onboarding are applied only when ALL hold:
  //   1. No preference action (updateMode / hydratePreferences / updateOnboarding)
  //      started after the summary request began — those bump
  //      preferenceRequestVersion at their start, so a summary that captured an
  //      older version is stale.
  //   2. No preference request is still in flight at apply time — its outcome is
  //      unknown, and the summary payload may predate its server-side commit;
  //      the preference request's own resolution applies the authoritative state.
  //   3. No unsaved local choice is pending (modeDirty) — after a FAILED save the
  //      version is stable but the server still holds the old state.
  // When all three hold the summary is genuinely newer and applies as normal.
  function canApplySummaryPreferences(preferenceVersionAtStart: number): boolean {
    return (
      preferenceRequestVersion === preferenceVersionAtStart &&
      pendingPreferenceRequests === 0 &&
      !modeDirty
    )
  }

  function applySummaryPreferences(
    summary: HomeSummary | TodaySummary,
    preferenceVersionAtStart: number,
  ): boolean {
    if (!canApplySummaryPreferences(preferenceVersionAtStart)) {
      return false
    }
    applyMode(summary.workspaceMode)
    syncOnboarding(summary.onboarding)
    return true
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
        if (modeDirty && preference.workspaceMode !== mode.value) {
          // The server still holds the pre-failed-save mode. Keep the unsaved
          // local choice; preferencesHydrated stays false so the unsynced state
          // remains visible. Onboarding has no unsaved local state, so the
          // fresh server copy applies.
          syncOnboarding(preference.onboarding)
        } else {
          // Server matches the local choice (or nothing is unsaved): confirmed.
          modeDirty = false
          applyMode(preference.workspaceMode)
          syncOnboarding(preference.onboarding)
          preferencesHydrated.value = true
        }
      }

      return preference
    } catch (e: unknown) {
      if (isCurrentPreferenceRequest(requestVersion)) {
        preferenceError.value = getErrorMessage(e, "We couldn't load your workspace preferences")
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
        modeDirty = false
        applyMode(preference.workspaceMode)
        syncOnboarding(preference.onboarding)
        preferencesHydrated.value = true
      }
    } catch (e: unknown) {
      if (isCurrentPreferenceRequest(requestVersion)) {
        // Keep the local selection AND remember it is unsaved so subsequent
        // summary fetches cannot silently revert it (issue #1343).
        modeDirty = true
        preferenceError.value = getErrorMessage(e, "We couldn't save this workspace mode")
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
      preferencesHydrated.value = true
      homeLoading.value = false
      return summary
    }

    const preferenceVersionAtStart = preferenceRequestVersion

    try {
      homeLoading.value = true
      homeError.value = null
      const summary = await workspaceApi.getHomeSummary()
      homeSummary.value = summary
      if (applySummaryPreferences(summary, preferenceVersionAtStart)) {
        preferencesHydrated.value = true
      } else if (onboarding.value) {
        // Stale summary: keep the newer known onboarding visible in the stored
        // summary so views reading summary.onboarding do not diverge from the
        // guarded onboarding ref.
        homeSummary.value = { ...summary, onboarding: onboarding.value }
      }
      return summary
    } catch (e: unknown) {
      homeError.value = getErrorMessage(e, "We couldn't load your workspace overview")
      throw e
    } finally {
      homeLoading.value = false
    }
  }

  async function fetchTodaySummary(): Promise<TodaySummary> {
    const requestVersion = ++todayRequestVersion
    const preferenceVersionAtStart = preferenceRequestVersion

    if (isDemoMode) {
      todayLoading.value = true
      todayError.value = null
      const summary = buildDemoTodaySummary()
      if (requestVersion === todayRequestVersion) {
        todaySummary.value = summary
        applyMode(summary.workspaceMode)
        syncOnboarding(summary.onboarding)
        todayLoading.value = false
      }
      return summary
    }

    try {
      todayLoading.value = true
      todayError.value = null
      const summary = await workspaceApi.getTodaySummary()
      if (requestVersion === todayRequestVersion) {
        todaySummary.value = summary
        if (!applySummaryPreferences(summary, preferenceVersionAtStart) && onboarding.value) {
          // Stale summary: keep the newer known onboarding visible (see Home).
          todaySummary.value = { ...summary, onboarding: onboarding.value }
        }
      }
      return summary
    } catch (e: unknown) {
      if (requestVersion === todayRequestVersion) {
        todayError.value = getErrorMessage(e, "We couldn't load today's overview")
      }
      throw e
    } finally {
      if (requestVersion === todayRequestVersion) {
        todayLoading.value = false
      }
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
      // Versioned like updateMode/hydratePreferences: an explicit onboarding
      // action makes in-flight summaries stale so a late summary cannot revert
      // it (issue #1343). The response itself applies unconditionally — it is
      // the authoritative result of this explicit action.
      startVersionedPreferenceRequest()
      preferenceError.value = null
      const nextOnboarding = await workspaceApi.updateOnboarding({ action })
      syncOnboarding(nextOnboarding)
      return nextOnboarding
    } catch (e: unknown) {
      preferenceError.value = getErrorMessage(e, "We couldn't update the setup guide")
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
    todayRequestVersion += 1
    todaySummary.value = null
    todayError.value = null
    todayLoading.value = false
  }

  function resetForLogout() {
    modeDirty = false
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
