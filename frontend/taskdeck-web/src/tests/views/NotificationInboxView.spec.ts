import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import NotificationInboxView from '../../views/NotificationInboxView.vue'

const routerMocks = vi.hoisted(() => ({
  push: vi.fn(),
}))

const routeMock = vi.hoisted(() => ({
  query: {} as Record<string, unknown>,
}))

const mockNotificationStore = reactive({
  notifications: [] as Array<{
    id: string
    title: string
    message: string
    boardId: string | null
    type: number | string
    cadence: number | string
    sourceEntityType: string | null
    sourceEntityId: string | null
    isRead: boolean
    readAt: string | null
    createdAt: string
    updatedAt: string
  }>,
  loading: false as boolean,
  error: null as string | null,
  fetchNotifications: vi.fn<(query?: { unreadOnly?: boolean; boardId?: string; limit?: number }) => Promise<void>>(),
  markAsRead: vi.fn<(notificationId: string) => Promise<void>>(),
  markAllRead: vi.fn<(boardId?: string) => Promise<{ markedCount: number }>>(),
})

vi.mock('../../store/notificationStore', () => ({
  useNotificationStore: () => mockNotificationStore,
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({
    push: routerMocks.push,
  }),
  useRoute: () => routeMock,
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
    mockNotificationStore.markAllRead.mockResolvedValue({ markedCount: 0 })
    routerMocks.push.mockReset()
    routeMock.query = {}
  })

  it('loads notifications on mount and renders items', async () => {
    mockNotificationStore.fetchNotifications.mockImplementation(async () => {
      mockNotificationStore.notifications = [
        {
          id: 'n1',
          title: 'Mentioned',
          message: 'You were mentioned.',
          boardId: 'board-1',
          type: 0,
          cadence: 0,
          sourceEntityType: 'proposal',
          sourceEntityId: 'proposal-1',
          isRead: false,
          readAt: null,
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
        },
      ]
    })

    const wrapper = mount(NotificationInboxView)
    await waitForUi()

    expect(mockNotificationStore.fetchNotifications).toHaveBeenCalledWith({ unreadOnly: false, limit: 200 })
    expect(wrapper.text()).toContain('Mentioned')
    expect(wrapper.text()).toContain('Mark read')
    expect(wrapper.text()).toContain('Open Proposal')
    expect(wrapper.text()).toContain('Board-linked')
  })

  it('marks notification as read when action is clicked', async () => {
    mockNotificationStore.notifications = [
      {
        id: 'n1',
        title: 'Mentioned',
        message: 'You were mentioned.',
        boardId: null,
        type: 0,
        cadence: 0,
        sourceEntityType: null,
        sourceEntityId: null,
        isRead: false,
        readAt: null,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
    ]

    const wrapper = mount(NotificationInboxView)
    await waitForUi()

    const action = wrapper.get('button.td-btn--primary')
    await action.trigger('click')

    expect(mockNotificationStore.markAsRead).toHaveBeenCalledWith('n1')
  })

  it('filters notifications by boardId from the route query', async () => {
    routeMock.query = { boardId: 'board-7' }

    const wrapper = mount(NotificationInboxView)
    await waitForUi()

    expect(mockNotificationStore.fetchNotifications).toHaveBeenCalledWith({
      unreadOnly: false,
      boardId: 'board-7',
      limit: 200,
    })
    expect(wrapper.text()).toContain('Showing notifications linked to board board-7.')
  })

  it('opens proposal notifications in review with preserved board context', async () => {
    mockNotificationStore.notifications = [
      {
        id: 'n1',
        title: 'Proposal ready',
        message: 'Review the scoped proposal.',
        boardId: 'board-7',
        type: 2,
        cadence: 0,
        sourceEntityType: 'Proposal',
        sourceEntityId: 'proposal-42',
        isRead: true,
        readAt: new Date().toISOString(),
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
    ]

    const wrapper = mount(NotificationInboxView)
    await waitForUi()

    const action = wrapper.findAll('button').find((node) => node.text() === 'Open Proposal')
    await action?.trigger('click')

    expect(routerMocks.push).toHaveBeenCalledWith({
      name: 'workspace-review',
      query: { boardId: 'board-7' },
      hash: '#proposal-proposal-42',
    })
  })

  it('renders type badge with accessible label', async () => {
    mockNotificationStore.notifications = [
      {
        id: 'n1',
        title: 'Test',
        message: 'Test message',
        boardId: null,
        type: 'Mention',
        cadence: 0,
        sourceEntityType: null,
        sourceEntityId: null,
        isRead: true,
        readAt: new Date().toISOString(),
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
    ]

    const wrapper = mount(NotificationInboxView)
    await waitForUi()

    expect(wrapper.text()).toContain('Mention')
  })

  it('shows Mark all read button when there are unread notifications', async () => {
    mockNotificationStore.notifications = [
      {
        id: 'n1',
        title: 'Unread',
        message: 'Message',
        boardId: null,
        type: 'Mention',
        cadence: 0,
        sourceEntityType: null,
        sourceEntityId: null,
        isRead: false,
        readAt: null,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
    ]

    const wrapper = mount(NotificationInboxView)
    await waitForUi()

    const markAllBtn = wrapper.findAll('button').find((b) => b.text() === 'Mark all read')
    expect(markAllBtn).toBeDefined()
  })

  it('hides Mark all read button when all notifications are read', async () => {
    mockNotificationStore.notifications = [
      {
        id: 'n1',
        title: 'Read',
        message: 'Message',
        boardId: null,
        type: 'Mention',
        cadence: 0,
        sourceEntityType: null,
        sourceEntityId: null,
        isRead: true,
        readAt: new Date().toISOString(),
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
    ]

    const wrapper = mount(NotificationInboxView)
    await waitForUi()

    const markAllBtn = wrapper.findAll('button').find((b) => b.text() === 'Mark all read')
    expect(markAllBtn).toBeUndefined()
  })

  it('calls markAllRead on the store when Mark all read is clicked', async () => {
    mockNotificationStore.notifications = [
      {
        id: 'n1',
        title: 'Unread',
        message: 'Message',
        boardId: null,
        type: 'Mention',
        cadence: 0,
        sourceEntityType: null,
        sourceEntityId: null,
        isRead: false,
        readAt: null,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
    ]

    const wrapper = mount(NotificationInboxView)
    await waitForUi()

    const markAllBtn = wrapper.findAll('button').find((b) => b.text() === 'Mark all read')
    await markAllBtn?.trigger('click')

    expect(mockNotificationStore.markAllRead).toHaveBeenCalledWith(undefined)
  })

  it('renders time header sections', async () => {
    const today = new Date()
    const yesterday = new Date(today)
    yesterday.setDate(yesterday.getDate() - 1)

    mockNotificationStore.notifications = [
      {
        id: 'n1',
        title: 'Today item',
        message: 'Message',
        boardId: null,
        type: 'Mention',
        cadence: 0,
        sourceEntityType: null,
        sourceEntityId: null,
        isRead: true,
        readAt: new Date().toISOString(),
        createdAt: today.toISOString(),
        updatedAt: today.toISOString(),
      },
      {
        id: 'n2',
        title: 'Yesterday item',
        message: 'Message',
        boardId: null,
        type: 'Assignment',
        cadence: 0,
        sourceEntityType: null,
        sourceEntityId: null,
        isRead: true,
        readAt: new Date().toISOString(),
        createdAt: yesterday.toISOString(),
        updatedAt: yesterday.toISOString(),
      },
    ]

    const wrapper = mount(NotificationInboxView)
    await waitForUi()

    expect(wrapper.text()).toContain('Today')
    expect(wrapper.text()).toContain('Yesterday')
  })

  it('shows collapsed summary for consecutive same-type notifications', async () => {
    const now = new Date()
    const t1 = new Date(now.getTime() - 1000)
    const t2 = new Date(now.getTime() - 2000)

    mockNotificationStore.notifications = [
      {
        id: 'n1',
        title: 'Mention 1',
        message: 'Message',
        boardId: null,
        type: 'Mention',
        cadence: 0,
        sourceEntityType: null,
        sourceEntityId: null,
        isRead: true,
        readAt: null,
        createdAt: t1.toISOString(),
        updatedAt: t1.toISOString(),
      },
      {
        id: 'n2',
        title: 'Mention 2',
        message: 'Message',
        boardId: null,
        type: 'Mention',
        cadence: 0,
        sourceEntityType: null,
        sourceEntityId: null,
        isRead: true,
        readAt: null,
        createdAt: t2.toISOString(),
        updatedAt: t2.toISOString(),
      },
    ]

    const wrapper = mount(NotificationInboxView)
    await waitForUi()

    expect(wrapper.text()).toContain('2 mention notifications')
  })
})
