import { defineStore } from 'pinia'
import { ref } from 'vue'
import { notificationsApi } from '../api/notificationsApi'
import { useToastStore } from './toastStore'
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

  const notifications = ref<NotificationItem[]>([])
  const preferences = ref<NotificationPreference | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  function guardDemoMutation(): never | void {
    if (isDemoMode) {
      toast.info('This action is view-only in demo mode.')
      throw new DemoModeError()
    }
  }

  async function fetchNotifications(query?: NotificationQuery) {
    if (isDemoMode) {
      notifications.value = []
      return
    }
    try {
      loading.value = true
      error.value = null
      notifications.value = await notificationsApi.getNotifications(query)
    } catch (e: unknown) {
      const msg = getErrorDisplay(e, 'Failed to load notifications').message
      error.value = msg
      toast.error(msg)
      throw e
    } finally {
      loading.value = false
    }
  }

  async function markAsRead(notificationId: string) {
    guardDemoMutation()
    try {
      const updated = await notificationsApi.markAsRead(notificationId)
      notifications.value = notifications.value.map((item) => (
        item.id === notificationId ? updated : item
      ))
      return updated
    } catch (e: unknown) {
      const msg = getErrorDisplay(e, 'Failed to mark notification as read').message
      error.value = msg
      toast.error(msg)
      throw e
    }
  }

  async function fetchPreferences() {
    if (isDemoMode) {
      preferences.value = null
      return null
    }
    try {
      loading.value = true
      error.value = null
      preferences.value = await notificationsApi.getPreferences()
      return preferences.value
    } catch (e: unknown) {
      const msg = getErrorDisplay(e, 'Failed to load notification preferences').message
      error.value = msg
      toast.error(msg)
      throw e
    } finally {
      loading.value = false
    }
  }

  async function updatePreferences(dto: UpdateNotificationPreferenceRequest) {
    guardDemoMutation()
    try {
      loading.value = true
      error.value = null
      preferences.value = await notificationsApi.updatePreferences(dto)
      toast.success('Notification preferences saved')
      return preferences.value
    } catch (e: unknown) {
      const msg = getErrorDisplay(e, 'Failed to save notification preferences').message
      error.value = msg
      toast.error(msg)
      throw e
    } finally {
      loading.value = false
    }
  }

  return {
    notifications,
    preferences,
    loading,
    error,
    fetchNotifications,
    markAsRead,
    fetchPreferences,
    updatePreferences,
  }
})
