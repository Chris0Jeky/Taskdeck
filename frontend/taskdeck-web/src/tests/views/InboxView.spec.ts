import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import InboxView from '../../views/InboxView.vue'

const vueHelpers = vi.hoisted(async () => {
  const { computed, ref, shallowRef } = await import('vue')
  return { computed, ref, shallowRef }
})

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
    errorMessage?: string | null
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
  triagePollingItemId: null as string | null,
  pollTriageCompletion: vi.fn<(itemId: string) => () => void>(),
  batchBusy: false,
  batchError: null as string | null,
  batchTriage: vi.fn<(itemIds: string[], action: string) => Promise<{
    total: number
    succeeded: number
    failed: number
    results: Array<{ itemId: string; success: boolean; errorCode?: string | null; errorMessage?: string | null }>
  }>>(),
  updateSuggestion: vi.fn<(itemId: string, dto: { text: string; titleHint?: string | null }) => Promise<{
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
    provenance?: unknown
  }>>(),
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

vi.mock('../../composables/useVirtualList', async () => {
  const { computed, ref, shallowRef } = await vueHelpers
  return {
    useVirtualList: (options: { count: { value: number } | (() => number); estimateSize: number }) => {
      const getCount = typeof options.count === 'function'
        ? options.count
        : () => options.count.value
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
    mockCaptureStore.triagePollingItemId = null
    mockCaptureStore.pollTriageCompletion.mockImplementation(() => () => {})
    mockCaptureStore.batchBusy = false
    mockCaptureStore.batchError = null
    mockCaptureStore.batchTriage.mockResolvedValue({
      total: 0, succeeded: 0, failed: 0, results: [],
    })
    mockCaptureStore.updateSuggestion.mockImplementation(async (itemId: string, dto: { text: string; titleHint?: string | null }) => {
      const detail = {
        id: itemId,
        userId: 'user-1',
        boardId: null,
        status: 'New' as const,
        source: 'Typed' as const,
        textExcerpt: dto.text.slice(0, 200),
        rawText: dto.text,
        createdAt: new Date().toISOString(),
        processedAt: null,
        retryCount: 0,
        provenance: null,
      }
      mockCaptureStore.detailById[itemId] = detail
      return detail
    })
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

    expect(wrapper.text()).toContain('Capture rough notes and turn them into reviewable proposed work.')
    expect(wrapper.text()).toContain('What is Inbox for?')
    expect(wrapper.text()).toContain('then sends it to Review before anything reaches a board')
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
    expect(options[0]?.attributes('tabindex')).toBe('-1')
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
    expect(wrapper.text()).toContain('Capture a note or transcript to get started')
    expect(wrapper.find('button').text()).toContain('New Capture')
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
    expect(mockCaptureStore.pollTriageCompletion).toHaveBeenCalledWith('capture-1')
  })

  it('does not start triage polling when the refreshed detail is already terminal', async () => {
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
    mockCaptureStore.triageItem.mockImplementationOnce(async (itemId: string) => {
      mockCaptureStore.detailById[itemId] = {
        ...mockCaptureStore.detailById[itemId],
        status: 'ProposalCreated',
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
    expect(mockCaptureStore.pollTriageCompletion).not.toHaveBeenCalled()
  })

  it('stops the previous triage poll before retrying triage, even when the retry fails', async () => {
    const stopFirstPoll = vi.fn()
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

    mockCaptureStore.pollTriageCompletion
      .mockImplementationOnce(() => stopFirstPoll)
      .mockImplementation(() => () => {})

    const wrapper = mount(InboxView)
    await waitForUi()

    await wrapper.get('[role="option"]').trigger('click')
    await waitForUi()

    const triageButton = () => wrapper.findAll('button').find((node) => node.text() === 'Start Triage')

    await triageButton()?.trigger('click')
    await waitForUi()

    mockCaptureStore.triageItem.mockRejectedValueOnce(new Error('retry failed'))

    await triageButton()?.trigger('click')
    await waitForUi()

    expect(mockCaptureStore.triageItem).toHaveBeenCalledTimes(2)
    expect(stopFirstPoll).toHaveBeenCalledTimes(1)
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

    expect(wrapper.text()).toContain('A proposed board update is ready for approval.')
    expect(wrapper.text()).toContain('Ready for review')
    const proposalButton = wrapper.findAll('button').find((node) => node.text() === 'Open in Review')
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

    expect(wrapper.find('[data-testid="inbox-detail-loading"]').exists()).toBe(true)
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

  // ── Batch selection tests ──

  it('renders checkboxes for each inbox item', async () => {
    const wrapper = mount(InboxView)
    await waitForUi()

    const checkboxes = wrapper.findAll('[data-testid="inbox-item-checkbox"]')
    expect(checkboxes.length).toBe(2)
  })

  it('shows batch action bar when items are selected', async () => {
    const wrapper = mount(InboxView)
    await waitForUi()

    expect(wrapper.find('[data-testid="batch-action-bar"]').exists()).toBe(false)

    const checkboxes = wrapper.findAll('[data-testid="inbox-item-checkbox"]')
    await checkboxes[0]?.trigger('click')
    await waitForUi()

    expect(wrapper.find('[data-testid="batch-action-bar"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('1 selected')
  })

  it('select-all toggles all items', async () => {
    const wrapper = mount(InboxView)
    await waitForUi()

    const selectAll = wrapper.find('[data-testid="select-all"] input')
    await selectAll.trigger('change')
    await waitForUi()

    expect(wrapper.text()).toContain('2 selected')

    await selectAll.trigger('change')
    await waitForUi()

    expect(wrapper.find('[data-testid="batch-action-bar"]').exists()).toBe(false)
  })

  it('batch triage action calls store with selected ids', async () => {
    mockCaptureStore.batchTriage.mockResolvedValue({
      total: 2, succeeded: 2, failed: 0, results: [
        { itemId: 'capture-1', success: true },
        { itemId: 'capture-2', success: true },
      ],
    })

    const wrapper = mount(InboxView)
    await waitForUi()

    const selectAll = wrapper.find('[data-testid="select-all"] input')
    await selectAll.trigger('change')
    await waitForUi()

    const triageBatchBtn = wrapper.find('[data-testid="batch-action-bar"]')
      .findAll('button').find((b) => b.text().includes('Triage'))
    await triageBatchBtn?.trigger('click')
    await waitForUi()

    expect(mockCaptureStore.batchTriage).toHaveBeenCalledWith(
      expect.arrayContaining(['capture-1', 'capture-2']),
      'triage',
    )
  })

  it('clears selection after successful batch action', async () => {
    mockCaptureStore.batchTriage.mockResolvedValue({
      total: 1, succeeded: 1, failed: 0, results: [
        { itemId: 'capture-1', success: true },
      ],
    })

    const wrapper = mount(InboxView)
    await waitForUi()

    const checkboxes = wrapper.findAll('[data-testid="inbox-item-checkbox"]')
    await checkboxes[0]?.trigger('click')
    await waitForUi()

    const ignoreBatchBtn = wrapper.find('[data-testid="batch-action-bar"]')
      .findAll('button').find((b) => b.text().includes('Ignore'))
    await ignoreBatchBtn?.trigger('click')
    await waitForUi()

    expect(mockCaptureStore.batchTriage).toHaveBeenCalledWith(['capture-1'], 'ignore')
    expect(wrapper.find('[data-testid="batch-action-bar"]').exists()).toBe(false)
  })

  // ── Suggestion editing tests ──

  it('shows edit button for new items in detail view', async () => {
    mockCaptureStore.fetchDetail.mockImplementationOnce(async (itemId: string) => {
      mockCaptureStore.detailById[itemId] = {
        id: itemId,
        userId: 'user-1',
        boardId: null,
        status: 'New',
        source: 'Typed',
        textExcerpt: 'Editable text',
        rawText: 'Full editable text',
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

    const editBtn = wrapper.find('[data-testid="suggestion-edit-btn"]')
    expect(editBtn.exists()).toBe(true)
  })

  it('enters editing mode and saves updated text', async () => {
    mockCaptureStore.fetchDetail.mockImplementationOnce(async (itemId: string) => {
      mockCaptureStore.detailById[itemId] = {
        id: itemId,
        userId: 'user-1',
        boardId: null,
        status: 'New',
        source: 'Typed',
        textExcerpt: 'Editable text',
        rawText: 'Full editable text',
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

    await wrapper.get('[data-testid="suggestion-edit-btn"]').trigger('click')
    await waitForUi()

    const textarea = wrapper.find('[data-testid="suggestion-edit-textarea"]')
    expect(textarea.exists()).toBe(true)
    expect((textarea.element as HTMLTextAreaElement).value).toBe('Full editable text')

    await textarea.setValue('Updated capture text')
    await wrapper.get('[data-testid="suggestion-save-btn"]').trigger('click')
    await waitForUi()

    expect(mockCaptureStore.updateSuggestion).toHaveBeenCalledWith('capture-1', {
      text: 'Updated capture text',
      titleHint: null,
    })
  })

  it('cancels editing without saving', async () => {
    mockCaptureStore.fetchDetail.mockImplementationOnce(async (itemId: string) => {
      mockCaptureStore.detailById[itemId] = {
        id: itemId,
        userId: 'user-1',
        boardId: null,
        status: 'New',
        source: 'Typed',
        textExcerpt: 'Editable text',
        rawText: 'Full editable text',
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

    await wrapper.get('[data-testid="suggestion-edit-btn"]').trigger('click')
    await waitForUi()

    expect(wrapper.find('[data-testid="suggestion-edit-textarea"]').exists()).toBe(true)

    await wrapper.get('[data-testid="suggestion-cancel-btn"]').trigger('click')
    await waitForUi()

    expect(wrapper.find('[data-testid="suggestion-edit-textarea"]').exists()).toBe(false)
    expect(mockCaptureStore.updateSuggestion).not.toHaveBeenCalled()
  })

  describe('triage action visibility', () => {
    it('does not render detail action footer when no item is selected', async () => {
      const wrapper = mount(InboxView)
      await waitForUi()

      expect(wrapper.find('.td-inbox-detail__actions').exists()).toBe(false)
    })

    it('renders detail action footer when an item is selected', async () => {
      const wrapper = mount(InboxView)
      await waitForUi()

      await wrapper.get('[role="option"]').trigger('click')
      await waitForUi()

      expect(wrapper.find('.td-inbox-detail__actions').exists()).toBe(true)
    })

    it('shows Start Triage button for a New item', async () => {
      mockCaptureStore.fetchDetail.mockImplementationOnce(async (itemId: string) => {
        mockCaptureStore.detailById[itemId] = {
          id: itemId,
          userId: 'user-1',
          boardId: null,
          status: 'New',
          source: 'Typed',
          textExcerpt: 'New item excerpt',
          rawText: 'New item full text',
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

      const footer = wrapper.get('.td-inbox-detail__actions')
      expect(footer.text()).toContain('Start Triage')
    })

    it('Start Triage button is enabled for a New item', async () => {
      mockCaptureStore.fetchDetail.mockImplementationOnce(async (itemId: string) => {
        mockCaptureStore.detailById[itemId] = {
          id: itemId,
          userId: 'user-1',
          boardId: null,
          status: 'New',
          source: 'Typed',
          textExcerpt: 'New item excerpt',
          rawText: 'New item full text',
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

      const footer = wrapper.get('.td-inbox-detail__actions')
      const triageBtn = footer.findAll('button').find((b) => b.text() === 'Start Triage')
      expect(triageBtn).toBeDefined()
      expect(triageBtn!.attributes('disabled')).toBeUndefined()
    })

    it('Ignore and Cancel buttons are enabled for a New item', async () => {
      mockCaptureStore.fetchDetail.mockImplementationOnce(async (itemId: string) => {
        mockCaptureStore.detailById[itemId] = {
          id: itemId,
          userId: 'user-1',
          boardId: null,
          status: 'New',
          source: 'Typed',
          textExcerpt: 'New item excerpt',
          rawText: 'New item full text',
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

      const footer = wrapper.get('.td-inbox-detail__actions')
      const ignoreBtn = footer.findAll('button').find((b) => b.text() === 'Ignore')
      const cancelBtn = footer.findAll('button').find((b) => b.text() === 'Cancel')
      expect(ignoreBtn).toBeDefined()
      expect(ignoreBtn!.attributes('disabled')).toBeUndefined()
      expect(cancelBtn).toBeDefined()
      expect(cancelBtn!.attributes('disabled')).toBeUndefined()
    })

    it('shows Triage Complete label and disables triage button for ProposalCreated status', async () => {
      mockCaptureStore.fetchDetail.mockImplementationOnce(async (itemId: string) => {
        mockCaptureStore.detailById[itemId] = {
          id: itemId,
          userId: 'user-1',
          boardId: null,
          status: 'ProposalCreated',
          source: 'Typed',
          textExcerpt: 'Ready item excerpt',
          rawText: 'Ready item full text',
          createdAt: new Date().toISOString(),
          processedAt: new Date().toISOString(),
          retryCount: 0,
          provenance: null,
        }
      })

      const wrapper = mount(InboxView)
      await waitForUi()

      await wrapper.get('[role="option"]').trigger('click')
      await waitForUi()

      const footer = wrapper.get('.td-inbox-detail__actions')
      const triageBtn = footer.findAll('button').find((b) => b.text() === 'Triage Complete')
      expect(triageBtn).toBeDefined()
      expect(triageBtn!.attributes('disabled')).toBeDefined()
    })

    it('disables Ignore and Cancel for a ProposalCreated item', async () => {
      mockCaptureStore.fetchDetail.mockImplementationOnce(async (itemId: string) => {
        mockCaptureStore.detailById[itemId] = {
          id: itemId,
          userId: 'user-1',
          boardId: null,
          status: 'ProposalCreated',
          source: 'Typed',
          textExcerpt: 'Ready item excerpt',
          rawText: 'Ready item full text',
          createdAt: new Date().toISOString(),
          processedAt: new Date().toISOString(),
          retryCount: 0,
          provenance: null,
        }
      })

      const wrapper = mount(InboxView)
      await waitForUi()

      await wrapper.get('[role="option"]').trigger('click')
      await waitForUi()

      const footer = wrapper.get('.td-inbox-detail__actions')
      const ignoreBtn = footer.findAll('button').find((b) => b.text() === 'Ignore')
      const cancelBtn = footer.findAll('button').find((b) => b.text() === 'Cancel')
      expect(ignoreBtn!.attributes('disabled')).toBeDefined()
      expect(cancelBtn!.attributes('disabled')).toBeDefined()
    })

    it('shows Converted label and disables triage for a Converted item', async () => {
      mockCaptureStore.fetchDetail.mockImplementationOnce(async (itemId: string) => {
        mockCaptureStore.detailById[itemId] = {
          id: itemId,
          userId: 'user-1',
          boardId: null,
          status: 'Converted',
          source: 'Typed',
          textExcerpt: 'Converted item excerpt',
          rawText: 'Converted item full text',
          createdAt: new Date().toISOString(),
          processedAt: new Date().toISOString(),
          retryCount: 0,
          provenance: null,
        }
      })

      const wrapper = mount(InboxView)
      await waitForUi()

      await wrapper.get('[role="option"]').trigger('click')
      await waitForUi()

      const footer = wrapper.get('.td-inbox-detail__actions')
      const triageBtn = footer.findAll('button').find((b) => b.text() === 'Converted')
      expect(triageBtn).toBeDefined()
      expect(triageBtn!.attributes('disabled')).toBeDefined()
    })

    it('shows Retry Triage and enables it for a Failed item', async () => {
      mockCaptureStore.fetchDetail.mockImplementationOnce(async (itemId: string) => {
        mockCaptureStore.detailById[itemId] = {
          id: itemId,
          userId: 'user-1',
          boardId: null,
          status: 'Failed',
          source: 'Typed',
          textExcerpt: 'Failed item excerpt',
          rawText: 'Failed item full text',
          createdAt: new Date().toISOString(),
          processedAt: null,
          retryCount: 1,
          errorMessage: 'LLM timeout',
          provenance: null,
        }
      })

      const wrapper = mount(InboxView)
      await waitForUi()

      await wrapper.get('[role="option"]').trigger('click')
      await waitForUi()

      const footer = wrapper.get('.td-inbox-detail__actions')
      const retryBtn = footer.findAll('button').find((b) => b.text() === 'Retry Triage')
      expect(retryBtn).toBeDefined()
      expect(retryBtn!.attributes('disabled')).toBeUndefined()
    })

    it('footer remains present after selecting a different item', async () => {
      const createdAt = new Date().toISOString()
      mockCaptureStore.fetchDetail.mockImplementation(async (itemId: string) => {
        mockCaptureStore.detailById[itemId] = {
          id: itemId,
          userId: 'user-1',
          boardId: null,
          status: 'New',
          source: 'Typed',
          textExcerpt: `Excerpt for ${itemId}`,
          rawText: `Full text for ${itemId}`,
          createdAt,
          processedAt: null,
          retryCount: 0,
          provenance: null,
        }
      })

      const wrapper = mount(InboxView)
      await waitForUi()

      const options = wrapper.findAll('[role="option"]')
      await options[0]!.trigger('click')
      await waitForUi()

      expect(wrapper.find('.td-inbox-detail__actions').exists()).toBe(true)

      await options[1]!.trigger('click')
      await waitForUi()

      expect(wrapper.find('.td-inbox-detail__actions').exists()).toBe(true)
    })

    it('footer disappears when detail panel is closed', async () => {
      const wrapper = mount(InboxView)
      await waitForUi()

      await wrapper.get('[role="option"]').trigger('click')
      await waitForUi()

      expect(wrapper.find('.td-inbox-detail__actions').exists()).toBe(true)

      const closeBtn = wrapper.findAll('button').find((b) => b.text().includes('Close'))
      await closeBtn?.trigger('click')
      await waitForUi()

      expect(wrapper.find('.td-inbox-detail__actions').exists()).toBe(false)
    })

    it('all four footer action buttons are present when an item is selected', async () => {
      mockCaptureStore.fetchDetail.mockImplementationOnce(async (itemId: string) => {
        mockCaptureStore.detailById[itemId] = {
          id: itemId,
          userId: 'user-1',
          boardId: null,
          status: 'New',
          source: 'Typed',
          textExcerpt: 'New item excerpt',
          rawText: 'New item full text',
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

      const footer = wrapper.get('.td-inbox-detail__actions')
      const footerButtons = footer.findAll('button')
      const buttonTexts = footerButtons.map((b) => b.text())
      // Verify all required footer actions are present: Refresh Detail, triage action, Ignore, Cancel
      expect(buttonTexts.some((t) => t.includes('Refresh Detail') || t.includes('Refreshing'))).toBe(true)
      expect(buttonTexts.some((t) => t.includes('Start Triage') || t.includes('Triage') || t.includes('Converted'))).toBe(true)
      expect(buttonTexts.some((t) => t.includes('Ignore'))).toBe(true)
      expect(buttonTexts.some((t) => t.includes('Cancel'))).toBe(true)
    })
  })

  describe('bulk action visibility', () => {
    it('batch action bar is absent when no checkboxes are ticked', async () => {
      const wrapper = mount(InboxView)
      await waitForUi()

      expect(wrapper.find('[data-testid="batch-action-bar"]').exists()).toBe(false)
    })

    it('batch action bar appears after checking one item', async () => {
      const wrapper = mount(InboxView)
      await waitForUi()

      const checkbox = wrapper.get('[data-testid="inbox-item-checkbox"]')
      await checkbox.trigger('click')
      await waitForUi()

      expect(wrapper.find('[data-testid="batch-action-bar"]').exists()).toBe(true)
    })

    it('batch action bar shows Triage, Ignore, Cancel and Clear buttons', async () => {
      const wrapper = mount(InboxView)
      await waitForUi()

      await wrapper.get('[data-testid="inbox-item-checkbox"]').trigger('click')
      await waitForUi()

      const bar = wrapper.get('[data-testid="batch-action-bar"]')
      const labels = bar.findAll('button').map((b) => b.text())
      expect(labels.some((l) => l.includes('Triage'))).toBe(true)
      expect(labels.some((l) => l.includes('Ignore'))).toBe(true)
      expect(labels.some((l) => l.includes('Cancel'))).toBe(true)
      expect(labels.some((l) => l === 'Clear')).toBe(true)
    })

    it('batch action bar shows correct count when two items are checked', async () => {
      const wrapper = mount(InboxView)
      await waitForUi()

      const checkboxes = wrapper.findAll('[data-testid="inbox-item-checkbox"]')
      await checkboxes[0]!.trigger('click')
      await waitForUi()
      await checkboxes[1]!.trigger('click')
      await waitForUi()

      const bar = wrapper.get('[data-testid="batch-action-bar"]')
      expect(bar.text()).toContain('Triage (2)')
      expect(bar.text()).toContain('Ignore (2)')
      expect(wrapper.text()).toContain('2 selected')
    })

    it('batch action bar disappears after Clear is clicked', async () => {
      const wrapper = mount(InboxView)
      await waitForUi()

      await wrapper.get('[data-testid="inbox-item-checkbox"]').trigger('click')
      await waitForUi()

      expect(wrapper.find('[data-testid="batch-action-bar"]').exists()).toBe(true)

      const clearBtn = wrapper.find('[data-testid="batch-action-bar"]')
        .findAll('button').find((b) => b.text() === 'Clear')
      await clearBtn?.trigger('click')
      await waitForUi()

      expect(wrapper.find('[data-testid="batch-action-bar"]').exists()).toBe(false)
    })

    it('select-all checkbox shows indeterminate state when only one of two items is checked', async () => {
      const wrapper = mount(InboxView)
      await waitForUi()

      const checkboxes = wrapper.findAll('[data-testid="inbox-item-checkbox"]')
      await checkboxes[0]!.trigger('click')
      await waitForUi()

      const selectAll = wrapper.get('[data-testid="select-all"] input')
      expect((selectAll.element as HTMLInputElement).indeterminate).toBe(true)
    })

    it('select-all selects all items and shows batch bar with full count', async () => {
      const wrapper = mount(InboxView)
      await waitForUi()

      await wrapper.get('[data-testid="select-all"] input').trigger('change')
      await waitForUi()

      expect(wrapper.find('[data-testid="batch-action-bar"]').exists()).toBe(true)
      expect(wrapper.text()).toContain('2 selected')
    })

    it('batch action buttons are disabled while batch operation is in progress', async () => {
      const wrapper = mount(InboxView)
      await waitForUi()

      await wrapper.get('[data-testid="inbox-item-checkbox"]').trigger('click')
      await waitForUi()

      mockCaptureStore.batchBusy = true
      await waitForUi()

      const bar = wrapper.get('[data-testid="batch-action-bar"]')
      // Triage, Ignore, and Cancel are disabled when batchBusy; Clear remains enabled
      const disabledButtons = bar.findAll('button[disabled]')
      expect(disabledButtons).toHaveLength(3)
      const clearBtn = bar.findAll('button').find((b) => b.text() === 'Clear')
      expect(clearBtn!.attributes('disabled')).toBeUndefined()
    })

    it('empty inbox does not render select-all checkbox or batch bar', async () => {
      mockCaptureStore.items = []
      mockCaptureStore.hasItems = false

      const wrapper = mount(InboxView)
      await waitForUi()

      expect(wrapper.find('[data-testid="select-all"]').exists()).toBe(false)
      expect(wrapper.find('[data-testid="batch-action-bar"]').exists()).toBe(false)
    })
  })

  describe('premium primitive states', () => {
    it('shows skeleton loading rows when list is loading', async () => {
      mockCaptureStore.loadingList = true

      const wrapper = mount(InboxView)
      await waitForUi()

      expect(wrapper.find('[data-testid="inbox-loading-skeleton"]').exists()).toBe(true)
      // Should render 5 skeleton rows
      expect(wrapper.findAll('.td-inbox__skeleton-row')).toHaveLength(5)
    })

    it('shows TdInlineAlert with retry button on list error', async () => {
      mockCaptureStore.loadingList = false
      mockCaptureStore.listError = 'Network error'
      mockCaptureStore.items = []
      mockCaptureStore.hasItems = false

      const wrapper = mount(InboxView)
      await waitForUi()

      expect(wrapper.find('[data-testid="inbox-list-error"]').exists()).toBe(true)
      expect(wrapper.text()).toContain('Network error')
      expect(wrapper.find('[data-testid="inbox-retry-btn"]').exists()).toBe(true)
    })

    it('shows TdEmptyState when inbox has no items', async () => {
      mockCaptureStore.loadingList = false
      mockCaptureStore.listError = null
      mockCaptureStore.items = []
      mockCaptureStore.hasItems = false

      const wrapper = mount(InboxView)
      await waitForUi()

      expect(wrapper.find('[data-testid="inbox-empty-state"]').exists()).toBe(true)
      expect(wrapper.text()).toContain('No capture items yet')
    })

    it('shows no-selection empty state in detail panel when no item selected', async () => {
      const wrapper = mount(InboxView)
      await waitForUi()

      expect(wrapper.find('[data-testid="inbox-detail-placeholder"]').exists()).toBe(true)
      expect(wrapper.text()).toContain('No item selected')
    })

    it('shows skeleton loading in detail panel while fetching detail', async () => {
      mockCaptureStore.fetchDetail.mockImplementationOnce(async () => {
        mockCaptureStore.loadingDetail = true
      })

      const wrapper = mount(InboxView)
      await waitForUi()

      await wrapper.get('[role="option"]').trigger('click')
      await waitForUi()

      expect(wrapper.find('[data-testid="inbox-detail-loading"]').exists()).toBe(true)
    })

    it('shows proposal link with success alert for items with proposal', async () => {
      mockCaptureStore.fetchDetail.mockImplementationOnce(async (itemId: string) => {
        mockCaptureStore.detailById[itemId] = {
          id: itemId,
          userId: 'user-1',
          boardId: null,
          status: 'ProposalCreated',
          source: 'Typed',
          textExcerpt: 'Some excerpt',
          rawText: 'Full text',
          createdAt: new Date().toISOString(),
          processedAt: new Date().toISOString(),
          retryCount: 0,
          provenance: {
            captureItemId: itemId,
            triageRunId: 'triage-1',
            proposalId: 'proposal-123',
            promptVersion: null,
          },
        }
      })

      const wrapper = mount(InboxView)
      await waitForUi()

      await wrapper.get('[role="option"]').trigger('click')
      await waitForUi()

      expect(wrapper.find('[data-testid="inbox-proposal-link"]').exists()).toBe(true)
      expect(wrapper.text()).toContain('proposed board update is ready')
    })

    it('uses TdBadge for status badges in list rows', async () => {
      const wrapper = mount(InboxView)
      await waitForUi()

      // TdBadge renders with td-badge class
      const badges = wrapper.findAll('.td-badge')
      // Each row should have 2 badges (status + source), so at least 4 for 2 items
      expect(badges.length).toBeGreaterThanOrEqual(4)
    })
  })
})
