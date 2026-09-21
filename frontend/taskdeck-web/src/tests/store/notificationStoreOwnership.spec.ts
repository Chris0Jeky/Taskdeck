import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { notificationsApi } from '../../api/notificationsApi'
import { useNotificationStore } from '../../store/notificationStore'
import { useSessionStore } from '../../store/sessionStore'
import type {
  NotificationItem,
  NotificationPreference,
  UpdateNotificationPreferenceRequest,
} from '../../types/notifications'

const toastMocks = vi.hoisted(() => ({
  error: vi.fn(),
  success: vi.fn(),
  info: vi.fn(),
  warning: vi.fn(),
}))

vi.mock('../../utils/demoMode', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../utils/demoMode')>()
  return { ...actual, isDemoMode: false }
})

vi.mock('../../api/notificationsApi', () => ({
  notificationsApi: {
    getNotifications: vi.fn(),
    markAsRead: vi.fn(),
    markAllRead: vi.fn(),
    getPreferences: vi.fn(),
    updatePreferences: vi.fn(),
  },
}))

vi.mock('../../api/authApi', () => ({
  authApi: {
    login: vi.fn(),
    register: vi.fn(),
    changePassword: vi.fn(),
    refreshToken: vi.fn(),
    exchangeOAuthCode: vi.fn(),
    exchangeOidcCode: vi.fn(),
  },
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => toastMocks,
}))

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (error: unknown, fallback: string) => ({
    message: error instanceof Error ? error.message : fallback,
  }),
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((yes, no) => {
    resolve = yes
    reject = no
  })
  return { promise, resolve, reject }
}

function token(suffix: string): string {
  const body = btoa(JSON.stringify({ exp: 1893456000 }))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/g, '')
  return `header.${body}.${suffix}`
}

function notification(id: string, isRead = false): NotificationItem {
  return {
    id,
    userId: 'user-a',
    boardId: null,
    type: 'Mention',
    cadence: 'Immediate',
    title: id,
    message: id,
    sourceEntityType: 'card',
    sourceEntityId: 'card-1',
    isRead,
    readAt: isRead ? '2026-09-21T00:00:01Z' : null,
    createdAt: '2026-09-21T00:00:00Z',
    updatedAt: '2026-09-21T00:00:01Z',
  }
}

function preferences(
  mentionImmediateEnabled: boolean,
): NotificationPreference {
  return {
    userId: 'user-a',
    inAppChannelEnabled: true,
    mentionImmediateEnabled,
    mentionDigestEnabled: !mentionImmediateEnabled,
    assignmentImmediateEnabled: true,
    assignmentDigestEnabled: false,
    proposalOutcomeImmediateEnabled: true,
    proposalOutcomeDigestEnabled: false,
    createdAt: '2026-09-21T00:00:00Z',
    updatedAt: '2026-09-21T00:00:01Z',
  }
}

function preferenceRequest(
  mentionImmediateEnabled: boolean,
): UpdateNotificationPreferenceRequest {
  const value = preferences(mentionImmediateEnabled)
  return {
    inAppChannelEnabled: value.inAppChannelEnabled,
    mentionImmediateEnabled: value.mentionImmediateEnabled,
    mentionDigestEnabled: value.mentionDigestEnabled,
    assignmentImmediateEnabled: value.assignmentImmediateEnabled,
    assignmentDigestEnabled: value.assignmentDigestEnabled,
    proposalOutcomeImmediateEnabled: value.proposalOutcomeImmediateEnabled,
    proposalOutcomeDigestEnabled: value.proposalOutcomeDigestEnabled,
  }
}

describe('notificationStore async ownership', () => {
  let session: ReturnType<typeof useSessionStore>
  let store: ReturnType<typeof useNotificationStore>

  beforeEach(() => {
    setActivePinia(createPinia())
    session = useSessionStore()
    session.userId = 'user-a'
    session.token = token('old')
    store = useNotificationStore()
    vi.clearAllMocks()
  })

  it('keeps the newest inbox read when responses settle in reverse order', async () => {
    const older = deferred<NotificationItem[]>()
    const newer = deferred<NotificationItem[]>()
    vi.mocked(notificationsApi.getNotifications)
      .mockReturnValueOnce(older.promise)
      .mockReturnValueOnce(newer.promise)

    const oldRequest = store.fetchNotifications({ boardId: 'board-old' })
    const newRequest = store.fetchNotifications({ boardId: 'board-new' })
    newer.resolve([notification('new')])
    await newRequest
    older.resolve([notification('old')])
    await oldRequest

    expect(store.notifications.map(item => item.id)).toEqual(['new'])
  })

  it('keeps the newest preference read when responses settle in reverse order', async () => {
    const older = deferred<NotificationPreference>()
    const newer = deferred<NotificationPreference>()
    vi.mocked(notificationsApi.getPreferences)
      .mockReturnValueOnce(older.promise)
      .mockReturnValueOnce(newer.promise)

    const oldRequest = store.fetchPreferences()
    const newRequest = store.fetchPreferences()
    newer.resolve(preferences(false))
    await newRequest
    older.resolve(preferences(true))
    await oldRequest

    expect(store.preferences?.mentionImmediateEnabled).toBe(false)
  })

  it('does not let an older inbox read undo a confirmed markAsRead', async () => {
    store.notifications = [notification('n-1')]
    const read = deferred<NotificationItem[]>()
    vi.mocked(notificationsApi.getNotifications).mockReturnValue(read.promise)
    vi.mocked(notificationsApi.markAsRead).mockResolvedValue(notification('n-1', true))

    const readRequest = store.fetchNotifications()
    await store.markAsRead('n-1')
    read.resolve([notification('n-1', false)])
    await readRequest

    expect(store.notifications[0]?.isRead).toBe(true)
  })

  it('does not let an older inbox read undo a confirmed markAllRead', async () => {
    store.notifications = [notification('n-1'), notification('n-2')]
    const read = deferred<NotificationItem[]>()
    vi.mocked(notificationsApi.getNotifications).mockReturnValue(read.promise)
    vi.mocked(notificationsApi.markAllRead).mockResolvedValue({ markedCount: 2 })

    const readRequest = store.fetchNotifications()
    await store.markAllRead()
    read.resolve([notification('n-1'), notification('n-2')])
    await readRequest

    expect(store.notifications.every(item => item.isRead)).toBe(true)
  })

  it('does not let an older preference read undo a confirmed update', async () => {
    store.preferences = preferences(true)
    const read = deferred<NotificationPreference>()
    vi.mocked(notificationsApi.getPreferences).mockReturnValue(read.promise)
    vi.mocked(notificationsApi.updatePreferences).mockResolvedValue(preferences(false))

    const readRequest = store.fetchPreferences()
    await store.updatePreferences(preferenceRequest(false))
    read.resolve(preferences(true))
    await readRequest

    expect(store.preferences?.mentionImmediateEnabled).toBe(false)
  })

  it('keeps loading true while an independent notification operation remains pending', async () => {
    const inbox = deferred<NotificationItem[]>()
    const prefs = deferred<NotificationPreference>()
    vi.mocked(notificationsApi.getNotifications).mockReturnValue(inbox.promise)
    vi.mocked(notificationsApi.getPreferences).mockReturnValue(prefs.promise)

    const inboxRequest = store.fetchNotifications()
    const prefsRequest = store.fetchPreferences()
    inbox.resolve([])
    await inboxRequest

    expect(store.loading).toBe(true)

    prefs.resolve(preferences(true))
    await prefsRequest
    expect(store.loading).toBe(false)
  })

  it('clears both surfaces and rejects old read settlement after token rotation', async () => {
    store.notifications = [notification('existing')]
    store.preferences = preferences(true)
    const inbox = deferred<NotificationItem[]>()
    const prefs = deferred<NotificationPreference>()
    vi.mocked(notificationsApi.getNotifications).mockReturnValue(inbox.promise)
    vi.mocked(notificationsApi.getPreferences).mockReturnValue(prefs.promise)

    const inboxRequest = store.fetchNotifications()
    const prefsRequest = store.fetchPreferences()
    session.token = token('new')
    const clearedImmediately = store.notifications.length === 0 && store.preferences === null

    inbox.resolve([notification('old-token')])
    prefs.resolve(preferences(false))
    await Promise.all([inboxRequest, prefsRequest])

    expect(clearedImmediately).toBe(true)
    expect(store.notifications).toEqual([])
    expect(store.preferences).toBeNull()
    expect(store.loading).toBe(false)
    expect(store.error).toBeNull()
  })

  it('suppresses a stale read failure after token rotation', async () => {
    const pending = deferred<NotificationItem[]>()
    vi.mocked(notificationsApi.getNotifications).mockReturnValue(pending.promise)
    const request = store.fetchNotifications()

    session.token = token('new')
    pending.reject(new Error('old credential read failed'))
    await expect(request).rejects.toThrow('old credential read failed')

    expect(store.notifications).toEqual([])
    expect(store.error).toBeNull()
    expect(store.loading).toBe(false)
    expect(toastMocks.error).not.toHaveBeenCalled()
  })

  it('suppresses a stale markAsRead success after token rotation', async () => {
    store.notifications = [notification('n-1')]
    const pending = deferred<NotificationItem>()
    vi.mocked(notificationsApi.markAsRead).mockReturnValue(pending.promise)
    const request = store.markAsRead('n-1')

    session.token = token('new')
    pending.resolve(notification('n-1', true))
    await request

    expect(store.notifications).toEqual([])
    expect(store.error).toBeNull()
  })

  it('suppresses a stale preference mutation failure after token rotation', async () => {
    const pending = deferred<NotificationPreference>()
    vi.mocked(notificationsApi.updatePreferences).mockReturnValue(pending.promise)
    const request = store.updatePreferences(preferenceRequest(false))

    session.token = token('new')
    pending.reject(new Error('old credential save failed'))
    await expect(request).rejects.toThrow('old credential save failed')

    expect(store.preferences).toBeNull()
    expect(store.error).toBeNull()
    expect(store.loading).toBe(false)
    expect(toastMocks.error).not.toHaveBeenCalled()
  })
})
