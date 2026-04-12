import { defineStore } from 'pinia'
import { ref } from 'vue'
import { agentApi } from '../api/agentApi'
import { useToastStore } from './toastStore'
import { isDemoMode } from '../utils/demoMode'
import { getErrorDisplay } from '../composables/useErrorMapper'
import type { AgentProfile, AgentRun, AgentRunDetail } from '../types/agent'

export const useAgentStore = defineStore('agent', () => {
  const toast = useToastStore()

  const profiles = ref<AgentProfile[]>([])
  const profilesLoading = ref(false)
  const profilesError = ref<string | null>(null)

  const runs = ref<AgentRun[]>([])
  const runsLoading = ref(false)
  const runsError = ref<string | null>(null)

  const runDetail = ref<AgentRunDetail | null>(null)
  const runDetailLoading = ref(false)
  const runDetailError = ref<string | null>(null)

  async function fetchProfiles(): Promise<void> {
    if (isDemoMode) {
      profilesLoading.value = true
      profilesError.value = null
      profiles.value = []
      profilesLoading.value = false
      return
    }
    try {
      profilesLoading.value = true
      profilesError.value = null
      profiles.value = await agentApi.listProfiles()
    } catch (e: unknown) {
      const msg = getErrorDisplay(e, 'Failed to load agent profiles').message
      profilesError.value = msg
      toast.error(msg)
      throw e
    } finally {
      profilesLoading.value = false
    }
  }

  async function fetchRuns(agentId: string, limit = 100): Promise<void> {
    if (isDemoMode) {
      runsLoading.value = true
      runsError.value = null
      runs.value = []
      runsLoading.value = false
      return
    }
    try {
      runsLoading.value = true
      runsError.value = null
      runs.value = await agentApi.listRuns(agentId, limit)
    } catch (e: unknown) {
      const msg = getErrorDisplay(e, 'Failed to load agent runs').message
      runsError.value = msg
      toast.error(msg)
      throw e
    } finally {
      runsLoading.value = false
    }
  }

  async function fetchRunDetail(agentId: string, runId: string): Promise<void> {
    if (isDemoMode) {
      runDetailLoading.value = true
      runDetailError.value = null
      runDetail.value = null
      runDetailLoading.value = false
      return
    }
    try {
      runDetailLoading.value = true
      runDetailError.value = null
      runDetail.value = await agentApi.getRunDetail(agentId, runId)
    } catch (e: unknown) {
      const msg = getErrorDisplay(e, 'Failed to load run details').message
      runDetailError.value = msg
      toast.error(msg)
      throw e
    } finally {
      runDetailLoading.value = false
    }
  }

  function clearRuns(): void {
    runs.value = []
    runsError.value = null
  }

  function clearRunDetail(): void {
    runDetail.value = null
    runDetailError.value = null
  }

  return {
    profiles,
    profilesLoading,
    profilesError,
    runs,
    runsLoading,
    runsError,
    runDetail,
    runDetailLoading,
    runDetailError,
    fetchProfiles,
    fetchRuns,
    fetchRunDetail,
    clearRuns,
    clearRunDetail,
  }
})
