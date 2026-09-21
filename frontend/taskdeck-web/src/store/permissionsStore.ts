import { defineStore } from 'pinia'
import { ref, computed, watch } from 'vue'
import { boardAccessApi } from '../api/boardAccessApi'
import { useToastStore } from './toastStore'
import { useSessionStore } from './sessionStore'
import { isDemoMode, DemoModeError } from '../utils/demoMode'
import type { BoardAccess, BoardRole, GrantAccessDto, UpdateAccessDto } from '../types/access'
import { normalizeBoardRole } from '../utils/roles'
import { getErrorDisplay } from '../composables/useErrorMapper'

export const usePermissionsStore = defineStore('permissions', () => {
  const toast = useToastStore()
  const session = useSessionStore()

  const boardAccess = ref<Map<string, BoardAccess[]>>(new Map())
  const loading = ref(false)
  const error = ref<string | null>(null)

  type ReadRetry = () => Promise<void>

  interface OperationOwner {
    epoch: number
    token: symbol
    userId: string | null
  }

  interface ReadOwner extends OperationOwner {
    observedMutationGeneration: number
    revalidateOnTokenRotation: boolean
  }

  let sessionEpoch = 0
  const activeOperations = new Set<symbol>()
  const activeReadByBoard = new Map<string, ReadOwner>()
  const readRetryByBoard = new Map<string, ReadRetry>()
  const mutationGenerationByBoard = new Map<string, number>()

  function syncLoading() {
    loading.value = activeOperations.size > 0
  }

  function beginOperation(label: string): OperationOwner {
    const owner = { epoch: sessionEpoch, token: Symbol(label), userId: session.userId }
    activeOperations.add(owner.token)
    error.value = null
    syncLoading()
    return owner
  }

  function ownsSession(owner: OperationOwner): boolean {
    return owner.epoch === sessionEpoch
  }

  function finishOperation(owner: OperationOwner) {
    if (!ownsSession(owner)) return
    activeOperations.delete(owner.token)
    syncLoading()
  }

  function mutationGeneration(boardId: string): number {
    return mutationGenerationByBoard.get(boardId) ?? 0
  }

  function beginRead(
    boardId: string,
    retry: ReadRetry,
    revalidateOnTokenRotation = false,
  ): ReadOwner {
    const previous = activeReadByBoard.get(boardId)
    if (previous?.epoch === sessionEpoch) activeOperations.delete(previous.token)

    const operation = beginOperation(`read:${boardId}`)
    const owner = {
      ...operation,
      observedMutationGeneration: mutationGeneration(boardId),
      revalidateOnTokenRotation,
    }
    activeReadByBoard.set(boardId, owner)
    readRetryByBoard.set(boardId, retry)
    return owner
  }

  function ownsRead(boardId: string, owner: ReadOwner): boolean {
    const current = activeReadByBoard.get(boardId)
    return ownsSession(owner)
      && current?.token === owner.token
      && owner.observedMutationGeneration === mutationGeneration(boardId)
  }

  function finishRead(boardId: string, owner: ReadOwner) {
    if (activeReadByBoard.get(boardId)?.token === owner.token) {
      activeReadByBoard.delete(boardId)
      readRetryByBoard.delete(boardId)
    }
    finishOperation(owner)
  }

  function recordMutation(boardId: string) {
    mutationGenerationByBoard.set(boardId, mutationGeneration(boardId) + 1)

    const staleRead = activeReadByBoard.get(boardId)
    if (staleRead?.epoch === sessionEpoch) {
      activeReadByBoard.delete(boardId)
      readRetryByBoard.delete(boardId)
      activeOperations.delete(staleRead.token)
      syncLoading()
    }
  }

  function invalidateOperations() {
    sessionEpoch += 1
    activeOperations.clear()
    activeReadByBoard.clear()
    readRetryByBoard.clear()
    mutationGenerationByBoard.clear()
    loading.value = false
    error.value = null
  }

  function retryMissingActiveReads() {
    const retries = Array.from(activeReadByBoard.keys())
      .filter(boardId => {
        const owner = activeReadByBoard.get(boardId)
        return owner?.revalidateOnTokenRotation === true || !boardAccess.value.has(boardId)
      })
      .map(boardId => readRetryByBoard.get(boardId))
      .filter((retry): retry is ReadRetry => retry !== undefined)

    invalidateOperations()
    for (const retry of retries) {
      void retry().catch(() => {
        // The retried store action owns current error/toast state.
      })
    }
  }

  async function reconcileStaleMutation(boardId: string, owner: OperationOwner) {
    // A same-user token rotation retires the mutation owner, but the server may
    // already have committed it. Re-read under the replacement credential so a
    // successful mutation cannot disappear from the access cache. Identity
    // changes and logout must not read a board on behalf of the old session.
    if (ownsSession(owner)
      || owner.userId === null
      || owner.userId !== session.userId
      || !session.isAuthenticated
      || session.isDemo) {
      return
    }

    try {
      await fetchBoardAccess(boardId, true)
    } catch {
      // The read owns its error/toast state. The mutation itself already
      // settled successfully, so do not turn a reconciliation failure into a
      // second, misleading mutation failure.
    }
  }

  function resetForSession() {
    invalidateOperations()
    boardAccess.value = new Map()
  }

  watch(
    () => [session.userId, session.isAuthenticated, session.isDemo],
    resetForSession,
    { flush: 'sync' },
  )

  watch(
    () => session.token,
    retryMissingActiveReads,
    { flush: 'sync' },
  )

  function guardDemoMutation(): never | void {
    if (isDemoMode) {
      toast.info('This action is view-only in demo mode.')
      throw new DemoModeError()
    }
  }

  const currentUserRole = computed(() => {
    return (boardId: string): BoardRole | null => {
      const accessList = boardAccess.value.get(boardId)
      if (!accessList || !session.userId) return null
      const entry = accessList.find(a => a.userId === session.userId)
      if (!entry) return null
      return normalizeBoardRole(entry.role)
    }
  })

  const canEdit = computed(() => {
    return (boardId: string): boolean => {
      const role = currentUserRole.value(boardId)
      return role === 'Owner' || role === 'Admin' || role === 'Editor'
    }
  })

  const canAdmin = computed(() => {
    return (boardId: string): boolean => {
      const role = currentUserRole.value(boardId)
      return role === 'Owner' || role === 'Admin'
    }
  })

  const isOwner = computed(() => {
    return (boardId: string): boolean => {
      const role = currentUserRole.value(boardId)
      return role === 'Owner'
    }
  })

  async function fetchBoardAccess(boardId: string, revalidateOnTokenRotation = false) {
    if (isDemoMode) {
      loading.value = true
      error.value = null
      boardAccess.value.set(boardId, [])
      loading.value = false
      return
    }

    const owner = beginRead(
      boardId,
      () => fetchBoardAccess(boardId, revalidateOnTokenRotation),
      revalidateOnTokenRotation,
    )
    try {
      const access = await boardAccessApi.getAccess(boardId)
      if (!ownsRead(boardId, owner)) return
      boardAccess.value.set(boardId, access)
    } catch (e: unknown) {
      if (ownsRead(boardId, owner)) {
        const msg = getErrorDisplay(e, 'Failed to fetch board access').message
        error.value = msg
        toast.error(msg)
      }
      throw e
    } finally {
      finishRead(boardId, owner)
    }
  }

  async function grantAccess(boardId: string, dto: GrantAccessDto) {
    guardDemoMutation()
    const owner = beginOperation(`grant:${boardId}`)
    try {
      session.requireUserId('board access management')
      const access = await boardAccessApi.grantAccess(boardId, dto)
      if (!ownsSession(owner)) {
        await reconcileStaleMutation(boardId, owner)
        return access
      }

      recordMutation(boardId)
      const existing = boardAccess.value.get(boardId) ?? []
      if (!existing.some(entry => entry.id === access.id)) {
        boardAccess.value.set(boardId, [...existing, access])
      }
      toast.success('Access granted')
      return access
    } catch (e: unknown) {
      if (ownsSession(owner)) {
        const msg = getErrorDisplay(e, 'Failed to grant access').message
        error.value = msg
        toast.error(msg)
      }
      throw e
    } finally {
      finishOperation(owner)
    }
  }

  async function updateAccess(boardId: string, accessId: string, dto: UpdateAccessDto) {
    guardDemoMutation()
    const owner = beginOperation(`update:${boardId}:${accessId}`)
    try {
      session.requireUserId('board access management')
      const updated = await boardAccessApi.updateAccess(boardId, accessId, dto)
      if (!ownsSession(owner)) {
        await reconcileStaleMutation(boardId, owner)
        return updated
      }

      recordMutation(boardId)
      const existing = boardAccess.value.get(boardId) ?? []
      if (existing.some(access => access.id === accessId)) {
        boardAccess.value.set(
          boardId,
          existing.map(access => access.id === accessId ? updated : access),
        )
      }
      toast.success('Access updated')
      return updated
    } catch (e: unknown) {
      if (ownsSession(owner)) {
        const msg = getErrorDisplay(e, 'Failed to update access').message
        error.value = msg
        toast.error(msg)
      }
      throw e
    } finally {
      finishOperation(owner)
    }
  }

  async function revokeAccess(boardId: string, accessId: string) {
    guardDemoMutation()
    const owner = beginOperation(`revoke:${boardId}:${accessId}`)
    try {
      session.requireUserId('board access management')
      await boardAccessApi.revokeAccess(boardId, accessId)
      if (!ownsSession(owner)) {
        await reconcileStaleMutation(boardId, owner)
        return
      }

      recordMutation(boardId)
      const existing = boardAccess.value.get(boardId) ?? []
      boardAccess.value.set(boardId, existing.filter(access => access.id !== accessId))
      toast.success('Access revoked')
    } catch (e: unknown) {
      if (ownsSession(owner)) {
        const msg = getErrorDisplay(e, 'Failed to revoke access').message
        error.value = msg
        toast.error(msg)
      }
      throw e
    } finally {
      finishOperation(owner)
    }
  }

  return {
    boardAccess,
    loading,
    error,
    currentUserRole,
    canEdit,
    canAdmin,
    isOwner,
    fetchBoardAccess,
    grantAccess,
    updateAccess,
    revokeAccess,
  }
})
