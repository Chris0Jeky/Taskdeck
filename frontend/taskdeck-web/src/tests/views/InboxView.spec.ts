import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import InboxView from '../../views/InboxView.vue'

const routerMocks = vi.hoisted(() => ({
  push: vi.fn(),
  replace: vi.fn(),
}))

const routeMock = vi.hoisted(() => ({
  query: {} as Record<string, unknown>,
  hash: '',
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
  cacheDetail: vi.fn<(detail: {
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
  }, syncSummary?: boolean) => void>(),
  fetchItems: vi.fn<(...args: unknown[]) => Promise<void>>(),
  fetchDetail: vi.fn<(itemId: string, options?: {
    forceRefresh?: boolean
    recordError?: boolean
    showToast?: boolean
    syncSummary?: boolean
  }) => Promise<void>>(),
  peekDetail: vi.fn<(itemId: string, options?: {
    forceRefresh?: boolean
    recordError?: boolean
    showToast?: boolean
    syncSummary?: boolean
  }) => Promise<{
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
  }>>(),
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
    replace: routerMocks.replace,
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
    localStorage.clear()
    escapeHandlers.splice(0, escapeHandlers.length)
    mockCaptureStore.detailById = {}
    mockCaptureStore.loadingList = false
    mockCaptureStore.loadingDetail = false
    mockCaptureStore.actionBusyItemId = null
    mockCaptureStore.listError = null
    mockCaptureStore.detailError = null
    mockCaptureStore.actionError = null
    mockCaptureStore.fetchItems.mockResolvedValue(undefined)
    mockCaptureStore.fetchDetail.mockImplementation(async (itemId: string, options) => {
      const forceRefresh = options?.forceRefresh ?? false
      if (!forceRefresh && mockCaptureStore.detailById[itemId]) {
        return
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
    })
    mockCaptureStore.peekDetail.mockImplementation(async (itemId: string) => (
      mockCaptureStore.detailById[itemId] ?? {
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
    ))
    mockCaptureStore.cacheDetail.mockImplementation((detail, syncSummary = true) => {
      mockCaptureStore.detailById[detail.id] = detail
      if (!syncSummary) {
        return
      }

      const existingIndex = mockCaptureStore.items.findIndex((item) => item.id === detail.id)
      const summary = {
        id: detail.id,
        userId: detail.userId,
        boardId: detail.boardId,
        status: detail.status,
        source: detail.source,
        textExcerpt: detail.textExcerpt,
        createdAt: detail.createdAt,
        processedAt: detail.processedAt,
      }

      if (existingIndex >= 0) {
        mockCaptureStore.items[existingIndex] = summary
        return
      }

      mockCaptureStore.items.unshift(summary)
    })
    mockCaptureStore.ignoreItem.mockResolvedValue(undefined)
    mockCaptureStore.cancelItem.mockResolvedValue(undefined)
    mockCaptureStore.triageItem.mockResolvedValue(undefined)
    routerMocks.push.mockReset()
    routerMocks.replace.mockReset()
    routerMocks.replace.mockResolvedValue(undefined)
    routeMock.query = {}
    routeMock.hash = ''
    seedItems()
  })

  it('loads inbox summaries on mount', async () => {
    mount(InboxView)
    await waitForUi()

    expect(mockCaptureStore.fetchItems).toHaveBeenCalledWith({ limit: 200 })
  })

  it('renders inbox guidance for the capture to review loop', async () => {
    const wrapper = mount(InboxView)
    await waitForUi()

    expect(wrapper.text()).toContain('What is Inbox for?')
    expect(wrapper.text()).toContain('Open Review')
  })

  it('preserves board context when the help callout opens review', async () => {
    routeMock.query = { boardId: 'board-7' }

    const wrapper = mount(InboxView)
    await waitForUi()

    const openReviewButton = wrapper.findAll('button').find((node) => node.text() === 'Open Review')
    await openReviewButton?.trigger('click')

    expect(routerMocks.push).toHaveBeenCalledWith({
      name: 'workspace-review',
      query: { boardId: 'board-7' },
      hash: undefined,
    })
  })

  it('loads board-scoped inbox summaries when the route includes a boardId query', async () => {
    routeMock.query = { boardId: 'board-7' }

    const wrapper = mount(InboxView)
    await waitForUi()

    expect(mockCaptureStore.fetchItems).toHaveBeenCalledWith({ limit: 200, boardId: 'board-7' })
    expect(wrapper.text()).toContain('Showing capture items linked to board board-7.')
  })

  it('auto-opens capture detail when the route hash points at a capture', async () => {
    routeMock.hash = '#capture-capture-2'

    const wrapper = mount(InboxView)
    await waitForUi()

    expect(mockCaptureStore.fetchItems).toHaveBeenCalledWith({ limit: 200 })
    expect(mockCaptureStore.fetchDetail).toHaveBeenCalledWith('capture-2')
    expect(wrapper.text()).toContain('Full text for capture-2')
  })

  it('rejects a hash deep link that is outside the active board-scoped inbox', async () => {
    routeMock.query = { boardId: 'board-7' }
    routeMock.hash = '#capture-capture-999'
    mockCaptureStore.peekDetail.mockResolvedValueOnce({
      id: 'capture-999',
      userId: 'user-1',
      boardId: 'board-9',
      status: 'New',
      source: 'Typed',
      textExcerpt: 'Board scoped excerpt',
      rawText: 'Mismatched board detail',
      createdAt: new Date().toISOString(),
      processedAt: null,
      retryCount: 0,
      provenance: null,
    })

    const wrapper = mount(InboxView)
    await waitForUi()

    expect(mockCaptureStore.fetchItems).toHaveBeenCalledWith({ limit: 200, boardId: 'board-7' })
    expect(mockCaptureStore.peekDetail).toHaveBeenCalledWith('capture-999', {
      forceRefresh: true,
      recordError: false,
      showToast: false,
    })
    expect(mockCaptureStore.fetchDetail).not.toHaveBeenCalled()
    expect(mockCaptureStore.cacheDetail).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('Select an item to inspect the captured text')
    expect(routerMocks.replace).toHaveBeenCalledWith({
      name: 'workspace-inbox',
      query: { boardId: 'board-7' },
    })
  })

  it('opens an older same-board hash deep link even when the item is not in the current list page', async () => {
    routeMock.query = { boardId: 'board-7' }
    routeMock.hash = '#capture-capture-999'
    mockCaptureStore.items = [
      {
        id: 'capture-1',
        userId: 'user-1',
        boardId: 'board-7',
        status: 'New',
        source: 'Typed',
        textExcerpt: 'Board scoped excerpt',
        createdAt: new Date().toISOString(),
        processedAt: null,
      },
    ]
    mockCaptureStore.peekDetail.mockResolvedValueOnce({
      id: 'capture-999',
      userId: 'user-1',
      boardId: 'board-7',
      status: 'Triaging',
      source: 'Typed',
      textExcerpt: 'Older board capture',
      rawText: 'Older board capture detail',
      createdAt: new Date().toISOString(),
      processedAt: null,
      retryCount: 0,
      provenance: null,
    })

    const wrapper = mount(InboxView)
    await waitForUi()

    expect(mockCaptureStore.peekDetail).toHaveBeenCalledWith('capture-999', {
      forceRefresh: true,
      recordError: false,
      showToast: false,
    })
    expect(mockCaptureStore.cacheDetail).toHaveBeenCalledWith(expect.objectContaining({
      id: 'capture-999',
      boardId: 'board-7',
    }), false)
    expect(mockCaptureStore.fetchDetail).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('Older board capture')
    expect(wrapper.text()).toContain('Older board capture detail')
    expect(routerMocks.replace).not.toHaveBeenCalled()
  })

  it('clears a stale capture hash when the board-scoped detail lookup fails', async () => {
    routeMock.query = { boardId: 'board-7' }
    routeMock.hash = '#capture-capture-999'
    mockCaptureStore.peekDetail.mockRejectedValueOnce({
      response: {
        status: 404,
        data: {
          errorCode: 'NotFound',
        },
      },
    })

    const wrapper = mount(InboxView)
    await waitForUi()

    expect(mockCaptureStore.fetchItems).toHaveBeenCalledWith({ limit: 200, boardId: 'board-7' })
    expect(mockCaptureStore.peekDetail).toHaveBeenCalledWith('capture-999', {
      forceRefresh: true,
      recordError: false,
      showToast: false,
    })
    expect(mockCaptureStore.peekDetail).toHaveBeenCalledTimes(1)
    expect(mockCaptureStore.fetchDetail).not.toHaveBeenCalled()
    expect(mockCaptureStore.cacheDetail).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('Select an item to inspect the captured text')
    expect(routerMocks.replace).toHaveBeenCalledWith({
      name: 'workspace-inbox',
      query: { boardId: 'board-7' },
    })
  })

  it('opens board-scoped hash targets with a fresh detail snapshot without overwriting the visible row summary', async () => {
    const createdAt = new Date().toISOString()
    routeMock.query = { boardId: 'board-7' }
    routeMock.hash = '#capture-capture-2'
    mockCaptureStore.items = [
      {
        id: 'capture-2',
        userId: 'user-1',
        boardId: 'board-7',
        status: 'Triaging',
        source: 'Typed',
        textExcerpt: 'Fresh row excerpt',
        createdAt,
        processedAt: createdAt,
      },
    ]
    mockCaptureStore.detailById['capture-2'] = {
      id: 'capture-2',
      userId: 'user-1',
      boardId: 'board-7',
      status: 'New',
      source: 'Typed',
      textExcerpt: 'Stale cached excerpt',
      rawText: 'Stale cached detail',
      createdAt,
      processedAt: null,
      retryCount: 0,
      provenance: null,
    }
    mockCaptureStore.peekDetail.mockResolvedValueOnce({
      id: 'capture-2',
      userId: 'user-1',
      boardId: 'board-7',
      status: 'ProposalCreated',
      source: 'Typed',
      textExcerpt: 'Fresh detail excerpt',
      rawText: 'Fresh detail text',
      createdAt,
      processedAt: createdAt,
      retryCount: 0,
      provenance: null,
    })

    const wrapper = mount(InboxView)
    await waitForUi()

    expect(mockCaptureStore.peekDetail).toHaveBeenCalledWith('capture-2', {
      forceRefresh: true,
      recordError: false,
      showToast: false,
    })
    expect(mockCaptureStore.cacheDetail).toHaveBeenCalledWith(expect.objectContaining({
      id: 'capture-2',
      status: 'ProposalCreated',
      textExcerpt: 'Fresh detail excerpt',
    }), false)
    expect(mockCaptureStore.fetchDetail).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('Fresh row excerpt')
    expect(wrapper.text()).not.toContain('Stale cached excerpt')
    expect(wrapper.text()).toContain('Fresh detail text')
  })

  it('attempts board-scoped hash hydration even when the inbox list fails to load', async () => {
    routeMock.query = { boardId: 'board-7' }
    routeMock.hash = '#capture-capture-2'
    mockCaptureStore.fetchItems.mockRejectedValueOnce(new Error('list failed'))
    mockCaptureStore.peekDetail.mockResolvedValueOnce({
      id: 'capture-2',
      userId: 'user-1',
      boardId: 'board-7',
      status: 'ProposalCreated',
      source: 'Typed',
      textExcerpt: 'Recovered detail excerpt',
      rawText: 'Recovered detail text',
      createdAt: new Date().toISOString(),
      processedAt: null,
      retryCount: 0,
      provenance: null,
    })

    const wrapper = mount(InboxView)
    await waitForUi()

    expect(mockCaptureStore.fetchItems).toHaveBeenCalledWith({ limit: 200, boardId: 'board-7' })
    expect(mockCaptureStore.peekDetail).toHaveBeenCalledWith('capture-2', {
      forceRefresh: true,
      recordError: false,
      showToast: false,
    })
    expect(mockCaptureStore.cacheDetail).toHaveBeenCalledWith(expect.objectContaining({
      id: 'capture-2',
      boardId: 'board-7',
      textExcerpt: 'Recovered detail excerpt',
    }), false)
    expect(wrapper.text()).toContain('Recovered detail text')
    expect(routerMocks.replace).not.toHaveBeenCalled()
  })

  it('preserves board-scoped hashes when detail loading fails transiently', async () => {
    routeMock.query = { boardId: 'board-7' }
    routeMock.hash = '#capture-capture-2'
    mockCaptureStore.detailById['capture-2'] = {
      id: 'capture-2',
      userId: 'user-1',
      boardId: 'board-99',
      status: 'ProposalCreated',
      source: 'Typed',
      textExcerpt: 'Stale cached excerpt',
      rawText: 'Stale cached detail from another board',
      createdAt: new Date().toISOString(),
      processedAt: null,
      retryCount: 0,
      provenance: null,
    }
    mockCaptureStore.peekDetail.mockRejectedValueOnce(new Error('transient lookup failure'))

    const wrapper = mount(InboxView)
    await waitForUi()

    expect(mockCaptureStore.peekDetail).toHaveBeenCalledTimes(1)
    expect(mockCaptureStore.peekDetail).toHaveBeenCalledWith('capture-2', {
      forceRefresh: true,
      recordError: false,
      showToast: false,
    })
    expect(mockCaptureStore.cacheDetail).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('Unable to load capture detail.')
    expect(wrapper.get('[role="alert"]').text()).toContain('Unable to load capture detail.')
    expect(wrapper.text()).not.toContain('Stale cached detail from another board')
    expect(routerMocks.replace).not.toHaveBeenCalled()
  })

  it('clears the capture hash when a hash-opened detail is closed', async () => {
    routeMock.query = { boardId: 'board-7' }
    routeMock.hash = '#capture-capture-2'

    const wrapper = mount(InboxView)
    await waitForUi()

    const closeButton = wrapper.findAll('button').find((node) => node.text() === 'Close (Esc)')
    await closeButton?.trigger('click')
    await waitForUi()

    expect(wrapper.text()).toContain('Select an item to inspect the captured text')
    expect(routerMocks.replace).toHaveBeenCalledWith({
      name: 'workspace-inbox',
      query: { boardId: 'board-7' },
    })
  })

  it('clears stale unscoped capture hashes when detail loading fails', async () => {
    routeMock.query = {}
    routeMock.hash = '#capture-missing-capture'
    mockCaptureStore.fetchDetail.mockRejectedValueOnce(new Error('missing'))

    const wrapper = mount(InboxView)
    await waitForUi()

    expect(mockCaptureStore.fetchDetail).toHaveBeenCalledWith('missing-capture')
    expect(wrapper.text()).toContain('Select an item to inspect the captured text')
    expect(routerMocks.replace).toHaveBeenCalledWith({
      name: 'workspace-inbox',
      query: {},
    })
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

  it('preserves board context when the empty-state review action is used', async () => {
    routeMock.query = { boardId: 'board-7' }
    mockCaptureStore.items = []
    mockCaptureStore.hasItems = false

    const wrapper = mount(InboxView)
    await waitForUi()

    const openReviewButtons = wrapper.findAll('button').filter((node) => node.text() === 'Open Review')
    await openReviewButtons[openReviewButtons.length - 1]?.trigger('click')

    expect(routerMocks.push).toHaveBeenCalledWith({
      name: 'workspace-review',
      query: { boardId: 'board-7' },
      hash: undefined,
    })
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

    expect(mockCaptureStore.fetchDetail).toHaveBeenLastCalledWith('capture-1', { forceRefresh: true })
    expect(wrapper.text()).toContain('Capture Detail')
  })
})
