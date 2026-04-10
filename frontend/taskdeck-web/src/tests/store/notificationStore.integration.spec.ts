/**
 * notificationStore integration tests — store + real notificationsApi module, HTTP layer mocked.
 *
 * These tests exercise the full store → notificationsApi → http chain.
 * Mocking http (not the API module) catches shape mismatches and verifies
 * the correct URL construction for query strings and path parameters.
 */
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import http from '../../api/http'
import { useNotificationStore } from '../../store/notificationStore'
import type { NotificationItem } from '../../types/notifications'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    patch: vi.fn(),
    delete: vi.fn(),
  },
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => ({ error: vi.fn(), success: vi.fn(), warning: vi.fn(), info: vi.fn() }),
}))

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (_error: unknown, fallback: string) => ({ message: fallback }),
}))

vi.mock('../../utils/demoMode', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../utils/demoMode')>()
  return { ...actual, isDemoMode: false }
})

function makeNotification(overrides: Partial<NotificationItem> = {}): NotificationItem {
  return {
    id: 'n-1',
    userId: 'u-1',
    boardId: null,
    type: 'Mention',
    cadence: 'Immediate',
    title: 'You were mentioned',
    message: 'Someone mentioned you in a comment',
    sourceEntityType: 'card',
    sourceEntityId: 'card-1',
    isRead: false,
    readAt: null,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  }
}

function makePreferences(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    userId: 'u-1',
    inAppChannelEnabled: true,
    mentionImmediateEnabled: true,
    mentionDigestEnabled: false,
    assignmentImmediateEnabled: true,
    assignmentDigestEnabled: false,
    proposalOutcomeImmediateEnabled: true,
    proposalOutcomeDigestEnabled: false,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  }
}

describe('notificationStore — integration (real notificationsApi, mocked HTTP)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  // ── fetchNotifications ────────────────────────────────────────────────────

  describe('fetchNotifications', () => {
    it('calls GET /notifications and populates store.notifications', async () => {
      const items = [makeNotification(), makeNotification({ id: 'n-2', isRead: true })]
      vi.mocked(http.get).mockResolvedValue({ data: items })

      const store = useNotificationStore()
      await store.fetchNotifications()

      expect(store.notifications).toHaveLength(2)
      expect(store.notifications[0].id).toBe('n-1')
      expect(store.error).toBeNull()
      expect(http.get).toHaveBeenCalledWith(expect.stringContaining('/notifications'))
    })

    it('appends unreadOnly=true to the query string when requested', async () => {
      vi.mocked(http.get).mockResolvedValue({ data: [] })

      const store = useNotificationStore()
      await store.fetchNotifications({ unreadOnly: true })

      const calledUrl = vi.mocked(http.get).mock.calls[0][0] as string
      expect(calledUrl).toContain('unreadOnly=true')
    })

    it('appends boardId to the query string for board-scoped filtering', async () => {
      vi.mocked(http.get).mockResolvedValue({ data: [] })

      const store = useNotificationStore()
      await store.fetchNotifications({ boardId: 'board-xyz' })

      const calledUrl = vi.mocked(http.get).mock.calls[0][0] as string
      expect(calledUrl).toContain('boardId=board-xyz')
    })

    it('sets error when GET /notifications fails', async () => {
      vi.mocked(http.get).mockRejectedValue(new Error('network'))

      const store = useNotificationStore()
      await expect(store.fetchNotifications()).rejects.toBeInstanceOf(Error)

      expect(store.error).toBe('Failed to load notifications')
    })
  })

  // ── markAsRead ────────────────────────────────────────────────────────────

  describe('markAsRead', () => {
    it('posts to /notifications/:id/read and updates isRead in local state', async () => {
      const store = useNotificationStore()
      store.notifications = [
        makeNotification({ id: 'n-1', isRead: false }),
        makeNotification({ id: 'n-2', isRead: false }),
      ]

      const readResponse = makeNotification({ id: 'n-1', isRead: true, readAt: '2026-02-01T00:00:00Z' })
      vi.mocked(http.post).mockResolvedValue({ data: readResponse })

      await store.markAsRead('n-1')

      expect(store.notifications[0].isRead).toBe(true)
      // The other notification must be untouched
      expect(store.notifications[1].isRead).toBe(false)
      expect(http.post).toHaveBeenCalledWith('/notifications/n-1/read')
    })

    it('correctly URL-encodes special characters in the notification id', async () => {
      const store = useNotificationStore()
      store.notifications = [makeNotification({ id: 'n/special+id' })]

      vi.mocked(http.post).mockResolvedValue({ data: makeNotification({ id: 'n/special+id', isRead: true }) })

      await store.markAsRead('n/special+id')

      const calledUrl = vi.mocked(http.post).mock.calls[0][0] as string
      // The slash and plus must be percent-encoded
      expect(calledUrl).not.toContain('n/special+id')
      expect(calledUrl).toContain('n%2Fspecial%2Bid')
    })

    it('sets error when POST /notifications/:id/read fails', async () => {
      const store = useNotificationStore()
      vi.mocked(http.post).mockRejectedValue(new Error('server error'))

      await expect(store.markAsRead('n-missing')).rejects.toBeInstanceOf(Error)
      expect(store.error).toBe('Failed to mark notification as read')
    })
  })

  // ── markAllRead ───────────────────────────────────────────────────────────

  describe('markAllRead', () => {
    it('posts to /notifications/mark-all-read and marks all local items as read', async () => {
      const store = useNotificationStore()
      store.notifications = [
        makeNotification({ id: 'n-1', isRead: false }),
        makeNotification({ id: 'n-2', isRead: false }),
      ]

      vi.mocked(http.post).mockResolvedValue({ data: { markedCount: 2 } })
      await store.markAllRead()

      expect(store.notifications[0].isRead).toBe(true)
      expect(store.notifications[1].isRead).toBe(true)
      expect(http.post).toHaveBeenCalledWith('/notifications/mark-all-read')
    })

    it('appends boardId query parameter for board-scoped mark-all-read', async () => {
      const store = useNotificationStore()
      store.notifications = [
        makeNotification({ id: 'n-1', boardId: 'board-A', isRead: false }),
        makeNotification({ id: 'n-2', boardId: 'board-B', isRead: false }),
      ]

      vi.mocked(http.post).mockResolvedValue({ data: { markedCount: 1 } })
      await store.markAllRead('board-A')

      // Only the board-A notification should be marked read in local state
      expect(store.notifications[0].isRead).toBe(true)
      expect(store.notifications[1].isRead).toBe(false)
      const calledUrl = vi.mocked(http.post).mock.calls[0][0] as string
      expect(calledUrl).toContain('boardId=board-A')
    })

    it('sets error when POST /notifications/mark-all-read fails', async () => {
      const store = useNotificationStore()
      vi.mocked(http.post).mockRejectedValue(new Error('batch failed'))

      await expect(store.markAllRead()).rejects.toBeInstanceOf(Error)
      expect(store.error).toBe('Failed to mark all notifications as read')
    })
  })

  // ── unread count behavior ──────────────────────────────────────────────

  describe('unread count behavior', () => {
    it('unread count derives from isRead=false notifications after fetch', async () => {
      const items = [
        makeNotification({ id: 'n-1', isRead: false }),
        makeNotification({ id: 'n-2', isRead: false }),
        makeNotification({ id: 'n-3', isRead: true }),
      ]
      vi.mocked(http.get).mockResolvedValue({ data: items })

      const store = useNotificationStore()
      await store.fetchNotifications()

      const unreadCount = store.notifications.filter(n => !n.isRead).length
      expect(unreadCount).toBe(2)
    })

    it('unread count drops to 0 after markAllRead', async () => {
      const store = useNotificationStore()
      store.notifications = [
        makeNotification({ id: 'n-1', isRead: false }),
        makeNotification({ id: 'n-2', isRead: false }),
        makeNotification({ id: 'n-3', isRead: false }),
      ]

      vi.mocked(http.post).mockResolvedValue({ data: { markedCount: 3 } })
      await store.markAllRead()

      const unreadCount = store.notifications.filter(n => !n.isRead).length
      expect(unreadCount).toBe(0)
    })

    it('unread count decrements by 1 after marking a single notification as read', async () => {
      const store = useNotificationStore()
      store.notifications = [
        makeNotification({ id: 'n-1', isRead: false }),
        makeNotification({ id: 'n-2', isRead: false }),
      ]

      const readResponse = makeNotification({ id: 'n-1', isRead: true, readAt: '2026-02-01T00:00:00Z' })
      vi.mocked(http.post).mockResolvedValue({ data: readResponse })

      await store.markAsRead('n-1')

      const unreadCount = store.notifications.filter(n => !n.isRead).length
      expect(unreadCount).toBe(1)
    })

    it('adding a new unread notification locally increases the unread count', () => {
      const store = useNotificationStore()
      store.notifications = [
        makeNotification({ id: 'n-1', isRead: true }),
      ]

      // Simulate a real-time notification arriving
      store.notifications.unshift(makeNotification({ id: 'n-realtime', isRead: false }))

      const unreadCount = store.notifications.filter(n => !n.isRead).length
      expect(unreadCount).toBe(1)
    })
  })

  // ── board-scoped mark all read and unread count ──────────────────────────

  describe('board-scoped unread count', () => {
    it('only marks notifications for the specified board as read, preserving others unread', async () => {
      const store = useNotificationStore()
      store.notifications = [
        makeNotification({ id: 'n-1', boardId: 'board-A', isRead: false }),
        makeNotification({ id: 'n-2', boardId: 'board-B', isRead: false }),
        makeNotification({ id: 'n-3', boardId: 'board-A', isRead: false }),
      ]

      vi.mocked(http.post).mockResolvedValue({ data: { markedCount: 2 } })
      await store.markAllRead('board-A')

      const boardAUnread = store.notifications.filter(n => n.boardId === 'board-A' && !n.isRead).length
      const boardBUnread = store.notifications.filter(n => n.boardId === 'board-B' && !n.isRead).length
      expect(boardAUnread).toBe(0)
      expect(boardBUnread).toBe(1)
    })
  })

  // ── preferences ───────────────────────────────────────────────────────────

  describe('fetchPreferences', () => {
    it('calls GET /notifications/preferences and populates store.preferences', async () => {
      const prefs = makePreferences()
      vi.mocked(http.get).mockResolvedValue({ data: prefs })

      const store = useNotificationStore()
      await store.fetchPreferences()

      expect(store.preferences?.mentionImmediateEnabled).toBe(true)
      expect(store.preferences?.mentionDigestEnabled).toBe(false)
      expect(http.get).toHaveBeenCalledWith('/notifications/preferences')
    })

    it('sets error when GET /notifications/preferences fails', async () => {
      vi.mocked(http.get).mockRejectedValue(new Error('network'))

      const store = useNotificationStore()
      await expect(store.fetchPreferences()).rejects.toBeInstanceOf(Error)
      expect(store.error).toBe('Failed to load notification preferences')
    })
  })

  describe('updatePreferences', () => {
    it('sends PUT /notifications/preferences and updates store.preferences', async () => {
      const updated = makePreferences({ mentionImmediateEnabled: false, mentionDigestEnabled: true })
      vi.mocked(http.put).mockResolvedValue({ data: updated })

      const store = useNotificationStore()
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
      expect(http.put).toHaveBeenCalledWith('/notifications/preferences', expect.any(Object))
    })

    it('sets error when PUT /notifications/preferences fails', async () => {
      vi.mocked(http.put).mockRejectedValue(new Error('save failed'))

      const store = useNotificationStore()
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
    })
  })
})
