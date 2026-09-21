import { defineStore } from 'pinia'
import { ref, watch } from 'vue'
import { agentApi } from '../api/agentApi'
import { useToastStore } from './toastStore'
import { useSessionStore } from './sessionStore'
import { isDemoMode } from '../utils/demoMode'
import { getErrorDisplay } from '../composables/useErrorMapper'
import type { AgentProfile, AgentRun, AgentRunDetail } from '../types/agent'

export const useAgentStore = defineStore('agent', () => {
  const toast = useToastStore()
  const session = useSessionStore()

  const profiles = ref<AgentProfile[]>([])
  const profilesLoading = ref(false)
  const profilesError = ref<string | null>(null)

  const runs = ref<AgentRun[]>([])
  const runsLoading = ref(false)
  const runsError = ref<string | null>(null)

  const runDetail = ref<AgentRunDetail | null>(null)
  const runDetailLoading = ref(false)
  const runDetailError = ref<string | null>(null)

  type ReadLane = 'profiles' | 'runs' | 'detail'
  interface ReadOwner {
    epoch: number
    token: symbol
  }

  let sessionEpoch = 0
  const readOwners = new Map<ReadLane, ReadOwner>()

  function setLaneLoading(lane: ReadLane, value: boolean): void {
    if (lane === 'profiles') profilesLoading.value = value
    else if (lane === 'runs') runsLoading.value = value
    else runDetailLoading.value = value
  }

  function clearLaneError(lane: ReadLane): void {
    if (lane === 'profiles') profilesError.value = null
    else if (lane === 'runs') runsError.value = null
    else runDetailError.value = null
  }

  function beginRead(lane: ReadLane): ReadOwner {
    const owner = { epoch: sessionEpoch, token: Symbol(lane) }
    readOwners.set(lane, owner)
    clearLaneError(lane)
    setLaneLoading(lane, true)
    return owner
  }

  function ownsRead(lane: ReadLane, owner: ReadOwner): boolean {
    const current = readOwners.get(lane)
    return owner.epoch === sessionEpoch && current?.token === owner.token
  }

  function finishRead(lane: ReadLane, owner: ReadOwner): void {
    if (!ownsRead(lane, owner)) return
    readOwners.delete(lane)
    setLaneLoading(lane, false)
  }

  function invalidateLane(lane: ReadLane): void {
    readOwners.delete(lane)
    clearLaneError(lane)
    setLaneLoading(lane, false)
  }

  function invalidateReads(): void {
    sessionEpoch += 1
    readOwners.clear()
    profilesLoading.value = false
    runsLoading.value = false
    runDetailLoading.value = false
    profilesError.value = null
    runsError.value = null
    runDetailError.value = null
  }

  function resetForSession(): void {
    invalidateReads()
    profiles.value = []
    runs.value = []
    runDetail.value = null
  }

  watch(
    () => [session.userId, session.isAuthenticated, session.isDemo],
    resetForSession,
    { flush: 'sync' },
  )

  watch(
    () => session.token,
    invalidateReads,
    { flush: 'sync' },
  )

  async function fetchProfiles(): Promise<void> {
    if (isDemoMode) {
      invalidateLane('profiles')
      profiles.value = []
      return
    }

    const owner = beginRead('profiles')
    try {
      const result = await agentApi.listProfiles()
      if (!ownsRead('profiles', owner)) return
      profiles.value = result
    } catch (e: unknown) {
      if (ownsRead('profiles', owner)) {
        const msg = getErrorDisplay(e, 'Failed to load agent profiles').message
        profilesError.value = msg
        toast.error(msg)
      }
      throw e
    } finally {
      finishRead('profiles', owner)
    }
  }

  async function fetchRuns(agentId: string, limit = 100): Promise<void> {
    if (isDemoMode) {
      invalidateLane('runs')
      runs.value = []
      return
    }

    const owner = beginRead('runs')
    try {
      const result = await agentApi.listRuns(agentId, limit)
      if (!ownsRead('runs', owner)) return
      runs.value = result
    } catch (e: unknown) {
      if (ownsRead('runs', owner)) {
        const msg = getErrorDisplay(e, 'Failed to load agent runs').message
        runsError.value = msg
        toast.error(msg)
      }
      throw e
    } finally {
      finishRead('runs', owner)
    }
  }

  async function fetchRunDetail(agentId: string, runId: string): Promise<void> {
    if (isDemoMode) {
      invalidateLane('detail')
      runDetail.value = null
      return
    }

    const owner = beginRead('detail')
    try {
      const result = await agentApi.getRunDetail(agentId, runId)
      if (!ownsRead('detail', owner)) return
      runDetail.value = result
    } catch (e: unknown) {
      if (ownsRead('detail', owner)) {
        const msg = getErrorDisplay(e, 'Failed to load run details').message
        runDetailError.value = msg
        toast.error(msg)
      }
      throw e
    } finally {
      finishRead('detail', owner)
    }
  }

  function clearRuns(): void {
    invalidateLane('runs')
    runs.value = []
  }

  function clearRunDetail(): void {
    invalidateLane('detail')
    runDetail.value = null
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
