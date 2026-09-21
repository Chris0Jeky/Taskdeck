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

  interface OperationOwner {
    epoch: number
    token: symbol
  }

  interface ReadOwner extends OperationOwner {
    observedMutationGeneration: number
  }

  interface MutationTail {
    promise: Promise<void>
    ownerToken: symbol
  }

  let sessionEpoch = 0
  let errorOwner: symbol | null = null
  const activeOperations = new Set<symbol>()
  const activeReadByBoard = new Map<string, ReadOwner>()
  const mutationGenerationByBoard = new Map<string, number>()
  const mutationTails = new Map<string, MutationTail>()

  function syncLoading() {
    loading.value = activeOperations.size > 0
  }

  function clearError() {
    error.value = null
    errorOwner = null
  }

  function recordError(owner: OperationOwner, message: string) {
    error.value = message
    errorOwner = owner.token
  }

  function beginOperation(label: string): OperationOwner {
    const owner = { epoch: sessionEpoch, token: Symbol(label) }
    activeOperations.add(owner.token)
    clearError()
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

  function beginRead(boardId: string): ReadOwner {
    const previous = activeReadByBoard.get(boardId)
    if (previous?.epoch === sessionEpoch) activeOperations.delete(previous.token)

    const operation = beginOperation(`read:${boardId}`)
    const owner = {
      ...operation,
      observedMutationGeneration: mutationGeneration(boardId),
    }
    activeReadByBoard.set(boardId, owner)
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
    }
    finishOperation(owner)
  }

  function recordMutation(boardId: string) {
    mutationGenerationByBoard.set(boardId, mutationGeneration(boardId) + 1)

    const staleRead = activeReadByBoard.get(boardId)
    if (staleRead?.epoch === sessionEpoch) {
      activeReadByBoard.delete(boardId)
      activeOperations.delete(staleRead.token)
      syncLoading()
    }
  }

  async function enqueueAccessMutation<T>(
    boardId: string,
    accessId: string,
    label: string,
    task: (owner: OperationOwner) => Promise<T>,
  ): Promise<T | undefined> {
    const key = `${boardId}:${accessId}`
    const predecessor = mutationTails.get(key)
    const owner = beginOperation(label)
    let release!: () => void
    const tail = new Promise<void>((resolve) => { release = resolve })
    mutationTails.set(key, { promise: tail, ownerToken: owner.token })

    try {
      if (predecessor) await predecessor.promise
      if (!ownsSession(owner)) return undefined

      // Retire only an error produced by this lane's predecessor. Another
      // access row can fail while this intent waits and must keep its receipt.
      if (predecessor && errorOwner === predecessor.ownerToken) clearError()
      return await task(owner)
    } finally {
      finishOperation(owner)
      release()
      if (mutationTails.get(key)?.promise === tail) mutationTails.delete(key)
    }
  }

  function resetForSession() {
    sessionEpoch += 1
    activeOperations.clear()
    activeReadByBoard.clear()
    mutationGenerationByBoard.clear()
    mutationTails.clear()
    boardAccess.value = new Map()
    loading.value = false
    clearError()
  }

  watch(
    () => [session.userId, session.token, session.isAuthenticated, session.isDemo],
    resetForSession,
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

  async function fetchBoardAccess(boardId: string) {
    if (isDemoMode) {
      loading.value = true
      clearError()
      boardAccess.value.set(boardId, [])
      loading.value = false
      return
    }

    const owner = beginRead(boardId)
    try {
      const access = await boardAccessApi.getAccess(boardId)
      if (!ownsRead(boardId, owner)) return
      boardAccess.value.set(boardId, access)
    } catch (e: unknown) {
      if (ownsRead(boardId, owner)) {
        const msg = getErrorDisplay(e, 'Failed to fetch board access').message
        recordError(owner, msg)
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
      if (!ownsSession(owner)) return access

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
        recordError(owner, msg)
        toast.error(msg)
      }
      throw e
    } finally {
      finishOperation(owner)
    }
  }

  async function updateAccess(boardId: string, accessId: string, dto: UpdateAccessDto) {
    guardDemoMutation()
    return await enqueueAccessMutation(
      boardId,
      accessId,
      `update:${boardId}:${accessId}`,
      async (owner) => {
        try {
          session.requireUserId('board access management')
          const updated = await boardAccessApi.updateAccess(boardId, accessId, dto)
          if (!ownsSession(owner)) return updated

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
            recordError(owner, msg)
            toast.error(msg)
          }
          throw e
        }
      },
    )
  }

  async function revokeAccess(boardId: string, accessId: string) {
    guardDemoMutation()
    await enqueueAccessMutation(
      boardId,
      accessId,
      `revoke:${boardId}:${accessId}`,
      async (owner) => {
        try {
          session.requireUserId('board access management')
          await boardAccessApi.revokeAccess(boardId, accessId)
          if (!ownsSession(owner)) return

          recordMutation(boardId)
          const existing = boardAccess.value.get(boardId) ?? []
          boardAccess.value.set(boardId, existing.filter(access => access.id !== accessId))
          toast.success('Access revoked')
        } catch (e: unknown) {
          if (ownsSession(owner)) {
            const msg = getErrorDisplay(e, 'Failed to revoke access').message
            recordError(owner, msg)
            toast.error(msg)
          }
          throw e
        }
      },
    )
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
