import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { workspaceApi } from '../api/workspaceApi'
import { useSessionStore } from './sessionStore'
import { useToastStore } from './toastStore'
import { getErrorMessage } from '../utils/errorMessage'
import { WORKSPACE_MODE_STORAGE_KEY } from '../utils/storageKeys'
import type { HomeSummary, WorkspaceMode, WorkspacePreference } from '../types/workspace'

const workspaceModes = ['guided', 'workbench', 'agent'] as const

function isWorkspaceMode(value: string | null | undefined): value is WorkspaceMode {
  return workspaceModes.includes(value as WorkspaceMode)
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
  const homeSummary = ref<HomeSummary | null>(null)
  const homeLoading = ref(false)
  const homeError = ref<string | null>(null)

  const hasHomeSummary = computed(() => homeSummary.value !== null)

  function persistLocalMode(nextMode: WorkspaceMode) {
    localStorage.setItem(WORKSPACE_MODE_STORAGE_KEY, nextMode)
  }

  function applyMode(nextMode: WorkspaceMode) {
    mode.value = nextMode
    persistLocalMode(nextMode)
  }

  async function hydratePreferences(): Promise<WorkspacePreference | null> {
    if (!session.isAuthenticated) {
      preferencesHydrated.value = false
      return null
    }

    try {
      preferenceLoading.value = true
      preferenceError.value = null
      const preference = await workspaceApi.getPreferences()
      applyMode(preference.workspaceMode)
      preferencesHydrated.value = true
      return preference
    } catch (e: unknown) {
      preferenceError.value = getErrorMessage(e, 'Failed to load workspace preferences')
      return null
    } finally {
      preferenceLoading.value = false
    }
  }

  async function updateMode(nextMode: WorkspaceMode): Promise<void> {
    applyMode(nextMode)

    if (!session.isAuthenticated) {
      return
    }

    try {
      preferenceLoading.value = true
      preferenceError.value = null
      const preference = await workspaceApi.updatePreferences({ workspaceMode: nextMode })
      applyMode(preference.workspaceMode)
      preferencesHydrated.value = true
    } catch (e: unknown) {
      preferenceError.value = getErrorMessage(e, 'Failed to save workspace mode')
      preferencesHydrated.value = false
      toast.warning(`${preferenceError.value}. Keeping the local selection for now.`)
    } finally {
      preferenceLoading.value = false
    }
  }

  async function fetchHomeSummary(): Promise<HomeSummary> {
    try {
      homeLoading.value = true
      homeError.value = null
      const summary = await workspaceApi.getHomeSummary()
      homeSummary.value = summary
      applyMode(summary.workspaceMode)
      return summary
    } catch (e: unknown) {
      homeError.value = getErrorMessage(e, 'Failed to load workspace summary')
      throw e
    } finally {
      homeLoading.value = false
    }
  }

  function clearHomeSummary() {
    homeSummary.value = null
    homeError.value = null
  }

  function resetForLogout() {
    preferencesHydrated.value = false
    preferenceError.value = null
    clearHomeSummary()
    applyMode(getLocalWorkspaceMode())
  }

  return {
    mode,
    preferenceLoading,
    preferenceError,
    preferencesHydrated,
    homeSummary,
    homeLoading,
    homeError,
    hasHomeSummary,
    hydratePreferences,
    updateMode,
    fetchHomeSummary,
    clearHomeSummary,
    resetForLogout,
  }
})
