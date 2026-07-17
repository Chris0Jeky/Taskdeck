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
  let pendingPreferenceRequests = 0
  let todayRequestVersion = 0
  // Per-field preference version counters (issue #1343). Each explicit writer
  // bumps only the field(s) it writes — updateMode bumps mode, updateOnboarding
  // bumps onboarding, hydratePreferences bumps both — so concurrent actions on
  // DIFFERENT fields cannot suppress each other's response handling. (A shared
  // counter let an onboarding dismissal that started mid-save skip the failed
  // save's warning + unsaved flag entirely.)
  let modeRequestVersion = 0
  let onboardingRequestVersion = 0
  // Session-scoped unsaved-local-choice flag (issue #1343). Set when an explicit
  // updateMode save FAILS: the store keeps the local selection (and says so via
  // the warning toast), so summary responses must not re-apply the server's
  // stale mode until a later save succeeds or hydratePreferences confirms the
  // server matches the local choice. Deliberately not persisted: a full reload
  // starts a new session, which re-syncs from server truth.
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

  // Ordering guard for summary-derived preference state — mode and onboarding,
  // each guarded independently (issue #1343). A Home/Today summary must not
  // overwrite newer explicit preference actions. Per field, it applies only when:
  //   1. No preference request was already in flight when the summary request
  //      BEGAN (preferencePending) — the summary's server-side read may predate
  //      that request's commit even if the request settles first, so its
  //      preference fields are indistinguishable from stale.
  //   2. No writer for that field started after the summary began — writers bump
  //      their field's version at start, so an older captured version is stale.
  //   3. No preference request is in flight at APPLY time — defense-in-depth;
  //      provably redundant with 1+2 today (any writer starting after the
  //      snapshot bumps a version; writers pending at snapshot set
  //      preferencePending), but it protects against future writers that track
  //      pending without versioning.
  //   4. Mode only: no unsaved local choice (modeDirty) — after a FAILED save
  //      the versions are stable but the server still holds the old mode.
  type SummaryGuardSnapshot = {
    modeVersion: number
    onboardingVersion: number
    preferencePending: boolean
  }

  function captureSummaryGuardSnapshot(): SummaryGuardSnapshot {
    return {
      modeVersion: modeRequestVersion,
      onboardingVersion: onboardingRequestVersion,
      preferencePending: pendingPreferenceRequests > 0,
    }
  }

  function applySummaryPreferences(
    summary: HomeSummary | TodaySummary,
    snapshot: SummaryGuardSnapshot,
  ): { modeApplied: boolean; onboardingApplied: boolean } {
    const guardClear = !snapshot.preferencePending && pendingPreferenceRequests === 0
    const modeApplied =
      guardClear && modeRequestVersion === snapshot.modeVersion && !modeDirty
    const onboardingApplied =
      guardClear && onboardingRequestVersion === snapshot.onboardingVersion

    if (modeApplied) {
      applyMode(summary.workspaceMode)
    }
    if (onboardingApplied) {
      syncOnboarding(summary.onboarding)
    }

    return { modeApplied, onboardingApplied }
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

  function startModePreferenceRequest(): number {
    beginPreferenceLoading()
    return ++modeRequestVersion
  }

  function startOnboardingPreferenceRequest(): number {
    beginPreferenceLoading()
    return ++onboardingRequestVersion
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

    // Hydration writes both preference fields, so it versions both.
    beginPreferenceLoading()
    const modeVersion = ++modeRequestVersion
    const onboardingVersion = ++onboardingRequestVersion

    try {
      preferenceError.value = null
      const preference = await workspaceApi.getPreferences()

      if (onboardingRequestVersion === onboardingVersion) {
        // Onboarding has no unsaved local state, so the fresh server copy
        // applies unless a newer onboarding writer superseded this hydrate.
        syncOnboarding(preference.onboarding)
      }

      if (modeRequestVersion === modeVersion) {
        if (modeDirty && preference.workspaceMode !== mode.value) {
          // The server still holds the pre-failed-save mode. Keep the unsaved
          // local choice; preferencesHydrated stays false so the unsynced state
          // remains visible.
        } else {
          // Server matches the local choice (or nothing is unsaved): confirmed.
          modeDirty = false
          applyMode(preference.workspaceMode)
          preferencesHydrated.value = true
        }
      }

      return preference
    } catch (e: unknown) {
      if (modeRequestVersion === modeVersion) {
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

    const requestVersion = startModePreferenceRequest()
    const onboardingVersionAtStart = onboardingRequestVersion

    if (!session.isAuthenticated) {
      preferencesHydrated.value = false
      finishPreferenceLoading()
      return
    }

    try {
      preferenceError.value = null
      const preference = await workspaceApi.updatePreferences({ workspaceMode: nextMode })

      if (modeRequestVersion === requestVersion) {
        modeDirty = false
        applyMode(preference.workspaceMode)
        preferencesHydrated.value = true
      }
      // The response's onboarding is an incidental echo (a mode save does not
      // change onboarding server-side); it must not clobber a newer explicit
      // onboarding action that started while this save was in flight.
      if (onboardingRequestVersion === onboardingVersionAtStart) {
        syncOnboarding(preference.onboarding)
      }
    } catch (e: unknown) {
      if (modeRequestVersion === requestVersion) {
        // Keep the local selection AND remember it is unsaved so subsequent
        // summary fetches cannot silently revert it (issue #1343). Guarded by
        // the MODE version only: a concurrent onboarding action must not
        // suppress this failed-save handling.
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

    const guardSnapshot = captureSummaryGuardSnapshot()

    try {
      homeLoading.value = true
      homeError.value = null
      const summary = await workspaceApi.getHomeSummary()
      homeSummary.value = summary
      const { modeApplied, onboardingApplied } = applySummaryPreferences(summary, guardSnapshot)
      if (modeApplied) {
        preferencesHydrated.value = true
      }
      if (!onboardingApplied && onboarding.value) {
        // Stale-for-onboarding summary: keep the newer known onboarding visible
        // in the stored summary so views reading summary.onboarding do not
        // diverge from the guarded onboarding ref.
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
    const guardSnapshot = captureSummaryGuardSnapshot()

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
        const { onboardingApplied } = applySummaryPreferences(summary, guardSnapshot)
        if (!onboardingApplied && onboarding.value) {
          // Stale-for-onboarding summary: keep the newer known onboarding
          // visible (see Home).
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
      // Versioned per-field (issue #1343): an explicit onboarding action makes
      // in-flight summaries' ONBOARDING stale without touching the mode guard —
      // a dismissal that starts mid-save must not suppress the save's own
      // success/failure handling. Latest onboarding writer wins.
      const requestVersion = startOnboardingPreferenceRequest()
      preferenceError.value = null
      const nextOnboarding = await workspaceApi.updateOnboarding({ action })
      if (onboardingRequestVersion === requestVersion) {
        syncOnboarding(nextOnboarding)
      }
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
