import { defineStore } from 'pinia'
import { ref, watch } from 'vue'
import { notificationsApi } from '../api/notificationsApi'
import { useToastStore } from './toastStore'
import { useSessionStore } from './sessionStore'
import { isDemoMode, DemoModeError } from '../utils/demoMode'
import { getErrorDisplay } from '../composables/useErrorMapper'
import type {
  NotificationItem,
  NotificationPreference,
  NotificationQuery,
  UpdateNotificationPreferenceRequest,
} from '../types/notifications'

export const useNotificationStore = defineStore('notifications', () => {
  const toast = useToastStore()
  const session = useSessionStore()

  const notifications = ref<NotificationItem[]>([])
  const preferences = ref<NotificationPreference | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  type ReadLane = 'notifications' | 'preferences'

  interface OperationOwner {
    epoch: number
    token: symbol
    ownsLoading: boolean
  }

  interface ReadOwner extends OperationOwner {
    observedMutationGeneration: number
  }

  interface PreferenceMutationTail {
    promise: Promise<void>
    ownerToken: symbol
  }

  let sessionEpoch = 0
  let notificationMutationGeneration = 0
  let preferenceMutationGeneration = 0
  let errorOwner: symbol | null = null
  let preferenceMutationTail: PreferenceMutationTail | null = null
  const activeLoadingOperations = new Set<symbol>()
  const readOwners = new Map<ReadLane, ReadOwner>()

  function syncLoading(): void {
    loading.value = activeLoadingOperations.size > 0
  }

  function clearError(): void {
    error.value = null
    errorOwner = null
  }

  function recordError(owner: OperationOwner, message: string): void {
    error.value = message
    errorOwner = owner.token
  }

  function beginOperation(
    label: string,
    options: { ownsLoading?: boolean; clearExistingError?: boolean } = {},
  ): OperationOwner {
    const owner = {
      epoch: sessionEpoch,
      token: Symbol(label),
      ownsLoading: options.ownsLoading ?? false,
    }
    if (owner.ownsLoading) activeLoadingOperations.add(owner.token)
    if (options.clearExistingError ?? false) clearError()
    syncLoading()
    return owner
  }

  function ownsSession(owner: OperationOwner): boolean {
    return owner.epoch === sessionEpoch
  }

  function finishOperation(owner: OperationOwner): void {
    if (!ownsSession(owner)) return
    if (owner.ownsLoading) activeLoadingOperations.delete(owner.token)
    syncLoading()
  }

  function mutationGeneration(lane: ReadLane): number {
    return lane === 'notifications'
      ? notificationMutationGeneration
      : preferenceMutationGeneration
  }

  function beginRead(lane: ReadLane): ReadOwner {
    const previous = readOwners.get(lane)
    if (previous?.epoch === sessionEpoch && previous.ownsLoading) {
      activeLoadingOperations.delete(previous.token)
    }

    const operation = beginOperation(`read:${lane}`, {
      ownsLoading: true,
      clearExistingError: true,
    })
    const owner = {
      ...operation,
      observedMutationGeneration: mutationGeneration(lane),
    }
    readOwners.set(lane, owner)
    return owner
  }

  function ownsRead(lane: ReadLane, owner: ReadOwner): boolean {
    const current = readOwners.get(lane)
    return ownsSession(owner)
      && current?.token === owner.token
      && owner.observedMutationGeneration === mutationGeneration(lane)
  }

  function finishRead(lane: ReadLane, owner: ReadOwner): void {
    if (readOwners.get(lane)?.token === owner.token) readOwners.delete(lane)
    finishOperation(owner)
  }

  function invalidateRead(lane: ReadLane): void {
    const owner = readOwners.get(lane)
    if (owner?.epoch === sessionEpoch && owner.ownsLoading) {
      activeLoadingOperations.delete(owner.token)
    }
    readOwners.delete(lane)
    syncLoading()
  }

  function recordNotificationMutation(): void {
    notificationMutationGeneration += 1
    invalidateRead('notifications')
  }

  function recordPreferenceMutation(): void {
    preferenceMutationGeneration += 1
    invalidateRead('preferences')
  }

  function resetForSession(): void {
    sessionEpoch += 1
    notificationMutationGeneration = 0
    preferenceMutationGeneration = 0
    activeLoadingOperations.clear()
    readOwners.clear()
    preferenceMutationTail = null
    notifications.value = []
    preferences.value = null
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

  async function fetchNotifications(query?: NotificationQuery) {
    if (isDemoMode) {
      invalidateRead('notifications')
      clearError()
      notifications.value = []
      return
    }

    const owner = beginRead('notifications')
    try {
      const result = await notificationsApi.getNotifications(query)
      if (!ownsRead('notifications', owner)) return
      notifications.value = result
    } catch (e: unknown) {
      if (ownsRead('notifications', owner)) {
        const msg = getErrorDisplay(e, 'Failed to load notifications').message
        recordError(owner, msg)
        toast.error(msg)
      }
      throw e
    } finally {
      finishRead('notifications', owner)
    }
  }

  async function markAsRead(notificationId: string) {
    guardDemoMutation()
    const owner = beginOperation(`mark-read:${notificationId}`)
    try {
      const updated = await notificationsApi.markAsRead(notificationId)
      if (!ownsSession(owner)) return updated

      recordNotificationMutation()
      notifications.value = notifications.value.map((item) => (
        item.id === notificationId ? updated : item
      ))
      return updated
    } catch (e: unknown) {
      if (ownsSession(owner)) {
        const msg = getErrorDisplay(e, 'Failed to mark notification as read').message
        recordError(owner, msg)
        toast.error(msg)
      }
      throw e
    }
  }

  async function markAllRead(boardId?: string) {
    guardDemoMutation()
    const owner = beginOperation(`mark-all-read:${boardId ?? 'all'}`)
    try {
      const result = await notificationsApi.markAllRead(boardId)
      if (!ownsSession(owner)) return result

      recordNotificationMutation()
      notifications.value = notifications.value.map((item) => {
        if (boardId && item.boardId !== boardId) return item
        return {
          ...item,
          isRead: true,
          readAt: item.readAt ?? new Date().toISOString(),
        }
      })
      return result
    } catch (e: unknown) {
      if (ownsSession(owner)) {
        const msg = getErrorDisplay(e, 'Failed to mark all notifications as read').message
        recordError(owner, msg)
        toast.error(msg)
      }
      throw e
    }
  }

  async function fetchPreferences() {
    if (isDemoMode) {
      invalidateRead('preferences')
      clearError()
      preferences.value = null
      return preferences.value
    }

    const owner = beginRead('preferences')
    try {
      const result = await notificationsApi.getPreferences()
      if (ownsRead('preferences', owner)) preferences.value = result
      return result
    } catch (e: unknown) {
      if (ownsRead('preferences', owner)) {
        const msg = getErrorDisplay(e, 'Failed to load notification preferences').message
        recordError(owner, msg)
        toast.error(msg)
      }
      throw e
    } finally {
      finishRead('preferences', owner)
    }
  }

  async function updatePreferences(dto: UpdateNotificationPreferenceRequest) {
    guardDemoMutation()
    const predecessor = preferenceMutationTail
    const owner = beginOperation('update-preferences', {
      ownsLoading: true,
      clearExistingError: predecessor === null,
    })
    let release!: () => void
    const tail = new Promise<void>((resolve) => { release = resolve })
    preferenceMutationTail = { promise: tail, ownerToken: owner.token }

    try {
      if (predecessor) await predecessor.promise
      if (!ownsSession(owner)) return undefined

      if (predecessor && errorOwner === predecessor.ownerToken) clearError()

      try {
        const updated = await notificationsApi.updatePreferences(dto)
        if (!ownsSession(owner)) return updated

        recordPreferenceMutation()
        preferences.value = updated
        toast.success('Notification preferences saved')
        return updated
      } catch (e: unknown) {
        if (ownsSession(owner)) {
          const msg = getErrorDisplay(e, 'Failed to save notification preferences').message
          recordError(owner, msg)
          toast.error(msg)
        }
        throw e
      }
    } finally {
      finishOperation(owner)
      release()
      if (preferenceMutationTail?.promise === tail) preferenceMutationTail = null
    }
  }

  return {
    notifications,
    preferences,
    loading,
    error,
    fetchNotifications,
    markAsRead,
    markAllRead,
    fetchPreferences,
    updatePreferences,
  }
})
