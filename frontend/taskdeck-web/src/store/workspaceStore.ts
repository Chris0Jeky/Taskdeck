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
  // ── Preference ordering model (issue #1343) ────────────────────────────────
  // WRITES CONFIRM, NEVER RE-APPLY: updateMode/updateOnboarding apply the
  // user's intent locally at start; their HTTP responses never write field
  // values back into the store, so no response echo can cross fields or revert
  // newer intent. A write response only settles bookkeeping for its OWN field,
  // and only if it is still the latest write of that field: success clears the
  // field's dirty flag, failure sets it (keeping the local intent + warning).
  // READS (summaries, hydratePreferences) apply server state per field, and
  // only when that field is clean: no write of that field overlapped the read
  // and the field has no unsaved local intent. Reads never bump write versions.
  let modeRequestVersion = 0
  let onboardingRequestVersion = 0
  let pendingModeWrites = 0
  let pendingOnboardingWrites = 0
  // Session-scoped unsaved-local-intent flags. Set when the field's write
  // FAILS while local intent is applied; cleared when a later write of the
  // field succeeds or hydratePreferences confirms the server matches the local
  // intent. Deliberately not persisted: a full reload starts a new session,
  // which re-syncs from server truth.
  let modeDirty = false
  let onboardingDirty = false

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

  // Read-guard snapshot, captured synchronously when a read (summary or
  // preferences hydrate) begins. A field from a read applies only when NO write
  // of that field overlapped the read:
  //   1. none was pending when the read began — the read's server-side result
  //      may predate that write's commit even if the write settles first;
  //   2. none started after the read began (field version unchanged);
  //   3. none is pending at apply time — defense-in-depth; provably redundant
  //      with 1+2 today, but protects future writers that track pending
  //      without versioning.
  // Summaries additionally require the field to have no unsaved local intent
  // (dirty flag); hydratePreferences instead RECONCILES dirty state (see there).
  type PreferenceReadSnapshot = {
    modeVersion: number
    onboardingVersion: number
    modeWritePending: boolean
    onboardingWritePending: boolean
  }

  function capturePreferenceReadSnapshot(): PreferenceReadSnapshot {
    return {
      modeVersion: modeRequestVersion,
      onboardingVersion: onboardingRequestVersion,
      modeWritePending: pendingModeWrites > 0,
      onboardingWritePending: pendingOnboardingWrites > 0,
    }
  }

  function isModeReadClear(snapshot: PreferenceReadSnapshot): boolean {
    return (
      !snapshot.modeWritePending &&
      modeRequestVersion === snapshot.modeVersion &&
      pendingModeWrites === 0
    )
  }

  function isOnboardingReadClear(snapshot: PreferenceReadSnapshot): boolean {
    return (
      !snapshot.onboardingWritePending &&
      onboardingRequestVersion === snapshot.onboardingVersion &&
      pendingOnboardingWrites === 0
    )
  }

  function applySummaryPreferences(
    summary: HomeSummary | TodaySummary,
    snapshot: PreferenceReadSnapshot,
  ): { modeApplied: boolean; onboardingApplied: boolean } {
    const modeApplied = isModeReadClear(snapshot) && !modeDirty
    const onboardingApplied = isOnboardingReadClear(snapshot) && !onboardingDirty

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

    // Hydration is a READ: it snapshots the guards and applies per field only
    // when that field is clean; unlike summaries it can RECONCILE dirty state.
    beginPreferenceLoading()
    const snapshot = capturePreferenceReadSnapshot()

    try {
      preferenceError.value = null
      const preference = await workspaceApi.getPreferences()

      if (isOnboardingReadClear(snapshot)) {
        if (
          onboardingDirty &&
          onboarding.value &&
          preference.onboarding?.visibility !== onboarding.value.visibility
        ) {
          // The server still holds the pre-failed-action onboarding. Keep the
          // unsaved local intent.
        } else {
          onboardingDirty = false
          syncOnboarding(preference.onboarding)
        }
      }

      if (isModeReadClear(snapshot)) {
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
      if (isModeReadClear(snapshot)) {
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

    const requestVersion = ++modeRequestVersion
    pendingModeWrites += 1
    beginPreferenceLoading()

    if (!session.isAuthenticated) {
      preferencesHydrated.value = false
      pendingModeWrites -= 1
      finishPreferenceLoading()
      return
    }

    try {
      preferenceError.value = null
      await workspaceApi.updatePreferences({ workspaceMode: nextMode })

      if (modeRequestVersion === requestVersion) {
        // Confirm-only: the locally-applied mode is authoritative for this
        // locally-initiated write. The response's field values are never
        // applied — in particular its onboarding echo is dead by construction
        // and cannot revert a concurrent explicit onboarding action.
        modeDirty = false
        preferencesHydrated.value = true
      }
    } catch (e: unknown) {
      if (modeRequestVersion === requestVersion) {
        // Keep the local selection AND remember it is unsaved so subsequent
        // reads cannot silently revert it (issue #1343). Guarded by the MODE
        // version only: a concurrent onboarding action must not suppress this
        // failed-save handling.
        modeDirty = true
        preferenceError.value = getErrorMessage(e, "We couldn't save this workspace mode")
        preferencesHydrated.value = false
        toast.warning(`${preferenceError.value}. Keeping the local selection for now.`)
      }
    } finally {
      pendingModeWrites -= 1
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

    const guardSnapshot = capturePreferenceReadSnapshot()

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
    const guardSnapshot = capturePreferenceReadSnapshot()

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

    // Optimistic local intent (mirrors updateMode): the visibility change the
    // action requests is applied immediately from the best-known onboarding
    // state; the write response then confirms rather than re-applies. The full
    // server-computed object (steps, timestamps) arrives via the next clean
    // read (summary/hydrate) once the write has settled.
    const optimisticBase =
      onboarding.value ?? homeSummary.value?.onboarding ?? todaySummary.value?.onboarding ?? null
    const appliedOptimistic = optimisticBase !== null
    if (optimisticBase) {
      syncOnboarding({
        ...optimisticBase,
        visibility: action === 'dismiss' ? 'dismissed' : 'active',
      })
    }

    const requestVersion = ++onboardingRequestVersion
    pendingOnboardingWrites += 1
    beginPreferenceLoading()

    try {
      preferenceError.value = null
      const nextOnboarding = await workspaceApi.updateOnboarding({ action })
      if (onboardingRequestVersion === requestVersion) {
        onboardingDirty = false
        if (!appliedOptimistic) {
          // Bootstrap: no local onboarding existed to patch, so adopt this
          // action's authoritative result as initial state. Not an echo
          // overwrite — this is still the latest onboarding write, so no newer
          // local intent can exist.
          syncOnboarding(nextOnboarding)
        }
      }
      return nextOnboarding
    } catch (e: unknown) {
      if (onboardingRequestVersion === requestVersion) {
        if (appliedOptimistic) {
          // Local intent stays applied; flag it unsaved so reads cannot
          // silently revert it (mirrors the failed-mode-save semantics).
          onboardingDirty = true
        }
        preferenceError.value = getErrorMessage(e, "We couldn't update the setup guide")
        toast.warning(preferenceError.value)
      }
      throw e
    } finally {
      pendingOnboardingWrites -= 1
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
    onboardingDirty = false
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
