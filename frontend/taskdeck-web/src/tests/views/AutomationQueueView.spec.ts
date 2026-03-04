import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import type { Proposal } from '../../types/automation'
import AutomationQueueView from '../../views/AutomationQueueView.vue'

const mocks = vi.hoisted(() => ({
  getProposals: vi.fn(),
  approveProposal: vi.fn(),
  rejectProposal: vi.fn(),
  executeProposal: vi.fn(),
  getProposalDiff: vi.fn(),
  fetchByStatus: vi.fn(),
  fetchStats: vi.fn(),
  submitRequest: vi.fn(),
  cancelRequest: vi.fn(),
  processNext: vi.fn(),
  successToast: vi.fn(),
  errorToast: vi.fn(),
  createRequestId: vi.fn(),
}))

vi.mock('../../api/automationApi', () => ({
  automationApi: {
    getProposals: mocks.getProposals,
    approveProposal: mocks.approveProposal,
    rejectProposal: mocks.rejectProposal,
    executeProposal: mocks.executeProposal,
    getProposalDiff: mocks.getProposalDiff,
  },
}))

vi.mock('../../store/queueStore', () => ({
  useQueueStore: () => ({
    stats: {
      pendingCount: 1,
      processingCount: 0,
      completedCount: 0,
      failedCount: 0,
      cancelledCount: 0,
    },
    loading: false,
    requests: [],
    fetchByStatus: mocks.fetchByStatus,
    fetchStats: mocks.fetchStats,
    submitRequest: mocks.submitRequest,
    cancelRequest: mocks.cancelRequest,
    processNext: mocks.processNext,
  }),
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => ({
    success: mocks.successToast,
    error: mocks.errorToast,
  }),
}))

vi.mock('../../utils/requestId', () => ({
  createRequestId: mocks.createRequestId,
}))

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (_error: unknown, fallback: string) => ({ message: fallback }),
}))

function buildProposal(overrides: Partial<Proposal> = {}): Proposal {
  const now = new Date().toISOString()
  return {
    id: 'proposal-1',
    sourceType: 'Queue',
    sourceReferenceId: 'capture-1',
    boardId: 'board-1',
    requestedByUserId: 'user-1',
    status: 'PendingReview',
    riskLevel: 'Low',
    summary: 'Queue proposal',
    diffPreview: null,
    validationIssues: null,
    createdAt: now,
    updatedAt: now,
    expiresAt: now,
    decidedAt: null,
    decidedByUserId: null,
    appliedAt: null,
    failureReason: null,
    correlationId: 'triage-run-1',
    operations: [],
    ...overrides,
  }
}

async function mountAt(path: string) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/workspace/automations/queue', name: 'workspace-automations-queue', component: AutomationQueueView },
      { path: '/workspace/automations/proposals', name: 'workspace-automations-proposals', component: AutomationQueueView },
    ],
  })

  await router.push(path)
  await router.isReady()

  const wrapper = mount(AutomationQueueView, {
    attachTo: document.body,
    global: {
      plugins: [router],
    },
  })

  await Promise.resolve()
  await Promise.resolve()
  await wrapper.vm.$nextTick()
  mountedWrapper = wrapper
  return wrapper
}

async function openComposer(wrapper: ReturnType<typeof mount>) {
  const toggle = wrapper.findAll('button').find(button => button.text().includes('+ New Request'))
  if (!toggle) {
    throw new Error('Expected composer toggle button')
  }

  await toggle.trigger('click')
  await wrapper.vm.$nextTick()
}

let mountedWrapper: ReturnType<typeof mount> | null = null
let originalScrollIntoView: typeof HTMLElement.prototype.scrollIntoView

describe('AutomationQueueView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    originalScrollIntoView = HTMLElement.prototype.scrollIntoView
    mocks.fetchByStatus.mockResolvedValue(undefined)
    mocks.fetchStats.mockResolvedValue(undefined)
    mocks.submitRequest.mockResolvedValue(undefined)
    mocks.cancelRequest.mockResolvedValue(undefined)
    mocks.processNext.mockResolvedValue(undefined)
    mocks.approveProposal.mockResolvedValue(buildProposal({ status: 'Approved' }))
    mocks.rejectProposal.mockResolvedValue(buildProposal({ status: 'Rejected' }))
    mocks.executeProposal.mockResolvedValue(buildProposal({ status: 'Applied' }))
    mocks.getProposalDiff.mockResolvedValue('diff')
    mocks.createRequestId.mockReturnValue('request-1')
  })

  afterEach(() => {
    mountedWrapper?.unmount()
    mountedWrapper = null

    Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', {
      configurable: true,
      writable: true,
      value: originalScrollIntoView,
    })
  })

  it('shows guidance that board-scoped instructions need a GUID board id and capture uses triage', async () => {
    const wrapper = await mountAt('/workspace/automations/queue')
    await openComposer(wrapper)

    expect(wrapper.text()).toContain('Board-scoped instructions require a Board ID GUID')
    expect(wrapper.text()).toContain('For board-scoped instruction patterns, provide a valid Board ID')
    expect(wrapper.text()).toContain('123e4567-e89b-12d3-a456-426614174000')
    expect(wrapper.text()).toContain('Inbox -> Start Triage')
  })

  it('submits trimmed valid GUID board id with queue request when provided', async () => {
    const wrapper = await mountAt('/workspace/automations/queue')
    await openComposer(wrapper)

    await wrapper.get('input[placeholder="instruction"]').setValue(' instruction ')
    await wrapper
      .get('input[placeholder="123e4567-e89b-12d3-a456-426614174000 (GUID for board-scoped instructions)"]')
      .setValue('  123E4567-E89B-12D3-A456-426614174000  ')
    await wrapper.get('textarea.td-textarea').setValue('  rename board to "Roadmap"  ')

    const submitButton = wrapper.findAll('button').find(button => button.text() === 'Submit Request')
    if (!submitButton) {
      throw new Error('Expected submit button')
    }

    await submitButton.trigger('click')

    expect(mocks.submitRequest).toHaveBeenCalledWith({
      requestType: 'instruction',
      payload: 'rename board to "Roadmap"',
      boardId: '123E4567-E89B-12D3-A456-426614174000',
    })
  })

  it('accepts no-hyphen GUID format for board id', async () => {
    const wrapper = await mountAt('/workspace/automations/queue')
    await openComposer(wrapper)

    await wrapper.get('input[placeholder="instruction"]').setValue('instruction')
    await wrapper
      .get('input[placeholder="123e4567-e89b-12d3-a456-426614174000 (GUID for board-scoped instructions)"]')
      .setValue('123e4567e89b12d3a456426614174000')
    await wrapper.get('textarea.td-textarea').setValue('rename board to "Roadmap"')

    const submitButton = wrapper.findAll('button').find(button => button.text() === 'Submit Request')
    if (!submitButton) {
      throw new Error('Expected submit button')
    }

    await submitButton.trigger('click')

    expect(mocks.submitRequest).toHaveBeenCalledWith({
      requestType: 'instruction',
      payload: 'rename board to "Roadmap"',
      boardId: '123e4567e89b12d3a456426614174000',
    })
  })

  it('blocks submit and shows toast error when board id is not a valid GUID', async () => {
    const wrapper = await mountAt('/workspace/automations/queue')
    await openComposer(wrapper)

    await wrapper.get('input[placeholder="instruction"]').setValue('instruction')
    await wrapper
      .get('input[placeholder="123e4567-e89b-12d3-a456-426614174000 (GUID for board-scoped instructions)"]')
      .setValue('board-42')
    await wrapper.get('textarea.td-textarea').setValue('rename board to "Roadmap"')

    const submitButton = wrapper.findAll('button').find(button => button.text() === 'Submit Request')
    if (!submitButton) {
      throw new Error('Expected submit button')
    }

    await submitButton.trigger('click')

    expect(mocks.submitRequest).not.toHaveBeenCalled()
    expect(mocks.errorToast).toHaveBeenCalledWith(
      'Board ID must be a GUID (for example 123e4567-e89b-12d3-a456-426614174000).',
    )
  })

  it('omits board id from queue request when board input is blank', async () => {
    const wrapper = await mountAt('/workspace/automations/queue')
    await openComposer(wrapper)

    await wrapper
      .get('input[placeholder="123e4567-e89b-12d3-a456-426614174000 (GUID for board-scoped instructions)"]')
      .setValue('   ')
    await wrapper.get('textarea.td-textarea').setValue('list pending proposals')

    const submitButton = wrapper.findAll('button').find(button => button.text() === 'Submit Request')
    if (!submitButton) {
      throw new Error('Expected submit button')
    }

    await submitButton.trigger('click')

    const [submittedDto] = mocks.submitRequest.mock.calls.at(-1) ?? []
    expect(submittedDto).toEqual({
      requestType: 'instruction',
      payload: 'list pending proposals',
    })
    expect(submittedDto).not.toHaveProperty('boardId')
  })

  it('blocks board-scoped instruction submit when board id is empty', async () => {
    const wrapper = await mountAt('/workspace/automations/queue')
    await openComposer(wrapper)

    await wrapper.get('input[placeholder="instruction"]').setValue('instruction')
    await wrapper
      .get('input[placeholder="123e4567-e89b-12d3-a456-426614174000 (GUID for board-scoped instructions)"]')
      .setValue('   ')
    await wrapper.get('textarea.td-textarea').setValue('rename board to "Roadmap"')

    const submitButton = wrapper.findAll('button').find(button => button.text() === 'Submit Request')
    if (!submitButton) {
      throw new Error('Expected submit button')
    }

    await submitButton.trigger('click')

    expect(mocks.submitRequest).not.toHaveBeenCalled()
    expect(mocks.errorToast).toHaveBeenCalledWith('Board ID is required for board-scoped instructions.')
  })

  it('disables submit button until request type and payload are both non-empty', async () => {
    const wrapper = await mountAt('/workspace/automations/queue')
    await openComposer(wrapper)

    const submitButton = wrapper.findAll('button').find(button => button.text() === 'Submit Request')
    if (!submitButton) {
      throw new Error('Expected submit button')
    }

    expect((submitButton.element as HTMLButtonElement).disabled).toBe(true)

    await wrapper.get('input[placeholder="instruction"]').setValue('instruction')
    await wrapper.vm.$nextTick()
    expect((submitButton.element as HTMLButtonElement).disabled).toBe(true)

    await wrapper.get('textarea.td-textarea').setValue('list pending proposals')
    await wrapper.vm.$nextTick()
    expect((submitButton.element as HTMLButtonElement).disabled).toBe(false)
  })

  it('shows capture and triage provenance context for queue proposals', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({
        id: 'proposal-99',
        sourceType: 'Queue',
        sourceReferenceId: 'capture-99',
        correlationId: 'triage-run-99',
      }),
    ])

    const wrapper = await mountAt('/workspace/automations/proposals')

    expect(mocks.getProposals).toHaveBeenCalledWith({ limit: 200 })
    expect(wrapper.text()).toContain('Capture-linked')
    expect(wrapper.text()).toContain('Triage run: triage-run-99')
    expect(wrapper.find('a[href="/workspace/inbox#capture-capture-99"]').exists()).toBe(true)
    expect(wrapper.find('a[href="/workspace/automations/proposals#proposal-proposal-99"]').exists()).toBe(true)
  })

  it('does not render capture link for non-queue proposals', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({
        id: 'proposal-manual',
        sourceType: 'Manual',
        sourceReferenceId: null,
        correlationId: 'manual-correlation',
      }),
    ])

    const wrapper = await mountAt('/workspace/automations/proposals')

    expect(wrapper.text()).not.toContain('Capture-linked')
    expect(wrapper.text()).not.toContain('Open Capture')
    expect(wrapper.text()).not.toContain('Triage run:')
  })

  it('scrolls to proposal card when route hash targets a proposal id', async () => {
    mocks.getProposals.mockResolvedValue([buildProposal({ id: 'proposal-42' })])
    const scrollSpy = vi.fn()
    Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', {
      configurable: true,
      writable: true,
      value: scrollSpy,
    })

    await mountAt('/workspace/automations/proposals#proposal-proposal-42')

    expect(mocks.getProposals).toHaveBeenCalled()
    expect(scrollSpy).toHaveBeenCalled()
  })

  it('does not attempt proposal scrolling when no proposal hash is present', async () => {
    mocks.getProposals.mockResolvedValue([buildProposal({ id: 'proposal-42' })])
    const scrollSpy = vi.fn()
    Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', {
      configurable: true,
      writable: true,
      value: scrollSpy,
    })

    await mountAt('/workspace/automations/proposals')

    expect(scrollSpy).not.toHaveBeenCalled()
  })

  it('ignores malformed proposal hash fragments without throwing', async () => {
    mocks.getProposals.mockResolvedValue([buildProposal({ id: 'proposal-42' })])
    const scrollSpy = vi.fn()
    Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', {
      configurable: true,
      writable: true,
      value: scrollSpy,
    })

    const wrapper = await mountAt('/workspace/automations/proposals#proposal-%E0%A4%A')

    expect(wrapper.exists()).toBe(true)
    expect(scrollSpy).not.toHaveBeenCalled()
  })
})
