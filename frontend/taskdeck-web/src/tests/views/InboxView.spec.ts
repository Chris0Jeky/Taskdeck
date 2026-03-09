import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import InboxView from '../../views/InboxView.vue'

const routerMocks = vi.hoisted(() => ({
  push: vi.fn(),
}))

const routeMock = vi.hoisted(() => ({
  query: {} as Record<string, unknown>,
}))

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
    provenance?: {
      captureItemId: string
      triageRunId: string | null
      proposalId: string | null
      promptVersion: string | null
    } | null
  }>,
  loadingList: false,
  loadingDetail: false,
  actionBusyItemId: null as string | null,
  listError: null as string | null,
  detailError: null as string | null,
  actionError: null as string | null,
  hasItems: true,
  fetchItems: vi.fn<(...args: unknown[]) => Promise<void>>(),
  fetchDetail: vi.fn<(itemId: string, forceRefresh?: boolean) => Promise<void>>(),
  ignoreItem: vi.fn<(itemId: string) => Promise<void>>(),
  cancelItem: vi.fn<(itemId: string) => Promise<void>>(),
  triageItem: vi.fn<(itemId: string) => Promise<void>>(),
})

vi.mock('../../store/captureStore', () => ({
  useCaptureStore: () => mockCaptureStore,
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({
    push: routerMocks.push,
  }),
  useRoute: () => routeMock,
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
    mockCaptureStore.listError = null
    mockCaptureStore.detailError = null
    mockCaptureStore.actionError = null
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
        provenance: null,
      }
    })
    mockCaptureStore.ignoreItem.mockResolvedValue(undefined)
    mockCaptureStore.cancelItem.mockResolvedValue(undefined)
    mockCaptureStore.triageItem.mockResolvedValue(undefined)
    routerMocks.push.mockReset()
    routeMock.query = {}
    seedItems()
  })

  it('loads inbox summaries on mount', async () => {
    mount(InboxView)
    await waitForUi()

    expect(mockCaptureStore.fetchItems).toHaveBeenCalledWith({ limit: 200 })
  })

  it('loads board-scoped inbox summaries when the route includes a boardId query', async () => {
    routeMock.query = { boardId: 'board-7' }

    const wrapper = mount(InboxView)
    await waitForUi()

    expect(mockCaptureStore.fetchItems).toHaveBeenCalledWith({ limit: 200, boardId: 'board-7' })
    expect(wrapper.text()).toContain('Showing capture items linked to board board-7.')
  })

  it('swallows fetchItems errors on mount', async () => {
    mockCaptureStore.fetchItems.mockRejectedValueOnce(new Error('load failed'))
    const wrapper = mount(InboxView)
    await waitForUi()

    expect(mockCaptureStore.fetchItems).toHaveBeenCalledWith({ limit: 200 })
    expect(wrapper.exists()).toBe(true)
  })

  it('does not load full detail until an item is opened', async () => {
    const wrapper = mount(InboxView)
    await waitForUi()

    expect(mockCaptureStore.fetchDetail).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('Select an item to inspect the captured text')

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

  it('keeps listbox accessibility state in sync with active selection', async () => {
    const wrapper = mount(InboxView)
    await waitForUi()

    const listbox = wrapper.get('[role="listbox"]')
    expect(listbox.attributes('aria-activedescendant')).toBe('td-inbox-option-0')

    await listbox.trigger('keydown', { key: 'ArrowDown' })
    await waitForUi()

    expect(listbox.attributes('aria-activedescendant')).toBe('td-inbox-option-1')
    const options = wrapper.findAll('[role="option"]')
    expect(options[0]?.attributes('tabindex')).toBeUndefined()
    expect(options[1]?.attributes('id')).toBe('td-inbox-option-1')
  })

  it('updates active descendant when an item is clicked', async () => {
    const wrapper = mount(InboxView)
    await waitForUi()

    const options = wrapper.findAll('[role="option"]')
    await options[1]?.trigger('click')
    await waitForUi()

    const listbox = wrapper.get('[role="listbox"]')
    expect(listbox.attributes('aria-activedescendant')).toBe('td-inbox-option-1')
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

    expect(wrapper.text()).toContain('Select an item to inspect the captured text')
  })

  it('shows guided empty-state actions when there are no capture items', async () => {
    mockCaptureStore.items = []
    mockCaptureStore.hasItems = false

    const wrapper = mount(InboxView)
    await waitForUi()

    expect(wrapper.text()).toContain('No capture items yet')
    expect(wrapper.text()).toContain('Start from Home')
    expect(wrapper.find('button').text()).toContain('Refresh')
    expect(wrapper.findAll('button').some((node) => node.text() === 'Open Home')).toBe(true)
    expect(wrapper.findAll('button').some((node) => node.text() === 'Open Review')).toBe(true)
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

  it('enqueues triage from detail actions when selection is triageable', async () => {
    mockCaptureStore.fetchDetail.mockImplementationOnce(async (itemId: string) => {
      mockCaptureStore.detailById[itemId] = {
        id: itemId,
        userId: 'user-1',
        boardId: 'board-1',
        status: 'New',
        source: 'Typed',
        textExcerpt: `Excerpt for ${itemId}`,
        rawText: `Full text for ${itemId}`,
        createdAt: new Date().toISOString(),
        processedAt: null,
        retryCount: 0,
        provenance: null,
      }
    })

    const wrapper = mount(InboxView)
    await waitForUi()

    await wrapper.get('[role="option"]').trigger('click')
    await waitForUi()

    const triageButton = wrapper.findAll('button').find((node) => node.text() === 'Start Triage')
    await triageButton?.trigger('click')
    await waitForUi()

    expect(mockCaptureStore.triageItem).toHaveBeenCalledWith('capture-1')
  })

  it('navigates to linked proposal route when detail includes proposal provenance', async () => {
    mockCaptureStore.fetchDetail.mockImplementationOnce(async (itemId: string) => {
      mockCaptureStore.detailById[itemId] = {
        id: itemId,
        userId: 'user-1',
        boardId: 'board-1',
        status: 'ProposalCreated',
        source: 'Typed',
        textExcerpt: `Excerpt for ${itemId}`,
        rawText: `Full text for ${itemId}`,
        createdAt: new Date().toISOString(),
        processedAt: new Date().toISOString(),
        retryCount: 0,
        provenance: {
          captureItemId: itemId,
          triageRunId: 'triage-1',
          proposalId: 'proposal-42',
          promptVersion: 'triage.v1',
        },
      }
    })

    const wrapper = mount(InboxView)
    await waitForUi()

    await wrapper.get('[role="option"]').trigger('click')
    await waitForUi()

    const proposalButton = wrapper.findAll('button').find((node) => node.text() === 'Open Proposal')
    expect(proposalButton?.exists()).toBe(true)

    await proposalButton?.trigger('click')

    expect(routerMocks.push).toHaveBeenCalledWith({
      name: 'workspace-review',
      query: { boardId: 'board-1' },
      hash: '#proposal-proposal-42',
    })
  })

  it('clears selection when opening detail fails', async () => {
    mockCaptureStore.fetchDetail.mockRejectedValueOnce(new Error('detail failed'))
    const wrapper = mount(InboxView)
    await waitForUi()

    await wrapper.get('[role="option"]').trigger('click')
    await waitForUi()

    expect(wrapper.text()).toContain('Select an item to inspect the captured text')
  })

  it('does not clear a newer selection when stale detail request fails', async () => {
    let rejectFirst: ((error: unknown) => void) | null = null
    mockCaptureStore.fetchDetail.mockImplementation((itemId: string) => {
      if (itemId === 'capture-1') {
        return new Promise((_, reject) => {
          rejectFirst = reject
        })
      }

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
        provenance: null,
      }
      return Promise.resolve()
    })

    const wrapper = mount(InboxView)
    await waitForUi()

    const options = wrapper.findAll('[role="option"]')
    await options[0]?.trigger('click')
    await options[1]?.trigger('click')
    await waitForUi()

    rejectFirst?.(new Error('stale request failure'))
    await waitForUi()

    expect(wrapper.text()).toContain('Full text for capture-2')
    expect(wrapper.text()).not.toContain('Select an item to inspect the captured text')
  })

  it('shows loading placeholder while selected detail is still loading', async () => {
    mockCaptureStore.fetchDetail.mockImplementationOnce(async () => {
      mockCaptureStore.loadingDetail = true
    })

    const wrapper = mount(InboxView)
    await waitForUi()

    await wrapper.get('[role="option"]').trigger('click')
    await waitForUi()

    expect(wrapper.text()).toContain('Loading detail...')
  })

  it('refresh detail action swallows fetch errors', async () => {
    const wrapper = mount(InboxView)
    await waitForUi()

    await wrapper.get('[role="option"]').trigger('click')
    await waitForUi()
    mockCaptureStore.fetchDetail.mockRejectedValueOnce(new Error('refresh failed'))

    const refreshButton = wrapper.findAll('button.td-btn--secondary')
      .find((node) => node.text() === 'Refresh Detail')
    await refreshButton?.trigger('click')
    await waitForUi()

    expect(mockCaptureStore.fetchDetail).toHaveBeenCalledWith('capture-1', true)
    expect(wrapper.text()).toContain('Capture Detail')
  })
})
