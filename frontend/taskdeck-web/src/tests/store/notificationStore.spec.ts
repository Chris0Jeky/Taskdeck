import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { notificationsApi } from '../../api/notificationsApi'
import { useNotificationStore } from '../../store/notificationStore'

const toastMocks = vi.hoisted(() => ({
  success: vi.fn(),
  error: vi.fn(),
}))

vi.mock('../../api/notificationsApi', () => ({
  notificationsApi: {
    getNotifications: vi.fn(),
    markAsRead: vi.fn(),
    getPreferences: vi.fn(),
    updatePreferences: vi.fn(),
  },
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => toastMocks,
}))

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (_error: unknown, fallback: string) => ({ message: fallback }),
}))

describe('notificationStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('fetches notifications', async () => {
    const store = useNotificationStore()
    vi.mocked(notificationsApi.getNotifications).mockResolvedValue([
      {
        id: 'n1',
        userId: 'u1',
        boardId: null,
        type: 'Mention',
        cadence: 'Immediate',
        title: 'Mentioned',
        message: 'You were mentioned',
        sourceEntityType: 'chat-message',
        sourceEntityId: 'm1',
        isRead: false,
        readAt: null,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
    ])

    await store.fetchNotifications({ unreadOnly: true })

    expect(store.notifications).toHaveLength(1)
    expect(notificationsApi.getNotifications).toHaveBeenCalledWith({ unreadOnly: true })
  })

  it('marks a notification as read in local state', async () => {
    const store = useNotificationStore()
    store.notifications = [
      {
        id: 'n1',
        userId: 'u1',
        boardId: null,
        type: 'Mention',
        cadence: 'Immediate',
        title: 'Mentioned',
        message: 'You were mentioned',
        sourceEntityType: 'chat-message',
        sourceEntityId: 'm1',
        isRead: false,
        readAt: null,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
    ]

    vi.mocked(notificationsApi.markAsRead).mockResolvedValue({
      ...store.notifications[0],
      isRead: true,
      readAt: new Date().toISOString(),
    })

    await store.markAsRead('n1')

    expect(store.notifications[0].isRead).toBe(true)
  })

  it('loads and updates preferences', async () => {
    const store = useNotificationStore()
    const preferences = {
      userId: 'u1',
      inAppChannelEnabled: true,
      mentionImmediateEnabled: true,
      mentionDigestEnabled: false,
      assignmentImmediateEnabled: true,
      assignmentDigestEnabled: false,
      proposalOutcomeImmediateEnabled: true,
      proposalOutcomeDigestEnabled: false,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    }

    vi.mocked(notificationsApi.getPreferences).mockResolvedValue(preferences)
    await store.fetchPreferences()
    expect(store.preferences).toEqual(preferences)

    vi.mocked(notificationsApi.updatePreferences).mockResolvedValue({
      ...preferences,
      mentionImmediateEnabled: false,
      mentionDigestEnabled: true,
    })

    await store.updatePreferences({
      inAppChannelEnabled: true,
      mentionImmediateEnabled: false,
      mentionDigestEnabled: true,
      assignmentImmediateEnabled: true,
      assignmentDigestEnabled: false,
      proposalOutcomeImmediateEnabled: true,
      proposalOutcomeDigestEnabled: false,
    })

    expect(store.preferences?.mentionImmediateEnabled).toBe(false)
    expect(store.preferences?.mentionDigestEnabled).toBe(true)
    expect(toastMocks.success).toHaveBeenCalled()
  })

  it('sets error and toasts when fetching notifications fails', async () => {
    const store = useNotificationStore()
    vi.mocked(notificationsApi.getNotifications).mockRejectedValue(new Error('network'))

    await expect(store.fetchNotifications()).rejects.toBeInstanceOf(Error)

    expect(store.error).toBe('Failed to load notifications')
    expect(toastMocks.error).toHaveBeenCalledWith('Failed to load notifications')
  })

  it('sets error and toasts when updating preferences fails', async () => {
    const store = useNotificationStore()
    vi.mocked(notificationsApi.updatePreferences).mockRejectedValue(new Error('save failed'))

    await expect(store.updatePreferences({
      inAppChannelEnabled: true,
      mentionImmediateEnabled: true,
      mentionDigestEnabled: false,
      assignmentImmediateEnabled: true,
      assignmentDigestEnabled: false,
      proposalOutcomeImmediateEnabled: true,
      proposalOutcomeDigestEnabled: false,
    })).rejects.toBeInstanceOf(Error)

    expect(store.error).toBe('Failed to save notification preferences')
    expect(toastMocks.error).toHaveBeenCalledWith('Failed to save notification preferences')
  })
})
