import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { boardAccessApi } from '../api/boardAccessApi'
import { useToastStore } from './toastStore'
import { useSessionStore } from './sessionStore'
import type { BoardAccess, BoardRole, GrantAccessDto, UpdateAccessDto } from '../types/access'

export const usePermissionsStore = defineStore('permissions', () => {
  const toast = useToastStore()
  const session = useSessionStore()

  const boardAccess = ref<Map<string, BoardAccess[]>>(new Map())
  const loading = ref(false)
  const error = ref<string | null>(null)

  const currentUserRole = computed(() => {
    return (boardId: string): BoardRole | null => {
      const accessList = boardAccess.value.get(boardId)
      if (!accessList || !session.userId) return null
      const entry = accessList.find(a => a.userId === session.userId)
      return entry?.role ?? null
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
    try {
      loading.value = true
      error.value = null
      const access = await boardAccessApi.getAccess(boardId)
      boardAccess.value.set(boardId, access)
    } catch (e: unknown) {
      const msg = getErrorMessage(e, 'Failed to fetch board access')
      error.value = msg
      toast.error(msg)
      throw e
    } finally {
      loading.value = false
    }
  }

  async function grantAccess(boardId: string, dto: GrantAccessDto) {
    try {
      loading.value = true
      error.value = null
      const grantedBy = session.userId ?? ''
      const access = await boardAccessApi.grantAccess(boardId, dto, grantedBy)
      const existing = boardAccess.value.get(boardId) ?? []
      boardAccess.value.set(boardId, [...existing, access])
      toast.success('Access granted')
      return access
    } catch (e: unknown) {
      const msg = getErrorMessage(e, 'Failed to grant access')
      error.value = msg
      toast.error(msg)
      throw e
    } finally {
      loading.value = false
    }
  }

  async function updateAccess(boardId: string, accessId: string, dto: UpdateAccessDto) {
    try {
      loading.value = true
      error.value = null
      const updatedBy = session.userId ?? ''
      const updated = await boardAccessApi.updateAccess(boardId, accessId, dto, updatedBy)
      const existing = boardAccess.value.get(boardId) ?? []
      const index = existing.findIndex(a => a.id === accessId)
      if (index !== -1) {
        existing[index] = updated
        boardAccess.value.set(boardId, [...existing])
      }
      toast.success('Access updated')
      return updated
    } catch (e: unknown) {
      const msg = getErrorMessage(e, 'Failed to update access')
      error.value = msg
      toast.error(msg)
      throw e
    } finally {
      loading.value = false
    }
  }

  async function revokeAccess(boardId: string, accessId: string) {
    try {
      loading.value = true
      error.value = null
      const revokedBy = session.userId ?? ''
      await boardAccessApi.revokeAccess(boardId, accessId, revokedBy)
      const existing = boardAccess.value.get(boardId) ?? []
      boardAccess.value.set(boardId, existing.filter(a => a.id !== accessId))
      toast.success('Access revoked')
    } catch (e: unknown) {
      const msg = getErrorMessage(e, 'Failed to revoke access')
      error.value = msg
      toast.error(msg)
      throw e
    } finally {
      loading.value = false
    }
  }

  function getErrorMessage(err: unknown, fallback: string): string {
    if (typeof err !== 'object' || err === null) return fallback
    const typed = err as { response?: { data?: { message?: unknown } }; message?: unknown }
    const responseMessage = typed.response?.data?.message
    if (typeof responseMessage === 'string' && responseMessage.trim().length > 0) return responseMessage
    if (typeof typed.message === 'string' && typed.message.trim().length > 0) return typed.message
    return fallback
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
