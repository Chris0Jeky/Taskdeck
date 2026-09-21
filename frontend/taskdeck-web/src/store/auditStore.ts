import { defineStore } from 'pinia'
import { ref, watch } from 'vue'
import { auditApi } from '../api/auditApi'
import { useToastStore } from './toastStore'
import { useSessionStore } from './sessionStore'
import { isDemoMode } from '../utils/demoMode'
import type { AuditEntry } from '../types/audit'
import { getErrorDisplay } from '../composables/useErrorMapper'

export const useAuditStore = defineStore('audit', () => {
  const toast = useToastStore()
  const session = useSessionStore()

  const entries = ref<AuditEntry[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)

  type ReadRetry = () => Promise<void>

  interface ReadOwner {
    epoch: number
    token: symbol
    successorReady: Promise<Promise<void>>
    resolveSuccessor: (successor: Promise<void>) => void
  }

  let credentialEpoch = 0
  let currentRead: ReadOwner | null = null
  let currentRetry: ReadRetry | null = null

  function clampLimit(limit: number): number {
    if (limit < 1) return 1
    if (limit > 100) return 100
    return limit
  }

  function beginRead(retry: ReadRetry): ReadOwner {
    let resolveSuccessor!: (successor: Promise<void>) => void
    const successorReady = new Promise<Promise<void>>((resolve) => {
      resolveSuccessor = resolve
    })
    const owner = {
      epoch: credentialEpoch,
      token: Symbol('audit-history'),
      successorReady,
      resolveSuccessor,
    }
    currentRead = owner
    currentRetry = retry
    error.value = null
    loading.value = true
    return owner
  }

  function ownsRead(owner: ReadOwner): boolean {
    return owner.epoch === credentialEpoch && currentRead?.token === owner.token
  }

  function finishRead(owner: ReadOwner): void {
    if (!ownsRead(owner)) return
    currentRead = null
    currentRetry = null
    loading.value = false
  }

  function invalidateCurrentRead(): void {
    credentialEpoch += 1
    currentRead = null
    currentRetry = null
    loading.value = false
    error.value = null
  }

  async function awaitSuccessor(owner: ReadOwner): Promise<boolean> {
    if (owner.epoch === credentialEpoch) return false
    await owner.successorReady.then((successor) => successor)
    return true
  }

  function retryActiveRead(): void {
    const retry = currentRead && currentRetry
      ? { owner: currentRead, retry: currentRetry }
      : null
    invalidateCurrentRead()
    if (!retry) return

    let resolveSuccessor!: () => void
    let rejectSuccessor!: (reason: unknown) => void
    const successor = new Promise<void>((resolve, reject) => {
      resolveSuccessor = resolve
      rejectSuccessor = reject
    })
    retry.owner.resolveSuccessor(successor)

    try {
      void retry.retry().then(resolveSuccessor, rejectSuccessor)
    } catch (error) {
      rejectSuccessor(error)
    }
    void successor.catch(() => {
      // The retried store action owns current error/toast state.
    })
  }

  function resetForSession(): void {
    const retiredRead = currentRead
    invalidateCurrentRead()
    retiredRead?.resolveSuccessor(Promise.resolve())
    entries.value = []
  }

  watch(
    () => [session.userId, session.isAuthenticated, session.isDemo],
    resetForSession,
    { flush: 'sync' },
  )

  watch(
    () => session.token,
    retryActiveRead,
    { flush: 'sync' },
  )

  async function fetchHistory(
    request: () => Promise<AuditEntry[]>,
    fallbackMessage: string,
    retry: ReadRetry,
  ): Promise<void> {
    const owner = beginRead(retry)
    try {
      const requestPromise = request()
      const outcome = await Promise.race([
        requestPromise.then((result) => ({ kind: 'request' as const, result })),
        owner.successorReady.then((successor) => successor.then(() => ({ kind: 'successor' as const }))),
      ])
      if (outcome.kind === 'successor') return

      const result = outcome.result
      if (!ownsRead(owner)) {
        await awaitSuccessor(owner)
        return
      }
      entries.value = result
    } catch (e: unknown) {
      if (ownsRead(owner)) {
        const msg = getErrorDisplay(e, fallbackMessage).message
        error.value = msg
        toast.error(msg)
      } else if (await awaitSuccessor(owner)) {
        return
      }
      throw e
    } finally {
      finishRead(owner)
    }
  }

  async function fetchBoardHistory(boardId: string, limit = 50) {
    if (isDemoMode) {
      resetForSession()
      return
    }

    await fetchHistory(
      () => auditApi.getBoardHistory(boardId, clampLimit(limit)),
      'Failed to fetch board history',
      () => fetchBoardHistory(boardId, limit),
    )
  }

  async function fetchEntityHistory(entityType: string, entityId: string, limit = 50) {
    if (isDemoMode) {
      resetForSession()
      return
    }

    await fetchHistory(
      () => auditApi.getEntityHistory(entityType, entityId, clampLimit(limit)),
      'Failed to fetch entity history',
      () => fetchEntityHistory(entityType, entityId, limit),
    )
  }

  async function fetchUserHistory(limit = 50) {
    if (isDemoMode) {
      resetForSession()
      return
    }

    await fetchHistory(
      () => auditApi.getUserHistory(clampLimit(limit)),
      'Failed to fetch user history',
      () => fetchUserHistory(limit),
    )
  }

  return {
    entries,
    loading,
    error,
    fetchBoardHistory,
    fetchEntityHistory,
    fetchUserHistory,
  }
})
