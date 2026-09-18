import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import type { Proposal } from '../../types/automation'
import ReviewView from '../../views/ReviewView.vue'

const vueHelpers = vi.hoisted(async () => {
  const { computed, ref, shallowRef } = await import('vue')
  return { computed, ref, shallowRef }
})

vi.mock('../../composables/useVirtualList', async () => {
  const { computed, ref, shallowRef } = await vueHelpers
  return {
    useVirtualList: (options: { count: { value: number } | (() => number); estimateSize: number }) => {
      const count = options.count
      const getCount = typeof count === 'function' ? count : () => count.value
      return {
        parentRef: ref<HTMLElement | null>(null),
        virtualItemEls: shallowRef<HTMLElement[]>([]),
        virtualRows: computed(() => Array.from({ length: getCount() }, (_, index) => ({
          key: index,
          index,
          start: index * options.estimateSize,
          end: (index + 1) * options.estimateSize,
          size: options.estimateSize,
          lane: 0,
        }))),
        totalSize: computed(() => getCount() * options.estimateSize),
        translateY: computed(() => 0),
        scrollToIndex: vi.fn(),
      }
    },
  }
})

const mocks = vi.hoisted(() => ({
  getProposals: vi.fn(),
  getProposal: vi.fn(),
  getBoards: vi.fn(),
  getColumns: vi.fn(),
  getRevisions: vi.fn(),
  refreshWorkloadCounts: vi.fn(),
  successToast: vi.fn(),
  errorToast: vi.fn(),
  infoToast: vi.fn(),
}))

vi.mock('../../api/automationApi', () => ({
  automationApi: {
    getProposals: mocks.getProposals,
    getProposal: mocks.getProposal,
    approveProposal: vi.fn(),
    rejectProposal: vi.fn(),
    deferProposal: vi.fn(),
    executeProposal: vi.fn(),
    getProposalDiff: vi.fn(),
    dismissProposals: vi.fn(),
  },
}))
vi.mock('../../api/boardsApi', () => ({ boardsApi: { getBoards: mocks.getBoards } }))
vi.mock('../../api/columnsApi', () => ({ columnsApi: { getColumns: mocks.getColumns } }))
vi.mock('../../api/proposalRevisionsApi', () => ({
  proposalRevisionsApi: { getRevisions: mocks.getRevisions },
}))
vi.mock('../../store/workspaceStore', () => ({
  useWorkspaceStore: () => ({ refreshWorkloadCounts: mocks.refreshWorkloadCounts }),
}))
vi.mock('../../store/toastStore', () => ({
  useToastStore: () => ({
    success: mocks.successToast,
    error: mocks.errorToast,
    info: mocks.infoToast,
  }),
}))
vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (_error: unknown, fallback: string) => ({ message: fallback, code: null }),
  isValidationError: () => false,
  getValidationReason: () => null,
}))
vi.mock('../../utils/requestId', () => ({ createRequestId: () => 'request-1' }))

function deferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  const promise = new Promise<T>((innerResolve) => { resolve = innerResolve })
  return { promise, resolve }
}

function proposal(overrides: Partial<Proposal> = {}): Proposal {
  const now = new Date().toISOString()
  return {
    id: 'proposal-visible',
    sourceType: 'Queue',
    sourceReferenceId: 'capture-1',
    boardId: 'board-1',
    requestedByUserId: 'user-1',
    status: 'PendingReview',
    riskLevel: 'Low',
    summary: 'Visible proposal',
    diffPreview: null,
    validationIssues: null,
    createdAt: now,
    updatedAt: now,
    expiresAt: new Date(Date.now() + 60 * 60_000).toISOString(),
    decidedAt: null,
    decidedByUserId: null,
    appliedAt: null,
    failureReason: null,
    correlationId: 'corr-1',
    operations: [],
    presentation: {
      plainSummary: 'Visible proposal',
      impactSummary: 'One planned change.',
      riskCue: 'Low risk.',
      sourceCue: 'Created from a queue item.',
      operationHeadlines: [],
      affectedEntities: [],
    },
    approvedRevisionId: null,
    latestRevisionId: null,
    ...overrides,
  }
}

async function mountView(path: string) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/workspace/review', name: 'workspace-review', component: ReviewView },
      { path: '/workspace/inbox', name: 'workspace-inbox', component: { template: '<div />' } },
      { path: '/workspace/boards/:id', name: 'workspace-board', component: { template: '<div />' } },
    ],
  })
  await router.push(path)
  await router.isReady()
  const wrapper = mount(ReviewView, {
    attachTo: document.body,
    global: { plugins: [router] },
  })
  await flushPromises()
  await wrapper.vm.$nextTick()
  return { wrapper, router }
}

describe('Legacy Review unavailable-return root fallback (GH-2599)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    localStorage.setItem('td.paper.mode.v2', 'off')
    mocks.getBoards.mockResolvedValue([])
    mocks.getColumns.mockResolvedValue([])
    mocks.getRevisions.mockResolvedValue([])
    mocks.getProposal.mockRejectedValue({ response: { status: 404 } })
  })

  afterEach(() => {
    document.body.innerHTML = ''
    localStorage.clear()
  })

  it('focuses the stable review landmark when a concurrent reload hides the queue and empty state', async () => {
    const visible = proposal()
    const reload = deferred<Proposal[]>()
    mocks.getProposals
      .mockResolvedValueOnce([visible])
      .mockReturnValueOnce(reload.promise)

    const { wrapper, router } = await mountView(
      '/workspace/review#proposal-proposal-missing',
    )
    try {
      const back = wrapper.get('[data-testid="review-unavailable-return"]')
      const refresh = wrapper.findAll('button').find((button) => button.text() === 'Refresh Review')
      expect(refresh).toBeDefined()
      ;(back.element as HTMLElement).focus()
      expect(document.activeElement).toBe(back.element)

      // Start both handlers in the same turn. The reload raises the skeleton,
      // while Return clears the pin and then performs its focus handoff.
      ;(refresh!.element as HTMLButtonElement).click()
      ;(back.element as HTMLButtonElement).click()
      await flushPromises()
      await wrapper.vm.$nextTick()

      expect(router.currentRoute.value.hash).toBe('')
      expect(wrapper.find('.td-review__skeleton').exists()).toBe(true)
      expect(wrapper.find('section[aria-label="Proposals awaiting review"]').exists()).toBe(false)
      expect(wrapper.find('.td-review-empty').exists()).toBe(false)
      const landmark = wrapper.get('.td-review').element
      expect(landmark.getAttribute('tabindex')).toBe('-1')
      expect(document.activeElement).toBe(landmark)

      // The landmark is a transient focus target only. Once it loses focus the
      // attribute goes with it, so an ordinary click on inert review content
      // cannot land on this root and suppress the skins' own focus handoffs.
      ;(landmark as HTMLElement).blur()
      await wrapper.vm.$nextTick()
      expect(landmark.getAttribute('tabindex')).toBeNull()

      reload.resolve([visible])
      await flushPromises()
    } finally {
      wrapper.unmount()
    }
  })
})
