import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { reactive } from 'vue'
import QuietInsightsView from '../../views/QuietInsightsView.vue'
import type { Board } from '../../types/board'
import type { Insight, Memory } from '../../types/workspaceInsights'

const mocks = vi.hoisted(() => ({
  route: { query: {} as Record<string, string> },
  boardStore: { boards: [] as Board[], fetchBoards: vi.fn() },
  api: {
    getInsights: vi.fn(),
    analyzeBoard: vi.fn(),
    updateInsight: vi.fn(),
    answerInsight: vi.fn(),
  },
}))

const routeMock = reactive(mocks.route)
const boardStore = reactive(mocks.boardStore)
const api = mocks.api

vi.mock('vue-router', () => ({
  onBeforeRouteLeave: vi.fn(),
  onBeforeRouteUpdate: vi.fn(),
  useRoute: () => mocks.route,
  RouterLink: {
    props: ['to'],
    template: '<a :href="typeof to === \'string\' ? to : \'#\'"><slot /></a>',
  },
}))

vi.mock('../../store/boardStore', () => ({
  useBoardStore: () => mocks.boardStore,
}))

vi.mock('../../api/workspaceInsights', () => ({
  workspaceInsightsApi: mocks.api,
}))

const board: Board = {
  id: 'board-1',
  name: 'Product board',
  description: null,
  isArchived: false,
  createdAt: '2026-09-01T00:00:00Z',
  updatedAt: '2026-09-01T00:00:00Z',
}

const insight: Insight = {
  id: 'insight-1',
  boardId: board.id,
  cardId: 'card-1',
  memoryId: null,
  rule: 'blocked-next-step',
  title: 'A blocked card has no next step',
  detail: 'A blocked card is waiting without a recorded next action.',
  state: 'available',
  evidence: 'Card “Prepare release notes” is blocked: Waiting on copy review.',
  checkedAt: '2026-09-08T09:00:00Z',
  snoozeUntil: null,
}

const memory: Memory = {
  id: 'memory-1',
  boardId: board.id,
  title: 'Release notes context',
  text: 'Copy review is owned by design.',
  originalText: 'Copy review is owned by design.',
  originalEvidence: insight.evidence,
  status: 'statement',
  archived: false,
  revision: 1,
  createdAt: '2026-09-08T09:01:00Z',
  history: [],
}

async function settle() {
  await flushPromises()
  await flushPromises()
}

function mountView() {
  return mount(QuietInsightsView)
}

describe('QuietInsightsView', () => {
  it('does not replace an unavailable linked board with a different board', async () => {
    mocks.route.query = { boardId: 'unavailable' }
    const wrapper = mountView()
    await settle()
    expect(wrapper.text()).toContain('This board is not available')
    expect(api.getInsights).not.toHaveBeenCalled()
    wrapper.unmount()
  })
  it('keeps the question evidence fixed while an answer is being composed', async () => {
    const wrapper = mountView()
    await settle()
    await wrapper.get('[data-action="answer-insight"]').trigger('click')
    await wrapper.get('textarea').setValue('A useful next step')
    expect(wrapper.get('[data-action="analyze-insights"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('[data-action="dismiss-insight"]').attributes('disabled')).toBeDefined()
    wrapper.unmount()
  })
  beforeEach(() => {
    vi.clearAllMocks()
    routeMock.query = {}
    boardStore.boards = [board]
    boardStore.fetchBoards.mockResolvedValue(undefined)
    api.getInsights.mockResolvedValue([insight])
    api.analyzeBoard.mockResolvedValue([insight])
    api.updateInsight.mockResolvedValue({ ...insight, state: 'dismissed' })
    api.answerInsight.mockResolvedValue(memory)
  })

  it('loads the selected board and shows structural evidence', async () => {
    const wrapper = mountView()
    await settle()

    expect(boardStore.fetchBoards).toHaveBeenCalledOnce()
    expect(api.getInsights).toHaveBeenCalledWith('board-1')
    expect(wrapper.text()).toContain('Quiet insights')
    expect(wrapper.text()).toContain(insight.evidence)
    expect(wrapper.find('a[href="/workspace/boards/board-1"]').exists()).toBe(true)
    expect(wrapper.find('a[href="/workspace/boards/board-1/cards/card-1/thinking"]').exists()).toBe(true)
  })

  it('waits for explicit Analyze now before requesting analysis', async () => {
    const wrapper = mountView()
    await settle()

    expect(api.analyzeBoard).not.toHaveBeenCalled()
    await wrapper.get('[data-action="analyze-insights"]').trigger('click')
    await settle()

    expect(api.analyzeBoard).toHaveBeenCalledWith({ boardId: 'board-1' })
  })

  it('sends action and preserves returned insight state', async () => {
    const dismissed = { ...insight, state: 'dismissed' as const }
    api.updateInsight.mockResolvedValue(dismissed)
    api.getInsights.mockResolvedValueOnce([insight]).mockResolvedValueOnce([dismissed])
    const wrapper = mountView()
    await settle()

    await wrapper.get('[data-action="dismiss-insight"]').trigger('click')
    await settle()

    expect(api.updateInsight).toHaveBeenCalledWith('insight-1', 'dismiss')
    expect(wrapper.text()).toContain('Dismissed')
  })

  it('answers with the exact displayed evidence and states board is unchanged', async () => {
    const wrapper = mountView()
    await settle()
    await wrapper.get('[data-action="answer-insight"]').trigger('click')
    await wrapper.get('#answer-insight-1').setValue('Design owns the copy review.')
    await wrapper.get('form.paper-insights__answer').trigger('submit')
    await settle()

    expect(api.answerInsight).toHaveBeenCalledWith('insight-1', {
      text: 'Design owns the copy review.',
      status: 'statement',
      evidence: insight.evidence,
    })
    expect(wrapper.text()).toContain('Saved to private memory.')
    expect(wrapper.text()).toContain('does not edit cards or columns')
  })

  it('renders an actionable API error', async () => {
    api.getInsights.mockRejectedValue(new Error('Insights unavailable'))
    const wrapper = mountView()
    await settle()

    expect(wrapper.find('[role="alert"]').text()).toContain('Insights unavailable')
    expect(wrapper.find('[role="alert"] button').text()).toBe('Retry')
  })
})
