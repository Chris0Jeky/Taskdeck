import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
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
      error.value = null
      boardAccess.value.set(boardId, [])
      loading.value = false
      return
    }
    try {
      loading.value = true
      error.value = null
      const access = await boardAccessApi.getAccess(boardId)
      boardAccess.value.set(boardId, access)
    } catch (e: unknown) {
      const msg = getErrorDisplay(e, 'Failed to fetch board access').message
      error.value = msg
      toast.error(msg)
      throw e
    } finally {
      loading.value = false
    }
  }

  async function grantAccess(boardId: string, dto: GrantAccessDto) {
    guardDemoMutation()
    try {
      loading.value = true
      error.value = null
      session.requireUserId('board access management')
      const access = await boardAccessApi.grantAccess(boardId, dto)
      const existing = boardAccess.value.get(boardId) ?? []
      boardAccess.value.set(boardId, [...existing, access])
      toast.success('Access granted')
      return access
    } catch (e: unknown) {
      const msg = getErrorDisplay(e, 'Failed to grant access').message
      error.value = msg
      toast.error(msg)
      throw e
    } finally {
      loading.value = false
    }
  }

  async function updateAccess(boardId: string, accessId: string, dto: UpdateAccessDto) {
    guardDemoMutation()
    try {
      loading.value = true
      error.value = null
      session.requireUserId('board access management')
      const updated = await boardAccessApi.updateAccess(boardId, accessId, dto)
      const existing = boardAccess.value.get(boardId) ?? []
      const index = existing.findIndex(a => a.id === accessId)
      if (index !== -1) {
        existing[index] = updated
        boardAccess.value.set(boardId, [...existing])
      }
      toast.success('Access updated')
      return updated
    } catch (e: unknown) {
      const msg = getErrorDisplay(e, 'Failed to update access').message
      error.value = msg
      toast.error(msg)
      throw e
    } finally {
      loading.value = false
    }
  }

  async function revokeAccess(boardId: string, accessId: string) {
    guardDemoMutation()
    try {
      loading.value = true
      error.value = null
      session.requireUserId('board access management')
      await boardAccessApi.revokeAccess(boardId, accessId)
      const existing = boardAccess.value.get(boardId) ?? []
      boardAccess.value.set(boardId, existing.filter(a => a.id !== accessId))
      toast.success('Access revoked')
    } catch (e: unknown) {
      const msg = getErrorDisplay(e, 'Failed to revoke access').message
      error.value = msg
      toast.error(msg)
      throw e
    } finally {
      loading.value = false
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
