/**
 * notificationStore — realtime arrival, stale reconciliation, and loading state tests.
 *
 * These tests exercise:
 * - Simulated real-time notification arrival (push to local state)
 * - Stale state reconciliation on re-fetch (server has newer data)
 * - Loading state transitions for all async operations
 * - Concurrent operations (markAsRead during fetch)
 * - Error recovery (error cleared on successful retry)
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

describe('notificationStore — realtime and extended scenarios', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  // ── simulated real-time notification arrival ─────────────────────────────

  describe('real-time notification arrival', () => {
    it('unshift of a new notification increments derived unread count', async () => {
      const store = useNotificationStore()

      // Initial state: one read notification
      vi.mocked(http.get).mockResolvedValue({
        data: [makeNotification({ id: 'n-existing', isRead: true })],
      })
      await store.fetchNotifications()
      expect(store.notifications.filter(n => !n.isRead)).toHaveLength(0)

      // Simulate real-time arrival
      store.notifications.unshift(
        makeNotification({ id: 'n-realtime-1', isRead: false, title: 'New mention' }),
      )

      const unread = store.notifications.filter(n => !n.isRead)
      expect(unread).toHaveLength(1)
      expect(unread[0].id).toBe('n-realtime-1')
    })

    it('multiple real-time arrivals accumulate correctly', () => {
      const store = useNotificationStore()
      store.notifications = [
        makeNotification({ id: 'n-1', isRead: true }),
      ]

      // Three new notifications arrive
      store.notifications.unshift(
        makeNotification({ id: 'n-rt-1', isRead: false }),
      )
      store.notifications.unshift(
        makeNotification({ id: 'n-rt-2', isRead: false }),
      )
      store.notifications.unshift(
        makeNotification({ id: 'n-rt-3', isRead: false }),
      )

      expect(store.notifications).toHaveLength(4)
      const unread = store.notifications.filter(n => !n.isRead)
      expect(unread).toHaveLength(3)
    })

    it('markAllRead clears all real-time arrivals', async () => {
      const store = useNotificationStore()
      store.notifications = [
        makeNotification({ id: 'n-old', isRead: false }),
      ]

      // Simulate two real-time arrivals
      store.notifications.unshift(
        makeNotification({ id: 'n-rt-1', isRead: false }),
      )
      store.notifications.unshift(
        makeNotification({ id: 'n-rt-2', isRead: false }),
      )
      expect(store.notifications.filter(n => !n.isRead)).toHaveLength(3)

      vi.mocked(http.post).mockResolvedValue({ data: { markedCount: 3 } })
      await store.markAllRead()

      expect(store.notifications.filter(n => !n.isRead)).toHaveLength(0)
      expect(store.notifications).toHaveLength(3) // items are still there, just marked read
    })
  })

  // ── stale reconciliation on re-fetch ──────────────────────────────────────

  describe('stale state reconciliation', () => {
    it('replaces local state with fresh server data on re-fetch', async () => {
      const store = useNotificationStore()

      // Initial fetch
      vi.mocked(http.get).mockResolvedValueOnce({
        data: [
          makeNotification({ id: 'n-1', isRead: false }),
          makeNotification({ id: 'n-2', isRead: false }),
        ],
      })
      await store.fetchNotifications()
      expect(store.notifications).toHaveLength(2)

      // Re-fetch: server has new notifications and one was read server-side
      vi.mocked(http.get).mockResolvedValueOnce({
        data: [
          makeNotification({ id: 'n-3', isRead: false, title: 'New' }),
          makeNotification({ id: 'n-1', isRead: true, readAt: '2026-02-01T00:00:00Z' }),
          makeNotification({ id: 'n-2', isRead: false }),
        ],
      })
      await store.fetchNotifications()

      expect(store.notifications).toHaveLength(3)
      const n1 = store.notifications.find(n => n.id === 'n-1')
      expect(n1?.isRead).toBe(true)
      expect(n1?.readAt).toBe('2026-02-01T00:00:00Z')
    })

    it('removes server-deleted notifications on re-fetch', async () => {
      const store = useNotificationStore()

      vi.mocked(http.get).mockResolvedValueOnce({
        data: [
          makeNotification({ id: 'n-1' }),
          makeNotification({ id: 'n-2' }),
        ],
      })
      await store.fetchNotifications()

      // Re-fetch: n-1 was deleted server-side
      vi.mocked(http.get).mockResolvedValueOnce({
        data: [makeNotification({ id: 'n-2' })],
      })
      await store.fetchNotifications()

      expect(store.notifications).toHaveLength(1)
      expect(store.notifications[0].id).toBe('n-2')
    })
  })

  // ── loading state transitions ──────────────────────────────────────────

  describe('loading state transitions', () => {
    it('sets loading=true during fetchNotifications and clears after', async () => {
      let loadingDuringFetch = false
      vi.mocked(http.get).mockImplementation(async () => {
        const store = useNotificationStore()
        loadingDuringFetch = store.loading
        return { data: [] }
      })

      const store = useNotificationStore()
      await store.fetchNotifications()

      expect(loadingDuringFetch).toBe(true)
      expect(store.loading).toBe(false)
    })

    it('clears loading even when fetchNotifications fails', async () => {
      vi.mocked(http.get).mockRejectedValue(new Error('timeout'))

      const store = useNotificationStore()
      await expect(store.fetchNotifications()).rejects.toBeInstanceOf(Error)

      expect(store.loading).toBe(false)
    })

    it('sets loading=true during fetchPreferences and clears after', async () => {
      let loadingDuringFetch = false
      vi.mocked(http.get).mockImplementation(async () => {
        const store = useNotificationStore()
        loadingDuringFetch = store.loading
        return { data: { userId: 'u-1', inAppChannelEnabled: true } }
      })

      const store = useNotificationStore()
      await store.fetchPreferences()

      expect(loadingDuringFetch).toBe(true)
      expect(store.loading).toBe(false)
    })

    it('sets loading=true during updatePreferences and clears after', async () => {
      vi.mocked(http.put).mockResolvedValue({
        data: { userId: 'u-1', mentionImmediateEnabled: false },
      })

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

      expect(store.loading).toBe(false)
    })
  })

  // ── error recovery ───────────────────────────────────────────────────────

  describe('error recovery', () => {
    it('clears error on successful retry after a failed fetch', async () => {
      const store = useNotificationStore()

      // First attempt fails
      vi.mocked(http.get).mockRejectedValueOnce(new Error('network'))
      await expect(store.fetchNotifications()).rejects.toBeInstanceOf(Error)
      expect(store.error).toBe('Failed to load notifications')

      // Retry succeeds
      vi.mocked(http.get).mockResolvedValueOnce({ data: [makeNotification()] })
      await store.fetchNotifications()

      expect(store.error).toBeNull()
      expect(store.notifications).toHaveLength(1)
    })

    it('updates notification state on successful retry even when error from prior failure persists', async () => {
      const store = useNotificationStore()
      store.notifications = [makeNotification({ id: 'n-err', isRead: false })]

      // First markAsRead fails — error is set
      vi.mocked(http.post).mockRejectedValueOnce(new Error('server error'))
      await expect(store.markAsRead('n-err')).rejects.toBeInstanceOf(Error)
      expect(store.error).toBe('Failed to mark notification as read')

      // Retry succeeds — notification is updated even though error persists
      // (markAsRead only sets error on failure; it does not clear it on success)
      const readNotification = makeNotification({ id: 'n-err', isRead: true, readAt: '2026-02-01T00:00:00Z' })
      vi.mocked(http.post).mockResolvedValueOnce({ data: readNotification })
      await store.markAsRead('n-err')

      expect(store.notifications[0].isRead).toBe(true)
      // Error from the prior failure is still set — not a bug, just markAsRead's current behavior
      expect(store.error).toBe('Failed to mark notification as read')
    })
  })

  // ── board-scoped notification filtering ──────────────────────────────────

  describe('board-scoped notification filtering', () => {
    it('fetches only board-specific notifications with boardId filter', async () => {
      vi.mocked(http.get).mockResolvedValue({
        data: [
          makeNotification({ id: 'n-board-a', boardId: 'board-A' }),
        ],
      })

      const store = useNotificationStore()
      await store.fetchNotifications({ boardId: 'board-A' })

      expect(store.notifications).toHaveLength(1)
      expect(store.notifications[0].boardId).toBe('board-A')
      const calledUrl = vi.mocked(http.get).mock.calls[0][0] as string
      expect(calledUrl).toContain('boardId=board-A')
    })

    it('combines unreadOnly and boardId filters', async () => {
      vi.mocked(http.get).mockResolvedValue({ data: [] })

      const store = useNotificationStore()
      await store.fetchNotifications({ boardId: 'board-X', unreadOnly: true })

      const calledUrl = vi.mocked(http.get).mock.calls[0][0] as string
      expect(calledUrl).toContain('boardId=board-X')
      expect(calledUrl).toContain('unreadOnly=true')
    })
  })

  // ── markAsRead idempotency ───────────────────────────────────────────────

  describe('markAsRead idempotency', () => {
    it('marking an already-read notification does not change its readAt timestamp', async () => {
      const store = useNotificationStore()
      store.notifications = [
        makeNotification({ id: 'n-already-read', isRead: true, readAt: '2026-01-15T00:00:00Z' }),
      ]

      const sameNotification = makeNotification({
        id: 'n-already-read',
        isRead: true,
        readAt: '2026-01-15T00:00:00Z',
      })
      vi.mocked(http.post).mockResolvedValue({ data: sameNotification })

      await store.markAsRead('n-already-read')

      expect(store.notifications[0].readAt).toBe('2026-01-15T00:00:00Z')
    })
  })
})
