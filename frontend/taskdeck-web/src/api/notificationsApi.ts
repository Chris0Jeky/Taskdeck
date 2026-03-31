import http from './http'
import { buildQueryString } from '../utils/queryBuilder'
import type {
  MarkAllReadResponse,
  NotificationItem,
  NotificationPreference,
  NotificationQuery,
  UpdateNotificationPreferenceRequest,
} from '../types/notifications'

export const notificationsApi = {
  async getNotifications(query?: NotificationQuery): Promise<NotificationItem[]> {
    const { data } = await http.get<NotificationItem[]>(`/notifications${buildQueryString(query)}`)
    return data
  },

  async markAsRead(notificationId: string): Promise<NotificationItem> {
    const { data } = await http.post<NotificationItem>(`/notifications/${encodeURIComponent(notificationId)}/read`)
    return data
  },

  async markAllRead(boardId?: string): Promise<MarkAllReadResponse> {
    const qs = boardId ? `?boardId=${encodeURIComponent(boardId)}` : ''
    const { data } = await http.post<MarkAllReadResponse>(`/notifications/mark-all-read${qs}`)
    return data
  },

  async getPreferences(): Promise<NotificationPreference> {
    const { data } = await http.get<NotificationPreference>('/notifications/preferences')
    return data
  },

  async updatePreferences(dto: UpdateNotificationPreferenceRequest): Promise<NotificationPreference> {
    const { data } = await http.put<NotificationPreference>('/notifications/preferences', dto)
    return data
  },
}
