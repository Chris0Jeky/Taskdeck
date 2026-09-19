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

const cachedComment: CardComment = {
  id: 'comment-1',
  boardId: card.boardId,
  cardId: card.id,
  parentCommentId: null,
  authorUserId: 'user-2',
  authorUsername: 'Teammate',
  content: 'Previously loaded comment',
  isDeleted: false,
  editedAt: null,
  mentions: [],
  createdAt: '2026-09-01T12:00:00Z',
  updatedAt: '2026-09-01T12:00:00Z',
}

describe('CardModal comment load state', () => {
  let fetchCardComments: ReturnType<typeof vi.fn>
  let getCardComments: ReturnType<typeof vi.fn>

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
    getCardComments = vi.fn().mockReturnValue([])
    const store = reactive({
      currentBoard: board,
      currentBoardCards: [card],
      currentBoardRequestGeneration: 1,
      currentBoardPayloadGeneration: 1,
      fetchCardComments,
      fetchCardProvenance: vi.fn().mockResolvedValue(null),
      getCardComments,
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

  it('keeps cached comments visible when their refresh cannot be confirmed', async () => {
    getCardComments.mockReturnValue([cachedComment])
    fetchCardComments.mockRejectedValueOnce(new Error('offline'))

    const wrapper = mount(CardModal, {
      attachTo: document.body,
      props: { card, isOpen: true, labels: [] },
    })

    await flushPromises()

    expect(wrapper.get('[data-testid="card-comments-load-error"]').text()).toContain(
      'Comments could not be loaded',
    )
    expect(wrapper.text()).toContain(cachedComment.content)
    expect(wrapper.find('[data-testid="card-comments-empty"]').exists()).toBe(false)

    wrapper.unmount()
  })

  it('keeps archived-card retry operable while comment mutations remain locked', async () => {
    fetchCardComments
      .mockRejectedValueOnce(new Error('offline'))
      .mockResolvedValueOnce([])

    const archivedCard: Card = { ...card, isArchived: true }
    const wrapper = mount(CardModal, {
      attachTo: document.body,
      props: { card: archivedCard, isOpen: true, labels: [] },
    })

    await flushPromises()

    const retry = wrapper.get<HTMLButtonElement>('[data-testid="card-comments-retry"]')
    expect(retry.element.closest('fieldset:disabled')).toBeNull()
    expect(wrapper.get<HTMLTextAreaElement>('#new-card-comment').attributes('disabled')).toBeDefined()

    await retry.trigger('click')
    await flushPromises()

    expect(fetchCardComments).toHaveBeenCalledTimes(2)
    expect(wrapper.get('[data-testid="card-comments-empty"]').text()).toContain('No comments yet')

    wrapper.unmount()
  })

  it('does not let a stale read from the previous card replace the current card state', async () => {
    const firstRead = createDeferred<CardComment[]>()
    const secondRead = createDeferred<CardComment[]>()
    fetchCardComments
      .mockReturnValueOnce(firstRead.promise)
      .mockReturnValueOnce(secondRead.promise)

    const wrapper = mount(CardModal, {
      attachTo: document.body,
      props: { card, isOpen: true, labels: [] },
    })
    await nextTick()

    const replacement: Card = {
      ...card,
      id: 'card-2',
      title: 'Replacement card',
      updatedAt: '2026-09-02T00:00:00Z',
    }
    await wrapper.setProps({ card: replacement })
    await nextTick()

    expect(fetchCardComments).toHaveBeenNthCalledWith(1, card.boardId, card.id)
    expect(fetchCardComments).toHaveBeenNthCalledWith(2, replacement.boardId, replacement.id)

    secondRead.resolve([])
    await flushPromises()
    expect(wrapper.get('[data-testid="card-comments-empty"]').text()).toContain('No comments yet')

    firstRead.reject(new Error('late failure'))
    await flushPromises()

    expect(wrapper.find('[data-testid="card-comments-load-error"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="card-comments-loading"]').exists()).toBe(false)
    expect(wrapper.get('[data-testid="card-comments-empty"]').text()).toContain('No comments yet')

    wrapper.unmount()
  })
})