import { defineStore } from 'pinia'
import { ref, watch } from 'vue'
import { queueApi } from '../api/queueApi'
import { useToastStore } from './toastStore'
import { useSessionStore } from './sessionStore'
import { isDemoMode, DemoModeError } from '../utils/demoMode'
import type { QueueRequest, CreateQueueRequestDto, QueueStats } from '../types/queue'
import { getErrorDisplay } from '../composables/useErrorMapper'

export const useQueueStore = defineStore('queue', () => {
  const toast = useToastStore()
  const session = useSessionStore()

  const requests = ref<QueueRequest[]>([])
  const stats = ref<QueueStats | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  type ReadLane = 'requests' | 'stats'

  interface OperationOwner {
    epoch: number
    token: symbol
  }

  interface ReadOwner extends OperationOwner {
    observedMutationGeneration: number
  }

  let credentialEpoch = 0
  let mutationGeneration = 0
  const activeOperations = new Set<symbol>()
  const readOwners = new Map<ReadLane, ReadOwner>()

  function syncLoading(): void {
    loading.value = activeOperations.size > 0
  }

  function beginOperation(label: string): OperationOwner {
    const owner = { epoch: credentialEpoch, token: Symbol(label) }
    activeOperations.add(owner.token)
    error.value = null
    syncLoading()
    return owner
  }

  function ownsCredential(owner: OperationOwner): boolean {
    return owner.epoch === credentialEpoch
  }

  function finishOperation(owner: OperationOwner): void {
    if (!ownsCredential(owner)) return
    activeOperations.delete(owner.token)
    syncLoading()
  }

  function beginRead(lane: ReadLane): ReadOwner {
    const previous = readOwners.get(lane)
    if (previous?.epoch === credentialEpoch) {
      activeOperations.delete(previous.token)
    }

    const operation = beginOperation(`read:${lane}`)
    const owner = {
      ...operation,
      observedMutationGeneration: mutationGeneration,
    }
    readOwners.set(lane, owner)
    return owner
  }

  function ownsRead(lane: ReadLane, owner: ReadOwner): boolean {
    return ownsCredential(owner)
      && readOwners.get(lane)?.token === owner.token
      && owner.observedMutationGeneration === mutationGeneration
  }

  function finishRead(lane: ReadLane, owner: ReadOwner): void {
    if (readOwners.get(lane)?.token === owner.token) {
      readOwners.delete(lane)
    }
    finishOperation(owner)
  }

  function invalidateRead(lane: ReadLane): void {
    const owner = readOwners.get(lane)
    if (owner?.epoch === credentialEpoch) {
      activeOperations.delete(owner.token)
    }
    readOwners.delete(lane)
    syncLoading()
  }

  function recordMutation(): void {
    mutationGeneration += 1
    invalidateRead('requests')
    invalidateRead('stats')
  }

  function $reset(): void {
    credentialEpoch += 1
    mutationGeneration = 0
    activeOperations.clear()
    readOwners.clear()
    requests.value = []
    stats.value = null
    loading.value = false
    error.value = null
  }

  watch(
    () => [session.userId, session.token, session.isAuthenticated, session.isDemo],
    $reset,
    { flush: 'sync' },
  )

  function guardDemoMutation(): never | void {
    if (isDemoMode) {
      toast.info('This action is view-only in demo mode.')
      throw new DemoModeError()
    }
  }

  async function fetchUserRequests() {
    if (isDemoMode) {
      invalidateRead('requests')
      error.value = null
      requests.value = []
      return
    }

    const owner = beginRead('requests')
    try {
      session.requireUserId('queue operations')
      const result = await queueApi.getUserRequests()
      if (ownsRead('requests', owner)) requests.value = result
    } catch (e: unknown) {
      if (ownsRead('requests', owner)) {
        const msg = getErrorDisplay(e, 'Failed to fetch queue requests').message
        error.value = msg
        toast.error(msg)
      }
      throw e
    } finally {
      finishRead('requests', owner)
    }
  }

  async function fetchByStatus(status: string) {
    if (isDemoMode) {
      invalidateRead('requests')
      error.value = null
      requests.value = []
      return
    }

    const owner = beginRead('requests')
    try {
      const result = await queueApi.getRequestsByStatus(status)
      if (ownsRead('requests', owner)) requests.value = result
    } catch (e: unknown) {
      if (ownsRead('requests', owner)) {
        const msg = getErrorDisplay(e, 'Failed to fetch requests by status').message
        error.value = msg
        toast.error(msg)
      }
      throw e
    } finally {
      finishRead('requests', owner)
    }
  }

  async function submitRequest(dto: CreateQueueRequestDto) {
    guardDemoMutation()
    const owner = beginOperation('submit-request')
    try {
      session.requireUserId('queue operations')
      const request = await queueApi.createRequest(dto)
      if (!ownsCredential(owner)) return request

      recordMutation()
      requests.value.push(request)
      toast.success('Request submitted')
      return request
    } catch (e: unknown) {
      if (ownsCredential(owner)) {
        const msg = getErrorDisplay(e, 'Failed to submit request').message
        error.value = msg
        toast.error(msg)
      }
      throw e
    } finally {
      finishOperation(owner)
    }
  }

  async function cancelRequest(requestId: string) {
    guardDemoMutation()
    const owner = beginOperation(`cancel-request:${requestId}`)
    try {
      session.requireUserId('queue operations')
      await queueApi.cancelRequest(requestId)
      if (!ownsCredential(owner)) return

      recordMutation()
      requests.value = requests.value.filter(r => r.id !== requestId)
      toast.success('Request cancelled')
    } catch (e: unknown) {
      if (ownsCredential(owner)) {
        const msg = getErrorDisplay(e, 'Failed to cancel request').message
        error.value = msg
        toast.error(msg)
      }
      throw e
    } finally {
      finishOperation(owner)
    }
  }

  async function processNext() {
    guardDemoMutation()
    const owner = beginOperation('process-next')
    try {
      const result = await queueApi.processNext()
      if (!ownsCredential(owner)) return result

      if (result) {
        recordMutation()
        toast.success('Request processed')
      } else {
        toast.info('No pending requests')
      }
      return result
    } catch (e: unknown) {
      if (ownsCredential(owner)) {
        const msg = getErrorDisplay(e, 'Failed to process request').message
        error.value = msg
        toast.error(msg)
      }
      throw e
    } finally {
      finishOperation(owner)
    }
  }

  async function fetchStats() {
    if (isDemoMode) {
      invalidateRead('stats')
      error.value = null
      stats.value = { pendingCount: 0, processingCount: 0, completedCount: 0, failedCount: 0 }
      return
    }

    const owner = beginRead('stats')
    try {
      const result = await queueApi.getStats()
      if (ownsRead('stats', owner)) stats.value = result
    } catch (e: unknown) {
      if (ownsRead('stats', owner)) {
        const msg = getErrorDisplay(e, 'Failed to fetch queue stats').message
        error.value = msg
        toast.error(msg)
      }
      throw e
    } finally {
      finishRead('stats', owner)
    }
  }

  return {
    requests,
    stats,
    loading,
    error,
    fetchUserRequests,
    fetchByStatus,
    submitRequest,
    cancelRequest,
    processNext,
    fetchStats,
    $reset,
  }
})
