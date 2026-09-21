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

  interface ReadOwner {
    epoch: number
    token: symbol
  }

  let credentialEpoch = 0
  let currentRead: ReadOwner | null = null

  function clampLimit(limit: number): number {
    if (limit < 1) return 1
    if (limit > 100) return 100
    return limit
  }

  function beginRead(): ReadOwner {
    const owner = { epoch: credentialEpoch, token: Symbol('audit-history') }
    currentRead = owner
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
    loading.value = false
  }

  function invalidateCurrentRead(): void {
    credentialEpoch += 1
    currentRead = null
    loading.value = false
    error.value = null
  }

  function resetForSession(): void {
    invalidateCurrentRead()
    entries.value = []
  }

  watch(
    () => [session.userId, session.isAuthenticated, session.isDemo],
    resetForSession,
    { flush: 'sync' },
  )

  watch(
    () => session.token,
    invalidateCurrentRead,
    { flush: 'sync' },
  )

  async function fetchHistory(
    request: () => Promise<AuditEntry[]>,
    fallbackMessage: string,
  ): Promise<void> {
    const owner = beginRead()
    try {
      const result = await request()
      if (!ownsRead(owner)) return
      entries.value = result
    } catch (e: unknown) {
      if (ownsRead(owner)) {
        const msg = getErrorDisplay(e, fallbackMessage).message
        error.value = msg
        toast.error(msg)
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
