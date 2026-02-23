import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import NotificationInboxView from '../../views/NotificationInboxView.vue'

const mockNotificationStore = reactive({
  notifications: [] as Array<{
    id: string
    title: string
    message: string
    type: number | string
    cadence: number | string
    isRead: boolean
    createdAt: string
  }>,
  loading: false,
  error: null as string | null,
  fetchNotifications: vi.fn<(query?: { unreadOnly?: boolean; limit?: number }) => Promise<void>>(),
  markAsRead: vi.fn<(notificationId: string) => Promise<void>>(),
})

vi.mock('../../store/notificationStore', () => ({
  useNotificationStore: () => mockNotificationStore,
}))

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (_error: unknown, fallback: string) => ({ message: fallback }),
}))

async function waitForUi() {
  await Promise.resolve()
  await Promise.resolve()
}

describe('NotificationInboxView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockNotificationStore.notifications = []
    mockNotificationStore.loading = false
    mockNotificationStore.error = null
    mockNotificationStore.fetchNotifications.mockResolvedValue(undefined)
    mockNotificationStore.markAsRead.mockResolvedValue(undefined)
  })

  it('loads notifications on mount and renders items', async () => {
    mockNotificationStore.fetchNotifications.mockImplementation(async () => {
      mockNotificationStore.notifications = [
        {
          id: 'n1',
          title: 'Mentioned',
          message: 'You were mentioned.',
          type: 0,
          cadence: 0,
          isRead: false,
          createdAt: new Date().toISOString(),
        },
      ]
    })

    const wrapper = mount(NotificationInboxView)
    await waitForUi()

    expect(mockNotificationStore.fetchNotifications).toHaveBeenCalledWith({ unreadOnly: false, limit: 200 })
    expect(wrapper.text()).toContain('Mentioned')
    expect(wrapper.text()).toContain('Mark read')
  })

  it('marks notification as read when action is clicked', async () => {
    mockNotificationStore.notifications = [
      {
        id: 'n1',
        title: 'Mentioned',
        message: 'You were mentioned.',
        type: 0,
        cadence: 0,
        isRead: false,
        createdAt: new Date().toISOString(),
      },
    ]

    const wrapper = mount(NotificationInboxView)
    await waitForUi()

    const action = wrapper.get('button.td-btn--primary')
    await action.trigger('click')

    expect(mockNotificationStore.markAsRead).toHaveBeenCalledWith('n1')
  })
})
