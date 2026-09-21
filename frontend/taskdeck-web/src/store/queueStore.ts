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
  type OperationScope = ReadLane | 'mutation'
  type ReadRetry = () => Promise<void>

  interface OperationOwner {
    epoch: number
    token: symbol
    scope: OperationScope
    userId: string | null
  }

  interface ReadOwner extends OperationOwner {
    observedMutationGeneration: number
  }

  let credentialEpoch = 0
  let mutationGeneration = 0
  let errorOwner: OperationOwner | null = null
  const activeOperations = new Set<symbol>()
  const readOwners = new Map<ReadLane, ReadOwner>()
  const readRetries = new Map<ReadLane, ReadRetry>()
  const successorReads = new Map<symbol, Promise<void>>()
  let activeRequestRetry: ReadRetry | null = null

  function syncLoading(): void {
    loading.value = activeOperations.size > 0
  }

  function clearErrorForScope(scope: OperationScope): void {
    if (errorOwner && errorOwner.epoch === credentialEpoch && errorOwner.scope !== scope) return
    errorOwner = null
    error.value = null
  }

  function publishError(owner: OperationOwner, message: string): void {
    errorOwner = owner
    error.value = message
  }

  function beginOperation(label: string, scope: OperationScope): OperationOwner {
    const owner = { epoch: credentialEpoch, token: Symbol(label), scope, userId: session.userId }
    activeOperations.add(owner.token)
    clearErrorForScope(scope)
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

  function beginRead(lane: ReadLane, retry: ReadRetry): ReadOwner {
    const previous = readOwners.get(lane)
    if (previous?.epoch === credentialEpoch) {
      activeOperations.delete(previous.token)
    }

    const operation = beginOperation(`read:${lane}`, lane)
    const owner = {
      ...operation,
      observedMutationGeneration: mutationGeneration,
    }
    readOwners.set(lane, owner)
    readRetries.set(lane, retry)
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
      readRetries.delete(lane)
    }
    finishOperation(owner)
  }

  function invalidateRead(lane: ReadLane): void {
    const owner = readOwners.get(lane)
    if (owner?.epoch === credentialEpoch) {
      activeOperations.delete(owner.token)
    }
    readOwners.delete(lane)
    readRetries.delete(lane)
    syncLoading()
  }

  function recordMutation(): void {
    mutationGeneration += 1
    invalidateRead('requests')
    invalidateRead('stats')
  }

  function invalidateOperations(): void {
    credentialEpoch += 1
    activeOperations.clear()
    readOwners.clear()
    readRetries.clear()
    errorOwner = null
    loading.value = false
    error.value = null
  }

  function canReportMutationFailure(owner: OperationOwner): boolean {
    return ownsCredential(owner)
      || (owner.userId !== null
        && owner.userId === session.userId
        && session.isAuthenticated
        && !session.isDemo)
  }

  async function awaitSuccessor(owner: ReadOwner): Promise<boolean> {
    const successor = successorReads.get(owner.token)
    if (!successor) return false

    try {
      await successor
    } finally {
      successorReads.delete(owner.token)
    }
    return true
  }

  function retryActiveReads(): void {
    if (!session.isAuthenticated || session.userId === null || session.isDemo) {
      $reset()
      return
    }

    const requestRetry = readOwners.has('requests')
      ? { owner: readOwners.get('requests')!, retry: readRetries.get('requests') }
      : undefined
    const statsRetry = readOwners.has('stats')
      ? { owner: readOwners.get('stats')!, retry: readRetries.get('stats') }
      : undefined

    invalidateOperations()
    for (const entry of [requestRetry, statsRetry]) {
      if (!entry?.retry) continue
      const successor = entry.retry()
      successorReads.set(entry.owner.token, successor)
      void successor.catch(() => {
        // The retried store action owns current error/toast state.
      })
    }
  }

  async function reconcileStaleMutation(owner: OperationOwner): Promise<void> {
    // A same-user token rotation can let the server commit a mutation after this
    // store retires its operation owner. Refresh both affected read lanes under
    // the replacement credential so a successful write is not hidden by stale
    // request rows or counts. Identity changes and logout must not read for the
    // retired session.
    if (ownsCredential(owner)
      || owner.userId === null
      || owner.userId !== session.userId
      || !session.isAuthenticated
      || session.isDemo) {
      return
    }

    try {
      const refreshes: Promise<void>[] = []
      if (!readOwners.has('requests')) {
        const refreshRequests = activeRequestRetry ?? fetchUserRequests
        refreshes.push(refreshRequests())
      }
      if (!readOwners.has('stats')) {
        refreshes.push(fetchStats())
      }
      await Promise.all(refreshes)
    } catch {
      // Each read owns its error/toast state. The mutation already succeeded,
      // so a reconciliation failure must not turn it into a false write error.
    }
  }

  function $reset(): void {
    invalidateOperations()
    successorReads.clear()
    activeRequestRetry = null
    mutationGeneration = 0
    requests.value = []
    stats.value = null
  }

  watch(
    () => [session.userId, session.isAuthenticated, session.isDemo],
    $reset,
    { flush: 'sync' },
  )

  watch(
    () => session.token,
    retryActiveReads,
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
      clearErrorForScope('requests')
      requests.value = []
      return
    }

    const retry = fetchUserRequests
    activeRequestRetry = retry
    const owner = beginRead('requests', retry)
    try {
      session.requireUserId('queue operations')
      const result = await queueApi.getUserRequests()
      if (ownsRead('requests', owner)) {
        requests.value = result
      } else {
        await awaitSuccessor(owner)
      }
    } catch (e: unknown) {
      if (ownsRead('requests', owner)) {
        const msg = getErrorDisplay(e, 'Failed to fetch queue requests').message
        publishError(owner, msg)
        toast.error(msg)
      } else if (await awaitSuccessor(owner)) {
        return
      }
      throw e
    } finally {
      finishRead('requests', owner)
    }
  }

  async function fetchByStatus(status: string) {
    if (isDemoMode) {
      invalidateRead('requests')
      clearErrorForScope('requests')
      requests.value = []
      return
    }

    const retry = () => fetchByStatus(status)
    activeRequestRetry = retry
    const owner = beginRead('requests', retry)
    try {
      const result = await queueApi.getRequestsByStatus(status)
      if (ownsRead('requests', owner)) {
        requests.value = result
      } else {
        await awaitSuccessor(owner)
      }
    } catch (e: unknown) {
      if (ownsRead('requests', owner)) {
        const msg = getErrorDisplay(e, 'Failed to fetch requests by status').message
        publishError(owner, msg)
        toast.error(msg)
      } else if (await awaitSuccessor(owner)) {
        return
      }
      throw e
    } finally {
      finishRead('requests', owner)
    }
  }

  async function submitRequest(dto: CreateQueueRequestDto) {
    guardDemoMutation()
    const owner = beginOperation('submit-request', 'mutation')
    try {
      session.requireUserId('queue operations')
      const request = await queueApi.createRequest(dto)
      if (!ownsCredential(owner)) {
        await reconcileStaleMutation(owner)
        return request
      }

      recordMutation()
      requests.value.push(request)
      toast.success('Request submitted')
      return request
    } catch (e: unknown) {
      if (canReportMutationFailure(owner)) {
        const msg = getErrorDisplay(e, 'Failed to submit request').message
        publishError(owner, msg)
        toast.error(msg)
      }
      throw e
    } finally {
      finishOperation(owner)
    }
  }

  async function cancelRequest(requestId: string) {
    guardDemoMutation()
    const owner = beginOperation(`cancel-request:${requestId}`, 'mutation')
    try {
      session.requireUserId('queue operations')
      await queueApi.cancelRequest(requestId)
      if (!ownsCredential(owner)) {
        await reconcileStaleMutation(owner)
        return
      }

      recordMutation()
      requests.value = requests.value.filter(r => r.id !== requestId)
      toast.success('Request cancelled')
    } catch (e: unknown) {
      if (canReportMutationFailure(owner)) {
        const msg = getErrorDisplay(e, 'Failed to cancel request').message
        publishError(owner, msg)
        toast.error(msg)
      }
      throw e
    } finally {
      finishOperation(owner)
    }
  }

  async function processNext() {
    guardDemoMutation()
    const owner = beginOperation('process-next', 'mutation')
    try {
      const result = await queueApi.processNext()
      if (!ownsCredential(owner)) {
        if (result) await reconcileStaleMutation(owner)
        return result
      }

      if (result) {
        recordMutation()
        toast.success('Request processed')
      } else {
        toast.info('No pending requests')
      }
      return result
    } catch (e: unknown) {
      if (canReportMutationFailure(owner)) {
        const msg = getErrorDisplay(e, 'Failed to process request').message
        publishError(owner, msg)
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
      clearErrorForScope('stats')
      stats.value = { pendingCount: 0, processingCount: 0, completedCount: 0, failedCount: 0 }
      return
    }

    const owner = beginRead('stats', fetchStats)
    try {
      const result = await queueApi.getStats()
      if (ownsRead('stats', owner)) {
        stats.value = result
      } else {
        await awaitSuccessor(owner)
      }
    } catch (e: unknown) {
      if (ownsRead('stats', owner)) {
        const msg = getErrorDisplay(e, 'Failed to fetch queue stats').message
        publishError(owner, msg)
        toast.error(msg)
      } else if (await awaitSuccessor(owner)) {
        return
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
