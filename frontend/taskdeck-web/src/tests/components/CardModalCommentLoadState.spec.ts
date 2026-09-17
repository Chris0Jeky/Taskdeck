import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick, reactive } from 'vue'
import CardModal from '../../components/board/CardModal.vue'
import { boardsApi } from '../../api/boardsApi'
import { cardsApi } from '../../api/cardsApi'
import { useBoardStore } from '../../store/boardStore'
import type { BoardDetail, Card } from '../../types/board'
import type { CardComment } from '../../types/comments'

function createDeferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, resolve, reject }
}

vi.mock('../../api/cardsApi', () => ({
  cardsApi: {
    getCards: vi.fn(),
    getParticipants: vi.fn(),
    getCard: vi.fn(),
    replaceAssignments: vi.fn(),
    previewDetach: vi.fn(),
  },
}))

vi.mock('../../api/boardsApi', () => ({
  boardsApi: { getBoard: vi.fn() },
}))

vi.mock('../../store/boardStore', () => ({
  useBoardStore: vi.fn(),
}))

vi.mock('../../store/sessionStore', () => ({
  useSessionStore: () => ({ userId: 'user-1' }),
}))

const card: Card = {
  id: 'card-1',
  boardId: 'board-1',
  columnId: 'column-1',
  title: 'Comment honesty',
  description: '',
  position: 0,
  dueDate: null,
  isBlocked: false,
  blockReason: null,
  labels: [],
  assignments: [],
  createdAt: '2026-09-01T00:00:00Z',
  updatedAt: '2026-09-01T00:00:00Z',
}

const board = {
  id: card.boardId,
  canWrite: true,
  isArchived: false,
  columns: [],
} as unknown as BoardDetail

describe('CardModal comment load state', () => {
  let fetchCardComments: ReturnType<typeof vi.fn>

  beforeEach(() => {
    vi.resetAllMocks()
    setActivePinia(createPinia())

    vi.mocked(cardsApi.getCards).mockResolvedValue([])
    vi.mocked(cardsApi.getParticipants).mockResolvedValue([])
    vi.mocked(cardsApi.getCard).mockResolvedValue(card)
    vi.mocked(cardsApi.previewDetach).mockResolvedValue({
      cardId: card.id,
      expectedUpdatedAt: card.updatedAt,
      expectedChildrenFingerprint: 'v1:children',
      children: [],
    })
    vi.mocked(boardsApi.getBoard).mockResolvedValue(board)

    fetchCardComments = vi.fn()
    const store = reactive({
      currentBoard: board,
      currentBoardCards: [card],
      currentBoardRequestGeneration: 1,
      currentBoardPayloadGeneration: 1,
      fetchCardComments,
      fetchCardProvenance: vi.fn().mockResolvedValue(null),
      getCardComments: vi.fn().mockReturnValue([]),
      setEditingCard: vi.fn(),
      fetchBoard: vi.fn(),
      setCardArchived: vi.fn(),
      updateCard: vi.fn(),
      deleteCard: vi.fn(),
      createCardComment: vi.fn(),
      updateCardComment: vi.fn(),
      deleteCardComment: vi.fn(),
      editingCardId: null,
    })
    vi.mocked(useBoardStore).mockReturnValue(store as never)
  })

  afterEach(() => {
    document.body.innerHTML = ''
    vi.restoreAllMocks()
  })

  it('withholds the empty state until a failed read is retried successfully', async () => {
    const initialRead = createDeferred<CardComment[]>()
    const retryRead = createDeferred<CardComment[]>()
    fetchCardComments
      .mockReturnValueOnce(initialRead.promise)
      .mockReturnValueOnce(retryRead.promise)

    const wrapper = mount(CardModal, {
      attachTo: document.body,
      props: { card, isOpen: true, labels: [] },
    })

    await nextTick()
    expect(fetchCardComments).toHaveBeenCalledTimes(1)
    expect(wrapper.get('[data-testid="card-comments-loading"]').text()).toContain('Loading comments')
    expect(wrapper.find('[data-testid="card-comments-empty"]').exists()).toBe(false)

    initialRead.reject(new Error('offline'))
    await flushPromises()

    expect(wrapper.get('[data-testid="card-comments-load-error"]').text()).toContain(
      'Comments could not be loaded',
    )
    expect(wrapper.find('[data-testid="card-comments-empty"]').exists()).toBe(false)

    await wrapper.get('[data-testid="card-comments-retry"]').trigger('click')
    await nextTick()

    expect(fetchCardComments).toHaveBeenCalledTimes(2)
    expect(wrapper.get('[data-testid="card-comments-loading"]').text()).toContain('Loading comments')
    expect(wrapper.find('[data-testid="card-comments-load-error"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="card-comments-empty"]').exists()).toBe(false)

    retryRead.resolve([])
    await flushPromises()

    expect(wrapper.find('[data-testid="card-comments-loading"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="card-comments-load-error"]').exists()).toBe(false)
    expect(wrapper.get('[data-testid="card-comments-empty"]').text()).toContain('No comments yet')

    wrapper.unmount()
  })
})
