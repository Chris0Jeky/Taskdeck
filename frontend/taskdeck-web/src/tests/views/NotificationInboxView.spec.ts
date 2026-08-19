import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import NotificationInboxView from '../../views/NotificationInboxView.vue'
import notificationInboxSource from '../../views/NotificationInboxView.vue?raw'

const vueHelpers = vi.hoisted(async () => {
  const { computed, ref, shallowRef } = await import('vue')
  return { computed, ref, shallowRef }
})

vi.mock('../../composables/useVirtualList', async () => {
  const { computed, ref, shallowRef } = await vueHelpers
  return {
    useVirtualList: (options: { count: { value: number } | (() => number); estimateSize: number }) => {
      const count = options.count
      const getCount = typeof count === 'function'
        ? count
        : () => count.value
      return {
        parentRef: ref(null),
        virtualItemEls: shallowRef([]),
        virtualRows: computed(() =>
          Array.from({ length: getCount() }, (_, i) => ({
            key: i,
            index: i,
            start: i * options.estimateSize,
            end: (i + 1) * options.estimateSize,
            size: options.estimateSize,
            lane: 0,
          })),
        ),
        totalSize: computed(() => getCount() * options.estimateSize),
        translateY: computed(() => 0),
        scrollToIndex: vi.fn(),
      }
    },
  }
})

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

    const action = wrapper.get('button.paper-notifications__mark-read')
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

  it('gives each notification type its own left accent stripe class', async () => {
    const now = new Date()
    mockNotificationStore.notifications = [
      {
        id: 'n1',
        title: 'Mention',
        message: 'Message',
        boardId: null,
        type: 'Mention',
        cadence: 0,
        sourceEntityType: null,
        sourceEntityId: null,
        isRead: true,
        readAt: null,
        createdAt: new Date(now.getTime() - 1000).toISOString(),
        updatedAt: new Date(now.getTime() - 1000).toISOString(),
      },
      {
        id: 'n2',
        title: 'Assignment',
        message: 'Message',
        boardId: null,
        type: 'Assignment',
        cadence: 0,
        sourceEntityType: null,
        sourceEntityId: null,
        isRead: true,
        readAt: null,
        createdAt: new Date(now.getTime() - 2000).toISOString(),
        updatedAt: new Date(now.getTime() - 2000).toISOString(),
      },
    ]

    const wrapper = mount(NotificationInboxView)
    await waitForUi()

    const rows = wrapper.findAll('.paper-notifications__row')
    expect(rows).toHaveLength(2)

    const stripes = rows.map((row) => row.classes().filter((c) => c.startsWith('border-l-')))
    expect(stripes[0]).toContain('border-l-4')
    expect(stripes[1]).toContain('border-l-4')

    const colours = stripes.map((classes) => classes.find((c) => c !== 'border-l-4'))
    expect(colours[0]).toBeTruthy()
    expect(colours[1]).toBeTruthy()
    expect(colours[0]).not.toBe(colours[1])
  })
})

/**
 * Scoped-CSS guard for the per-type accent stripe (#1781 Paper restyle).
 *
 * `typeBorderClass` puts Tailwind's `border-l-4 border-l-<colour>` on each
 * notification card, and that stripe is the type signal — information, not
 * decoration. Vue compiles `<style scoped>` rules to `.selector[data-v-…]`
 * (specificity 0,2,0), which outranks those single-class utilities (0,1,0), so
 * ANY `border` / `border-color` shorthand — or an explicit left-edge
 * declaration — in the scoped block silently erases the stripe on every type at
 * once. jsdom loads no Tailwind sheet and computes no cascade across a scoped
 * SFC block, so the mounted assertions above cannot see this; the source is the
 * only place it is observable in a unit test.
 *
 * The invariant: the card rules declare their borders per side and leave the
 * left edge entirely undeclared.
 */
describe('NotificationInboxView scoped card borders', () => {
  const CARD_RULES = [
    '.paper-notifications__row',
    '.paper-notifications__group-summary',
    '.paper-notifications__row--unread',
  ] as const

  /** Body of the first top-level rule for `selector`, comments stripped. */
  function readRule(selector: string): string {
    const pattern = new RegExp(`^\\${selector}\\s*\\{([\\s\\S]*?)\\}`, 'm')
    const match = notificationInboxSource.match(pattern)
    if (!match) throw new Error(`Could not locate the ${selector} rule`)
    return match[1]!.replace(/\/\*[\s\S]*?\*\//g, '')
  }

  it.each(CARD_RULES)('%s declares no left border, so the type stripe survives', (selector) => {
    const body = readRule(selector)

    expect(body).not.toMatch(/(^|[\s;])border\s*:/)
    expect(body).not.toMatch(/(^|[\s;])border-color\s*:/)
    expect(body).not.toMatch(/border-left/)
    expect(body).not.toMatch(/border-inline-start/)
  })

  it('still paints the other three sides of a notification card', () => {
    const body = readRule('.paper-notifications__row')

    expect(body).toMatch(/border-top:\s*1px solid var\(--line/)
    expect(body).toMatch(/border-right:\s*1px solid var\(--line/)
    expect(body).toMatch(/border-bottom:\s*1px solid var\(--line/)
  })

  it('marks unread cards per side rather than with a border-color shorthand', () => {
    const body = readRule('.paper-notifications__row--unread')

    expect(body).toMatch(/border-top-color:\s*var\(--ember/)
    expect(body).toMatch(/border-right-color:\s*var\(--ember/)
    expect(body).toMatch(/border-bottom-color:\s*var\(--ember/)
  })
})
