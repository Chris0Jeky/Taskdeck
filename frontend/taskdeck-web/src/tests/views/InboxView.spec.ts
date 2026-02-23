import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import InboxView from '../../views/InboxView.vue'

const escapeHandlers: Array<() => void> = []

const mockCaptureStore = reactive({
  items: [] as Array<{
    id: string
    userId: string
    boardId: string | null
    status: string | number
    source: string | number
    textExcerpt: string
    createdAt: string
    processedAt: string | null
  }>,
  detailById: {} as Record<string, {
    id: string
    userId: string
    boardId: string | null
    status: string | number
    source: string | number
    textExcerpt: string
    rawText: string
    createdAt: string
    processedAt: string | null
    retryCount: number
  }>,
  loadingList: false,
  loadingDetail: false,
  actionBusyItemId: null as string | null,
  error: null as string | null,
  hasItems: true,
  fetchItems: vi.fn<(...args: unknown[]) => Promise<void>>(),
  fetchDetail: vi.fn<(itemId: string, forceRefresh?: boolean) => Promise<void>>(),
  ignoreItem: vi.fn<(itemId: string) => Promise<void>>(),
  cancelItem: vi.fn<(itemId: string) => Promise<void>>(),
})

vi.mock('../../store/captureStore', () => ({
  useCaptureStore: () => mockCaptureStore,
}))

vi.mock('../../composables/useEscapeStack', () => ({
  registerEscapeHandler: vi.fn((handler: () => void) => {
    escapeHandlers.push(handler)
    return () => {
      const index = escapeHandlers.indexOf(handler)
      if (index >= 0) {
        escapeHandlers.splice(index, 1)
      }
    }
  }),
}))

async function waitForUi() {
  await Promise.resolve()
  await Promise.resolve()
  await Promise.resolve()
}

function seedItems() {
  const createdAt = new Date().toISOString()
  mockCaptureStore.items = [
    {
      id: 'capture-1',
      userId: 'user-1',
      boardId: null,
      status: 'New',
      source: 'Typed',
      textExcerpt: 'First excerpt',
      createdAt,
      processedAt: null,
    },
    {
      id: 'capture-2',
      userId: 'user-1',
      boardId: null,
      status: 'Triaging',
      source: 'Paste',
      textExcerpt: 'Second excerpt',
      createdAt,
      processedAt: null,
    },
  ]
  mockCaptureStore.hasItems = true
}

describe('InboxView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    escapeHandlers.splice(0, escapeHandlers.length)
    mockCaptureStore.detailById = {}
    mockCaptureStore.loadingList = false
    mockCaptureStore.loadingDetail = false
    mockCaptureStore.actionBusyItemId = null
    mockCaptureStore.error = null
    mockCaptureStore.fetchItems.mockResolvedValue(undefined)
    mockCaptureStore.fetchDetail.mockImplementation(async (itemId: string) => {
      mockCaptureStore.detailById[itemId] = {
        id: itemId,
        userId: 'user-1',
        boardId: null,
        status: 'Triaging',
        source: 'Typed',
        textExcerpt: `Excerpt for ${itemId}`,
        rawText: `Full text for ${itemId}`,
        createdAt: new Date().toISOString(),
        processedAt: null,
        retryCount: 0,
      }
    })
    mockCaptureStore.ignoreItem.mockResolvedValue(undefined)
    mockCaptureStore.cancelItem.mockResolvedValue(undefined)
    seedItems()
  })

  it('loads inbox summaries on mount', async () => {
    mount(InboxView)
    await waitForUi()

    expect(mockCaptureStore.fetchItems).toHaveBeenCalledWith({ limit: 200 })
  })

  it('does not load full detail until an item is opened', async () => {
    const wrapper = mount(InboxView)
    await waitForUi()

    expect(mockCaptureStore.fetchDetail).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('Select an item to view full text')

    const firstRow = wrapper.get('[role="option"]')
    await firstRow.trigger('click')
    await waitForUi()

    expect(mockCaptureStore.fetchDetail).toHaveBeenCalledWith('capture-1')
    expect(wrapper.text()).toContain('Full text for capture-1')
  })

  it('supports keyboard navigation and enter-to-open', async () => {
    const wrapper = mount(InboxView)
    await waitForUi()

    const listbox = wrapper.get('[role="listbox"]')
    await listbox.trigger('keydown', { key: 'ArrowDown' })
    await listbox.trigger('keydown', { key: 'Enter' })
    await waitForUi()

    expect(mockCaptureStore.fetchDetail).toHaveBeenCalledWith('capture-2')
    expect(wrapper.text()).toContain('Full text for capture-2')
  })

  it('wraps selection from first to last item on ArrowUp', async () => {
    const wrapper = mount(InboxView)
    await waitForUi()

    const listbox = wrapper.get('[role="listbox"]')
    await listbox.trigger('keydown', { key: 'ArrowUp' })
    await listbox.trigger('keydown', { key: 'Enter' })
    await waitForUi()

    expect(mockCaptureStore.fetchDetail).toHaveBeenCalledWith('capture-2')
    expect(wrapper.text()).toContain('Full text for capture-2')
  })

  it('closes detail with escape handler', async () => {
    const wrapper = mount(InboxView)
    await waitForUi()

    await wrapper.get('[role="option"]').trigger('click')
    await waitForUi()
    expect(wrapper.text()).toContain('Full text for capture-1')

    expect(escapeHandlers.length).toBeGreaterThan(0)
    escapeHandlers[escapeHandlers.length - 1]()
    await waitForUi()

    expect(wrapper.text()).toContain('Select an item to view full text')
  })

  it('disables ignore and cancel for non-cancellable statuses', async () => {
    const wrapper = mount(InboxView)
    await waitForUi()

    await wrapper.get('[role="option"]').trigger('click')
    await waitForUi()

    const ignoreButton = wrapper.get('button.td-btn--danger')
    const cancelButton = wrapper.findAll('button.td-btn--secondary').find((node) => node.text() === 'Cancel')

    expect(ignoreButton.attributes('disabled')).toBeDefined()
    expect(cancelButton?.attributes('disabled')).toBeDefined()
  })
})
