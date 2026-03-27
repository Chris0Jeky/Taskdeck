import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { workspaceApi } from '../api/workspaceApi'
import { useSessionStore } from './sessionStore'
import { useToastStore } from './toastStore'
import { getErrorMessage } from '../utils/errorMessage'
import { WORKSPACE_MODE_STORAGE_KEY } from '../utils/storageKeys'
import { isDemoMode } from '../utils/demoMode'
import type {
  HomeSummary,
  TodaySummary,
  WorkspaceMode,
  WorkspaceOnboarding,
  WorkspaceOnboardingAction,
  WorkspacePreference,
} from '../types/workspace'
import { isWorkspaceMode } from '../types/workspace'

const DEMO_ONBOARDING: WorkspaceOnboarding = {
  visibility: 'active',
  isComplete: false,
  currentStepId: 'capture',
  dismissedAt: null,
  completedAt: null,
  steps: [
    { stepId: 'board', title: 'Create a board', description: 'Set up a board to organise your work.', targetSurface: 'boards', isComplete: true },
    { stepId: 'capture', title: 'Capture a note', description: 'Drop a quick thought into the inbox.', targetSurface: 'capture', isComplete: false },
    { stepId: 'review', title: 'Review a proposal', description: 'Approve or reject a proposed change before it reaches a board.', targetSurface: 'review', isComplete: false },
  ],
}

function buildDemoHomeSummary(): HomeSummary {
  return {
    workspaceMode: 'guided',
    isFirstRun: false,
    onboarding: DEMO_ONBOARDING,
    workload: { capturesNeedingTriage: 3, capturesInProgress: 1, capturesReadyForFollowUp: 2, proposalsPendingReview: 1 },
    boards: {
      totalBoards: 2,
      recentBoardsCount: 2,
      recentBoards: [
        { id: 'demo-board-1', name: 'Product Backlog', description: 'Feature requests and bug reports.', updatedAt: new Date().toISOString() },
        { id: 'demo-board-2', name: 'Sprint 12', description: 'Current sprint work items.', updatedAt: new Date().toISOString() },
      ],
    },
    recommendedActions: [
      { actionId: 'review-proposals', title: 'Review proposals', description: 'One proposal is waiting for your decision.', targetSurface: 'review', attentionCount: 1 },
      { actionId: 'triage-captures', title: 'Triage inbox', description: 'Three captures need sorting.', targetSurface: 'capture', attentionCount: 3 },
    ],
  }
}

function buildDemoTodaySummary(): TodaySummary {
  return {
    workspaceMode: 'guided',
    onboarding: DEMO_ONBOARDING,
    summary: { capturesNeedingTriage: 3, proposalsPendingReview: 1, overdueCards: 1, dueTodayCards: 2, blockedCards: 0 },
    overdueCards: [
      { boardId: 'demo-board-1', boardName: 'Product Backlog', cardId: 'demo-card-1', title: 'Fix login redirect loop', dueDate: '2026-03-25T00:00:00Z', blockReason: null, updatedAt: new Date().toISOString() },
    ],
    dueTodayCards: [
      { boardId: 'demo-board-2', boardName: 'Sprint 12', cardId: 'demo-card-2', title: 'Add dark-mode toggle', dueDate: new Date().toISOString(), blockReason: null, updatedAt: new Date().toISOString() },
      { boardId: 'demo-board-2', boardName: 'Sprint 12', cardId: 'demo-card-3', title: 'Write onboarding copy', dueDate: new Date().toISOString(), blockReason: null, updatedAt: new Date().toISOString() },
    ],
    blockedCards: [],
    recommendedActions: [
      { actionId: 'review-proposals', title: 'Review proposals', description: 'One proposal is waiting for your decision.', targetSurface: 'review', attentionCount: 1 },
    ],
  }
}

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
    const requestVersion = startVersionedPreferenceRequest()
    applyMode(nextMode)

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
