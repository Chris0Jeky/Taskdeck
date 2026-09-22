import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { boardsApi } from '../../../api/boardsApi'
import { cardsApi } from '../../../api/cardsApi'
import { cardCommentsApi } from '../../../api/cardCommentsApi'
import { columnsApi } from '../../../api/columnsApi'
import { labelsApi } from '../../../api/labelsApi'
import { useBoardStore } from '../../../store/boardStore'
import type { Board, BoardDetail, Card, Column, Label } from '../../../types/board'
import type { CardComment } from '../../../types/comments'

vi.mock('../../../api/boardsApi')
vi.mock('../../../api/cardsApi')
vi.mock('../../../api/cardCommentsApi')
vi.mock('../../../api/columnsApi')
vi.mock('../../../api/labelsApi')

const time = '2026-09-22T12:00:00Z'

const columnA: Column = {
  id: 'column-a', boardId: 'board-a', name: 'Todo', position: 0,
  wipLimit: null, cardCount: 1, createdAt: time, updatedAt: time,
}
const columnB: Column = { ...columnA, id: 'column-b', boardId: 'board-b' }
const boardA: BoardDetail = {
  id: 'board-a', name: 'Account A board', description: null, isArchived: false,
  createdAt: time, updatedAt: time, columns: [columnA],
}
const boardB: BoardDetail = {
  ...boardA, id: 'board-b', name: 'Account B board', columns: [columnB],
}
const cardA: Card = {
  id: 'card-a', boardId: 'board-a', columnId: 'column-a', title: 'A card',
  description: '', dueDate: null, isBlocked: false, blockReason: null,
  position: 0, labels: [], createdAt: time, updatedAt: time,
}
const cardB: Card = { ...cardA, id: 'card-b', boardId: 'board-b', columnId: 'column-b', title: 'B card' }
const labelA: Label = {
  id: 'label-a', boardId: 'board-a', name: 'A label', colorHex: '#123456',
  createdAt: time, updatedAt: time,
}
const labelB: Label = { ...labelA, id: 'label-b', boardId: 'board-b', name: 'B label' }
const commentA: CardComment = {
  id: 'comment-a', boardId: 'board-a', cardId: 'card-a', parentCommentId: null,
  authorUserId: 'account-a', authorUsername: 'account-a', content: 'A comment',
  isDeleted: false, editedAt: null, mentions: [], createdAt: time, updatedAt: time,
}
const commentB: CardComment = { ...commentA, id: 'comment-b', boardId: 'board-b', cardId: 'card-b', content: 'B comment' }

function deferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}

type Store = ReturnType<typeof useBoardStore>

function installBoardA(store: Store) {
  store.currentBoard = structuredClone(boardA)
  store.currentBoardCards = [structuredClone(cardA)]
  store.currentBoardLabels = [structuredClone(labelA)]
  store.cardCommentsByCardId = { 'card-a': [structuredClone(commentA)] }
}

function startBoardBDetail(store: Store) {
  const board = deferred<BoardDetail>()
  const cards = deferred<Card[]>()
  const labels = deferred<Label[]>()
  vi.mocked(boardsApi.getBoard).mockReturnValueOnce(board.promise)
  vi.mocked(cardsApi.getCards).mockReturnValueOnce(cards.promise)
  vi.mocked(labelsApi.getLabels).mockReturnValueOnce(labels.promise)
  const read = store.fetchBoard('board-b')
  return { read, board, cards, labels }
}

const detailMutationCases = [
  {
    name: 'board',
    api: boardsApi.updateBoard,
    result: boardA,
    start: (store: Store) => store.updateBoard('board-a', { name: 'A board' }),
  },
  {
    name: 'card',
    api: cardsApi.updateCard,
    result: cardA,
    start: (store: Store) => store.updateCard('board-a', 'card-a', { title: 'A card' }),
  },
  {
    name: 'label',
    api: labelsApi.updateLabel,
    result: labelA,
    start: (store: Store) => store.updateLabel('board-a', 'label-a', { name: 'A label' }),
  },
  {
    name: 'comment',
    api: cardCommentsApi.updateComment,
    result: commentA,
    start: (store: Store) => store.updateCardComment('board-a', 'card-a', 'comment-a', { content: 'A comment' }),
  },
  {
    name: 'column',
    api: columnsApi.updateColumn,
    result: columnA,
    start: (store: Store) => store.updateColumn('board-a', 'column-a', { name: 'Todo' }),
  },
]

describe('board loading ownership', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    setActivePinia(createPinia())
  })

  for (const mutation of detailMutationCases) {
    it(`${mutation.name} mutation cannot clear loading owned by a newer board detail read`, async () => {
      const store = useBoardStore()
      installBoardA(store)
      const pendingMutation = deferred<unknown>()
      vi.mocked(mutation.api).mockReturnValueOnce(pendingMutation.promise as never)

      const call = mutation.start(store)
      expect(store.loading).toBe(true)
      const detail = startBoardBDetail(store)

      pendingMutation.resolve(mutation.result)
      await expect(call).resolves.toEqual(mutation.result)
      expect(store.loading).toBe(true)

      detail.board.resolve(structuredClone(boardB))
      detail.cards.resolve([structuredClone(cardB)])
      detail.labels.resolve([structuredClone(labelB)])
      await expect(detail.read).resolves.toBe(true)
      expect(store.loading).toBe(false)
    })
  }

  for (const outcome of ['success', 'rejection'] as const) {
    it(`a ${outcome} from one mutation module cannot clear another module's loading owner`, async () => {
      const store = useBoardStore()
      installBoardA(store)
      const first = deferred<Card>()
      const second = deferred<CardComment>()
      vi.mocked(cardsApi.updateCard).mockReturnValueOnce(first.promise)
      vi.mocked(cardCommentsApi.createComment).mockReturnValueOnce(second.promise)

      const firstCall = store.updateCard('board-a', 'card-a', { title: 'A card' })
      const secondCall = store.createCardComment('board-a', 'card-a', { content: 'B comment' })
      expect(store.loading).toBe(true)

      if (outcome === 'success') {
        first.resolve(structuredClone(cardA))
        await expect(firstCall).resolves.toEqual(cardA)
      } else {
        const failure = new Error('first mutation failed')
        const rejected = expect(firstCall).rejects.toBe(failure)
        first.reject(failure)
        await rejected
      }
      expect(store.loading).toBe(true)

      second.resolve(structuredClone(commentB))
      await expect(secondCall).resolves.toEqual(commentB)
      expect(store.loading).toBe(false)
    })
  }

  for (const outcome of ['success', 'rejection'] as const) {
    it(`logout retires an old ${outcome} without clearing a new deferred operation`, async () => {
      const store = useBoardStore()
      const old = deferred<BoardDetail>()
      vi.mocked(boardsApi.updateBoard).mockReturnValueOnce(old.promise as never)
      const oldCall = store.updateBoard('board-a', { name: 'Old account' })

      store.resetForLogout()
      expect(store.loading).toBe(false)

      const current = deferred<typeof boardB>()
      vi.mocked(boardsApi.updateBoard).mockReturnValueOnce(current.promise)
      const currentCall = store.updateBoard('board-b', { name: 'Current account' })
      expect(store.loading).toBe(true)

      if (outcome === 'success') {
        old.resolve(structuredClone(boardA))
        await expect(oldCall).resolves.toEqual(boardA)
      } else {
        const failure = new Error('old account failed')
        const rejected = expect(oldCall).rejects.toBe(failure)
        old.reject(failure)
        await rejected
      }
      expect(store.loading).toBe(true)

      current.resolve(structuredClone(boardB))
      await expect(currentCall).resolves.toEqual(boardB)
      expect(store.loading).toBe(false)
    })
  }

  for (const firstKind of ['unfiltered', 'filtered'] as const) {
    it(`keeps loading while the ${firstKind} and the other board list read overlap`, async () => {
      const store = useBoardStore()
      const first = deferred<Board[]>()
      const second = deferred<Board[]>()
      vi.mocked(boardsApi.getBoards)
        .mockReturnValueOnce(first.promise)
        .mockReturnValueOnce(second.promise)

      const firstRead = firstKind === 'unfiltered'
        ? store.fetchBoards(undefined, false, { force: true })
        : store.fetchBoards('account', false, { force: true })
      const secondRead = firstKind === 'unfiltered'
        ? store.fetchBoards('account', false, { force: true })
        : store.fetchBoards(undefined, false, { force: true })
      expect(store.loading).toBe(true)

      first.resolve([structuredClone(boardA)])
      await expect(firstRead).resolves.toBeUndefined()
      expect(store.loading).toBe(true)

      second.resolve([structuredClone(boardB)])
      await expect(secondRead).resolves.toBeUndefined()
      expect(store.loading).toBe(false)
    })
  }
})
