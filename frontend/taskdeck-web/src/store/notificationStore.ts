import { defineStore } from 'pinia'
import { ref } from 'vue'
import { notificationsApi } from '../api/notificationsApi'
import { useToastStore } from './toastStore'
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

  async function fetchNotifications(query?: NotificationQuery) {
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
