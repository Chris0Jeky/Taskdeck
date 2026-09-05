import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { reactive } from 'vue'
import PaperInboxView from '../../../../views/paper/PaperInboxView.vue'
import type { BoardDetail } from '../../../../types/board'

/**
 * The Inbox scope chip and the Inbox list request, asserted in ONE test (#1984
 * finding 2).
 *
 * Neither existing spec can hold both halves of this truth.
 * `PaperInboxView.spec.ts` replaces `useInboxOrchestrator` with a stub, so the
 * real `fetchItems` argument never exists there; `useInboxOrchestrator.spec.ts`
 * mocks the `vue` module itself, so no component can be mounted there. The
 * defect this issue records is exactly the seam between them: the chip named a
 * Column filter that the list request never applied. So this spec mounts the
 * real view over the real orchestrator with only the route, the capture store,
 * the session store and the boards API stubbed, and observes the rendered chip
 * and the outgoing request together.
 *
 * It lives beside the inbox component specs so the region's standing
 * verification command (`src/tests/views/paper/inbox`) already covers it.
 */

const mockRoute = reactive<{ hash: string; query: Record<string, unknown> }>({
  hash: '',
  query: {},
})
const mockRouter = { push: vi.fn(), replace: vi.fn() }

vi.mock('vue-router', () => ({
  useRoute: () => mockRoute,
  useRouter: () => mockRouter,
}))

const mockCaptureStore = reactive({
  items: [] as Array<{ id: string }>,
  detailById: {} as Record<string, unknown>,
  loadingList: false,
  listError: null as string | null,
  actionBusyItemId: null as string | null,
  triagePollingItemId: null as string | null,
  fetchItems: vi.fn<(...args: unknown[]) => Promise<void>>(),
  fetchDetail: vi.fn(),
  peekDetail: vi.fn(),
  cacheDetail: vi.fn(),
  createItem: vi.fn(),
  triageItem: vi.fn(),
  keepItem: vi.fn(),
  archiveItem: vi.fn(),
  ignoreItem: vi.fn(),
  cancelItem: vi.fn(),
  batchTriage: vi.fn(),
  updateSuggestion: vi.fn(),
  pollTriageCompletion: vi.fn(() => () => undefined),
  pollBatchTriageCompletion: vi.fn(() => () => undefined),
})

vi.mock('../../../../store/captureStore', () => ({
  useCaptureStore: () => mockCaptureStore,
}))

const mockBoardsApi = vi.hoisted(() => ({
  getBoard: vi.fn<(id: string) => Promise<BoardDetail>>(),
}))

vi.mock('../../../../api/boardsApi', () => ({
  boardsApi: mockBoardsApi,
}))

const mockBoardStore = reactive({
  boards: [] as Array<{ id: string; name: string }>,
  fetchBoards: vi.fn<() => Promise<void>>(),
})

vi.mock('../../../../store/boardStore', () => ({
  useBoardStore: () => mockBoardStore,
}))

const mockSessionStore = reactive({ userId: 'user-a' as string | null })

vi.mock('../../../../store/sessionStore', () => ({
  useSessionStore: () => mockSessionStore,
}))

function scopedBoard(): BoardDetail {
  const timestamp = '2026-09-04T00:00:00Z'
  return {
    id: 'board-1',
    name: 'Payments API Migration',
    description: null,
    isArchived: false,
    createdAt: timestamp,
    updatedAt: timestamp,
    columns: [
      {
        id: 'col-ready',
        boardId: 'board-1',
        name: 'Ready',
        position: 0,
        wipLimit: null,
        cardCount: 0,
        createdAt: timestamp,
        updatedAt: timestamp,
      },
    ],
  }
}

/**
 * Shallow so the heavy capture/triage children stay stubbed, but with the scope
 * disclosure left real: the chip's rendered text is the thing under test.
 */
function mountInbox() {
  return mount(PaperInboxView, {
    shallow: true,
    global: {
      stubs: { PaperScopeDisclosure: false },
    },
  })
}

describe('Paper Inbox scope truth (#1984)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockRoute.hash = ''
    mockRoute.query = {}
    mockCaptureStore.items = []
    mockCaptureStore.detailById = {}
    mockCaptureStore.loadingList = false
    mockCaptureStore.listError = null
    mockCaptureStore.fetchItems.mockResolvedValue(undefined)
    mockBoardsApi.getBoard.mockResolvedValue(scopedBoard())
    mockBoardStore.fetchBoards.mockResolvedValue(undefined)
    mockSessionStore.userId = 'user-a'
  })

  it('never names a Column filter in the chip while the list request is board-only', async () => {
    mockRoute.query = { boardId: 'board-1', columnId: 'col-ready' }

    const wrapper = mountInbox()
    await flushPromises()

    // What the list request actually applies.
    expect(mockCaptureStore.fetchItems).toHaveBeenCalledTimes(1)
    const listQuery = mockCaptureStore.fetchItems.mock.calls[0]![0] as Record<string, unknown>
    expect(listQuery).toEqual({ limit: 200, boardId: 'board-1' })
    expect(listQuery).not.toHaveProperty('columnId')

    // What the chip claims it applies. The two must say the same thing.
    const chip = wrapper.get('[data-testid="paper-scope-disclosure"]')
    expect(chip.text()).toContain('Board: Payments API Migration')
    expect(chip.text()).not.toContain('Column')
    expect(chip.text()).not.toContain('Ready')

    wrapper.unmount()
  })

  it('keeps the chip board-only when the route carries no column at all', async () => {
    mockRoute.query = { boardId: 'board-1' }

    const wrapper = mountInbox()
    await flushPromises()

    expect(mockCaptureStore.fetchItems).toHaveBeenCalledWith({ limit: 200, boardId: 'board-1' })
    expect(wrapper.get('[data-testid="paper-scope-disclosure"]').text()).toContain(
      'Board: Payments API Migration',
    )

    wrapper.unmount()
  })
})
