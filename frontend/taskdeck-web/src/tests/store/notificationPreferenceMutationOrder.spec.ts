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

function preferences(enabled: boolean): NotificationPreference {
  return {
    userId: 'user-a',
    inAppChannelEnabled: true,
    mentionImmediateEnabled: enabled,
    mentionDigestEnabled: !enabled,
    assignmentImmediateEnabled: true,
    assignmentDigestEnabled: false,
    proposalOutcomeImmediateEnabled: true,
    proposalOutcomeDigestEnabled: false,
    createdAt: '2026-09-21T00:00:00Z',
    updatedAt: '2026-09-21T00:00:01Z',
  }
}

function request(enabled: boolean): UpdateNotificationPreferenceRequest {
  const value = preferences(enabled)
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

function notification(id: string): NotificationItem {
  return {
    id,
    userId: 'user-a',
    boardId: null,
    type: 'Mention',
    cadence: 'Immediate',
    title: id,
    message: id,
    sourceEntityType: null,
    sourceEntityId: null,
    isRead: false,
    readAt: null,
    createdAt: '2026-09-21T00:00:00Z',
    updatedAt: '2026-09-21T00:00:00Z',
  }
}

async function flushQueue() {
  await Promise.resolve()
  await Promise.resolve()
}

describe('notification preference mutation ordering', () => {
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

  it('starts the first save immediately and serializes the second intent', async () => {
    const first = deferred<NotificationPreference>()
    const second = deferred<NotificationPreference>()
    vi.mocked(notificationsApi.updatePreferences)
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(second.promise)

    const firstRequest = store.updatePreferences(request(true))
    const secondRequest = store.updatePreferences(request(false))
    expect(notificationsApi.updatePreferences).toHaveBeenCalledTimes(1)

    first.resolve(preferences(true))
    await firstRequest
    await flushQueue()
    expect(notificationsApi.updatePreferences).toHaveBeenCalledTimes(2)

    second.resolve(preferences(false))
    await secondRequest
    expect(store.preferences?.mentionImmediateEnabled).toBe(false)
  })

  it('continues with the queued save after its predecessor fails', async () => {
    const first = deferred<NotificationPreference>()
    const second = deferred<NotificationPreference>()
    vi.mocked(notificationsApi.updatePreferences)
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(second.promise)

    const firstRequest = store.updatePreferences(request(true))
    const secondRequest = store.updatePreferences(request(false))
    expect(notificationsApi.updatePreferences).toHaveBeenCalledTimes(1)

    first.reject(new Error('first save failed'))
    await expect(firstRequest).rejects.toThrow('first save failed')
    await flushQueue()
    expect(notificationsApi.updatePreferences).toHaveBeenCalledTimes(2)

    second.resolve(preferences(false))
    await secondRequest
    expect(store.preferences?.mentionImmediateEnabled).toBe(false)
  })

  it('does not start queued old-credential intent after token rotation', async () => {
    const first = deferred<NotificationPreference>()
    const second = deferred<NotificationPreference>()
    vi.mocked(notificationsApi.updatePreferences)
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(second.promise)

    const firstRequest = store.updatePreferences(request(true))
    const secondRequest = store.updatePreferences(request(false))
    const callsBeforeRotation = vi.mocked(notificationsApi.updatePreferences).mock.calls.length

    session.token = token('new')
    first.resolve(preferences(true))
    second.resolve(preferences(false))
    await Promise.all([firstRequest, secondRequest])

    expect(callsBeforeRotation).toBe(1)
    expect(notificationsApi.updatePreferences).toHaveBeenCalledTimes(1)
    expect(store.preferences).toBeNull()
    expect(store.loading).toBe(false)
  })

  it('keeps loading true from queued submission through final settlement', async () => {
    const first = deferred<NotificationPreference>()
    const second = deferred<NotificationPreference>()
    vi.mocked(notificationsApi.updatePreferences)
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(second.promise)

    const firstRequest = store.updatePreferences(request(true))
    const secondRequest = store.updatePreferences(request(false))
    expect(store.loading).toBe(true)

    first.resolve(preferences(true))
    await firstRequest
    await flushQueue()
    expect(store.loading).toBe(true)

    second.resolve(preferences(false))
    await secondRequest
    expect(store.loading).toBe(false)
  })

  it('does not erase an inbox failure when queued preference work starts', async () => {
    const first = deferred<NotificationPreference>()
    const second = deferred<NotificationPreference>()
    const inbox = deferred<NotificationItem[]>()
    vi.mocked(notificationsApi.updatePreferences)
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(second.promise)
    vi.mocked(notificationsApi.getNotifications).mockReturnValue(inbox.promise)

    const firstRequest = store.updatePreferences(request(true))
    const secondRequest = store.updatePreferences(request(false))
    const inboxRequest = store.fetchNotifications()
    expect(notificationsApi.updatePreferences).toHaveBeenCalledTimes(1)

    inbox.reject(new Error('inbox failed'))
    await expect(inboxRequest).rejects.toThrow('inbox failed')
    expect(store.error).toBe('inbox failed')

    first.resolve(preferences(true))
    await firstRequest
    await flushQueue()
    expect(notificationsApi.updatePreferences).toHaveBeenCalledTimes(2)
    expect(store.error).toBe('inbox failed')

    second.resolve(preferences(false))
    await secondRequest
    expect(store.error).toBe('inbox failed')
  })
})
