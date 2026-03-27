import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'

vi.mock('../../utils/demoMode', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../utils/demoMode')>()
  return {
    ...actual,
    isDemoMode: true,
  }
})

vi.mock('../../api/captureApi', () => ({
  captureApi: {
    createItem: vi.fn(),
    listItems: vi.fn(),
    getItem: vi.fn(),
    ignoreItem: vi.fn(),
    cancelItem: vi.fn(),
    enqueueTriage: vi.fn(),
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

import { useCaptureStore } from '../../store/captureStore'
import { captureApi } from '../../api/captureApi'

describe('captureStore demo mode', () => {
  let store: ReturnType<typeof useCaptureStore>

  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    store = useCaptureStore()
  })

  it('fetchItems returns demo capture items without calling API', async () => {
    await store.fetchItems()

    expect(store.items.length).toBeGreaterThan(0)
    expect(store.items[0].source).toBe('Typed')
    expect(captureApi.listItems).not.toHaveBeenCalled()
  })

  it('createItem throws DemoModeError and shows toast', async () => {
    await expect(
      store.createItem({ boardId: null, text: 'test', source: 'Typed' }),
    ).rejects.toThrow('view-only in demo mode')
    expect(toastMocks.info).toHaveBeenCalledWith('This action is view-only in demo mode.')
    expect(captureApi.createItem).not.toHaveBeenCalled()
  })

  it('triageItem throws DemoModeError in demo mode', async () => {
    await expect(store.triageItem('demo-cap-1')).rejects.toThrow('view-only in demo mode')
    expect(captureApi.enqueueTriage).not.toHaveBeenCalled()
  })
})
