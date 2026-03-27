import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'

vi.mock('../../utils/demoMode', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../utils/demoMode')>()
  return {
    ...actual,
    isDemoMode: true,
  }
})

vi.mock('../../api/notificationsApi', () => ({
  notificationsApi: {
    getNotifications: vi.fn(),
    markAsRead: vi.fn(),
    getPreferences: vi.fn(),
    updatePreferences: vi.fn(),
  },
}))

const toastMocks = vi.hoisted(() => ({
  success: vi.fn(),
  error: vi.fn(),
  info: vi.fn(),
  warning: vi.fn(),
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => toastMocks,
}))

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (_error: unknown, fallback: string) => ({ message: fallback }),
}))

import { useNotificationStore } from '../../store/notificationStore'
import { notificationsApi } from '../../api/notificationsApi'

describe('notificationStore demo mode', () => {
  let store: ReturnType<typeof useNotificationStore>

  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    store = useNotificationStore()
  })

  it('fetchNotifications returns empty array without calling API', async () => {
    await store.fetchNotifications()

    expect(store.notifications).toEqual([])
    expect(store.error).toBeNull()
    expect(store.loading).toBe(false)
    expect(notificationsApi.getNotifications).not.toHaveBeenCalled()
  })

  it('fetchPreferences returns null without calling API', async () => {
    const result = await store.fetchPreferences()

    expect(result).toBeNull()
    expect(store.preferences).toBeNull()
    expect(store.error).toBeNull()
    expect(store.loading).toBe(false)
    expect(notificationsApi.getPreferences).not.toHaveBeenCalled()
  })

  it('markAsRead throws DemoModeError and shows toast', async () => {
    await expect(store.markAsRead('notif-1')).rejects.toThrow('view-only in demo mode')
    expect(toastMocks.info).toHaveBeenCalledWith('This action is view-only in demo mode.')
    expect(notificationsApi.markAsRead).not.toHaveBeenCalled()
  })

  it('updatePreferences throws DemoModeError and shows toast', async () => {
    await expect(
      store.updatePreferences({ emailEnabled: false } as never),
    ).rejects.toThrow('view-only in demo mode')
    expect(toastMocks.info).toHaveBeenCalledWith('This action is view-only in demo mode.')
    expect(notificationsApi.updatePreferences).not.toHaveBeenCalled()
  })
})
