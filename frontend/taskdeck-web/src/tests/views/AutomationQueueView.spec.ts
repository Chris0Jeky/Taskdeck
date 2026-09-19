import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { reactive } from 'vue'
import { BOARD_REQUEST_TIMEOUT_MS } from '../../api/http'
import AutomationQueueView from '../../views/AutomationQueueView.vue'

const routerMocks = vi.hoisted(() => ({
  push: vi.fn(),
}))

const mockQueueStore = reactive({
  stats: {
    pendingCount: 1,
    processingCount: 0,
    completedCount: 0,
    failedCount: 0,
    cancelledCount: 0,
  },
  loading: false,
  requests: [] as Array<{
    id: string
    requestType: string
    status: string | number
    createdAt: string
    processedAt: string | null
    errorMessage: string | null
  }>,
  fetchByStatus: vi.fn<(status: string) => Promise<void>>(),
  fetchStats: vi.fn<() => Promise<void>>(),
  submitRequest: vi.fn<(payload: Record<string, unknown>) => Promise<void>>(),
  cancelRequest: vi.fn<(requestId: string) => Promise<void>>(),
  processNext: vi.fn<() => Promise<void>>(),
})

const toastMocks = vi.hoisted(() => ({
  error: vi.fn(),
  success: vi.fn(),
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({
    push: routerMocks.push,
  }),
}))

vi.mock('../../store/queueStore', () => ({
  useQueueStore: () => mockQueueStore,
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => ({
    error: toastMocks.error,
    success: toastMocks.success,
  }),
}))

const boardsMocks = vi.hoisted(() => ({
  getBoards: vi.fn(),
}))

vi.mock('../../api/boardsApi', () => ({
  boardsApi: {
    getBoards: boardsMocks.getBoards,
  },
}))

async function waitForUi() {
  await Promise.resolve()
  await Promise.resolve()
  await Promise.resolve()
}

async function openComposer(wrapper: ReturnType<typeof mount>) {
  const toggle = wrapper.findAll('button').find((button) => button.text().includes('+ New Request'))
  if (!toggle) {
    throw new Error('Expected composer toggle button')
  }

  await toggle.trigger('click')
  await wrapper.vm.$nextTick()
}

describe('AutomationQueueView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    boardsMocks.getBoards.mockResolvedValue([
      { id: '123e4567-e89b-12d3-a456-426614174000', name: 'Engineering Sprint' },
      { id: 'board-42', name: 'Roadmap Board' },
    ])
    mockQueueStore.loading = false
    mockQueueStore.requests = []
    mockQueueStore.fetchByStatus.mockResolvedValue(undefined)
    mockQueueStore.fetchStats.mockResolvedValue(undefined)
    mockQueueStore.submitRequest.mockResolvedValue(undefined)
    mockQueueStore.cancelRequest.mockResolvedValue(undefined)
    mockQueueStore.processNext.mockResolvedValue(undefined)
  })

  it('loads queue data on mount and frames queue as an advanced operator path', async () => {
    const wrapper = mount(AutomationQueueView)
    await waitForUi()

    expect(mockQueueStore.fetchByStatus).toHaveBeenCalledWith('Pending')
    expect(mockQueueStore.fetchStats).toHaveBeenCalledTimes(1)
    expect(wrapper.text()).toContain('When to use queue directly')
    expect(wrapper.text()).toContain('Back to Review')
    expect(wrapper.text()).toContain('Open Chat (Advanced)')
  })

  it('shows board picker and triage guidance in the composer', async () => {
    const wrapper = mount(AutomationQueueView)
    await waitForUi()
    await openComposer(wrapper)

    expect(wrapper.text()).toContain('Board-scoped instructions')
    expect(wrapper.text()).toContain('Inbox -> Start Triage')
    expect(wrapper.find('input[aria-label="Board for queue request"]').exists()).toBe(true)
  })

  it('bounds board discovery and recovers without blocking manual board IDs', async () => {
    let resolveRetry: (boards: Array<{ id: string; name: string }>) => void = () => {}
    const retryRequest = new Promise<Array<{ id: string; name: string }>>((resolve) => {
      resolveRetry = resolve
    })

    boardsMocks.getBoards
      .mockRejectedValueOnce(new Error('board discovery failed'))
      .mockReturnValueOnce(retryRequest)

    const wrapper = mount(AutomationQueueView)
    await flushPromises()
    await openComposer(wrapper)

    const boundedRead = {
      timeout: BOARD_REQUEST_TIMEOUT_MS,
      skipRetry: true,
    }

    expect(boardsMocks.getBoards).toHaveBeenNthCalledWith(1, undefined, true, boundedRead)
    expect(wrapper.get('[data-testid="queue-boards-error-message"]').attributes('role')).toBe('alert')
    expect(wrapper.get('[data-testid="queue-boards-error-message"]').text()).toContain(
      'Board suggestions could not be loaded.',
    )
    expect(
      wrapper.get('input[aria-label="Board for queue request"]').attributes('disabled'),
    ).toBeUndefined()

    const retryButton = wrapper.get('[data-testid="queue-boards-retry"]')
    await retryButton.trigger('click')
    await wrapper.vm.$nextTick()

    expect(boardsMocks.getBoards).toHaveBeenNthCalledWith(2, undefined, true, boundedRead)
    expect(wrapper.get('[data-testid="queue-boards-retry"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('[data-testid="queue-boards-retry"]').text()).toContain('Retrying boards...')

    resolveRetry([
      { id: '123e4567-e89b-12d3-a456-426614174000', name: 'Engineering Sprint' },
    ])
    await flushPromises()

    expect(wrapper.find('[data-testid="queue-boards-error-message"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="queue-boards-retry"]').exists()).toBe(false)
  })

  it('submits board id selected via board picker with queue request', async () => {
    const wrapper = mount(AutomationQueueView)
    await waitForUi()
    await openComposer(wrapper)

    await wrapper.get('input[placeholder="instruction"]').setValue(' instruction ')
    await wrapper.get('input[aria-label="Board for queue request"]').setValue('123e4567-e89b-12d3-a456-426614174000')
    await wrapper.get('textarea.paper-queue__textarea').setValue('  rename board to "Roadmap"  ')

    const submitButton = wrapper.findAll('button').find((button) => button.text() === 'Submit Request')
    if (!submitButton) {
      throw new Error('Expected submit button')
    }

    await submitButton.trigger('click')

    expect(mockQueueStore.submitRequest).toHaveBeenCalledWith({
      requestType: 'instruction',
      payload: 'rename board to "Roadmap"',
      boardId: '123e4567-e89b-12d3-a456-426614174000',
    })
  })

  it('blocks submit and shows toast error when board id is not a valid GUID', async () => {
    const wrapper = mount(AutomationQueueView)
    await waitForUi()
    await openComposer(wrapper)

    await wrapper.get('input[placeholder="instruction"]').setValue('instruction')
    await wrapper.get('input[aria-label="Board for queue request"]').setValue('not-a-guid')
    await wrapper.get('textarea.paper-queue__textarea').setValue('rename board to "Roadmap"')

    const submitButton = wrapper.findAll('button').find((button) => button.text() === 'Submit Request')
    if (!submitButton) {
      throw new Error('Expected submit button')
    }

    await submitButton.trigger('click')

    expect(mockQueueStore.submitRequest).not.toHaveBeenCalled()
    expect(toastMocks.error).toHaveBeenCalledWith(
      'Board ID must be a valid board selection or GUID.',
    )
  })

  it('blocks board-scoped instruction submit when board is empty', async () => {
    const wrapper = mount(AutomationQueueView)
    await waitForUi()
    await openComposer(wrapper)

    await wrapper.get('input[placeholder="instruction"]').setValue('instruction')
    await wrapper.get('input[aria-label="Board for queue request"]').setValue('   ')
    await wrapper.get('textarea.paper-queue__textarea').setValue('rename board to "Roadmap"')

    const submitButton = wrapper.findAll('button').find((button) => button.text() === 'Submit Request')
    if (!submitButton) {
      throw new Error('Expected submit button')
    }

    await submitButton.trigger('click')

    expect(mockQueueStore.submitRequest).not.toHaveBeenCalled()
    expect(toastMocks.error).toHaveBeenCalledWith('Board is required for board-scoped instructions. Select one from the board picker.')
  })

  it('shows actionable empty-state guidance and routes back to review', async () => {
    const wrapper = mount(AutomationQueueView)
    await waitForUi()

    expect(wrapper.text()).toContain('No queue requests match this filter')
    const reviewButton = wrapper.findAll('button').find((button) => button.text() === 'Open Review')
    expect(reviewButton).toBeTruthy()

    await reviewButton!.trigger('click')

    expect(routerMocks.push).toHaveBeenCalledWith('/workspace/review')
  })
})
