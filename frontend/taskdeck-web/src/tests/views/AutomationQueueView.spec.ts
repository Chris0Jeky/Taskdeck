import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
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

  it('shows guidance that board-scoped instructions need a GUID and triage starts from inbox', async () => {
    const wrapper = mount(AutomationQueueView)
    await waitForUi()
    await openComposer(wrapper)

    expect(wrapper.text()).toContain('Board-scoped instructions require a Board ID GUID')
    expect(wrapper.text()).toContain('Inbox -> Start Triage')
    expect(wrapper.get('input[placeholder="123e4567-e89b-12d3-a456-426614174000 (GUID for board-scoped instructions)"]').exists()).toBe(true)
  })

  it('submits trimmed valid GUID board id with queue request when provided', async () => {
    const wrapper = mount(AutomationQueueView)
    await waitForUi()
    await openComposer(wrapper)

    await wrapper.get('input[placeholder="instruction"]').setValue(' instruction ')
    await wrapper
      .get('input[placeholder="123e4567-e89b-12d3-a456-426614174000 (GUID for board-scoped instructions)"]')
      .setValue('  123E4567-E89B-12D3-A456-426614174000  ')
    await wrapper.get('textarea.td-textarea').setValue('  rename board to "Roadmap"  ')

    const submitButton = wrapper.findAll('button').find((button) => button.text() === 'Submit Request')
    if (!submitButton) {
      throw new Error('Expected submit button')
    }

    await submitButton.trigger('click')

    expect(mockQueueStore.submitRequest).toHaveBeenCalledWith({
      requestType: 'instruction',
      payload: 'rename board to "Roadmap"',
      boardId: '123E4567-E89B-12D3-A456-426614174000',
    })
  })

  it('blocks submit and shows toast error when board id is not a valid GUID', async () => {
    const wrapper = mount(AutomationQueueView)
    await waitForUi()
    await openComposer(wrapper)

    await wrapper.get('input[placeholder="instruction"]').setValue('instruction')
    await wrapper
      .get('input[placeholder="123e4567-e89b-12d3-a456-426614174000 (GUID for board-scoped instructions)"]')
      .setValue('board-42')
    await wrapper.get('textarea.td-textarea').setValue('rename board to "Roadmap"')

    const submitButton = wrapper.findAll('button').find((button) => button.text() === 'Submit Request')
    if (!submitButton) {
      throw new Error('Expected submit button')
    }

    await submitButton.trigger('click')

    expect(mockQueueStore.submitRequest).not.toHaveBeenCalled()
    expect(toastMocks.error).toHaveBeenCalledWith(
      'Board ID must be a GUID (for example 123e4567-e89b-12d3-a456-426614174000).',
    )
  })

  it('blocks board-scoped instruction submit when board id is empty', async () => {
    const wrapper = mount(AutomationQueueView)
    await waitForUi()
    await openComposer(wrapper)

    await wrapper.get('input[placeholder="instruction"]').setValue('instruction')
    await wrapper
      .get('input[placeholder="123e4567-e89b-12d3-a456-426614174000 (GUID for board-scoped instructions)"]')
      .setValue('   ')
    await wrapper.get('textarea.td-textarea').setValue('rename board to "Roadmap"')

    const submitButton = wrapper.findAll('button').find((button) => button.text() === 'Submit Request')
    if (!submitButton) {
      throw new Error('Expected submit button')
    }

    await submitButton.trigger('click')

    expect(mockQueueStore.submitRequest).not.toHaveBeenCalled()
    expect(toastMocks.error).toHaveBeenCalledWith('Board ID is required for board-scoped instructions.')
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
