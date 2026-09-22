import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useBoardStore } from '../../../store/boardStore'
import { useToastStore } from '../../../store/toastStore'
import { boardsApi } from '../../../api/boardsApi'
import { cardsApi } from '../../../api/cardsApi'
import { labelsApi } from '../../../api/labelsApi'
import { cardCommentsApi } from '../../../api/cardCommentsApi'
import type { BoardDetail, Card, Label } from '../../../types/board'
import type { CardComment } from '../../../types/comments'

vi.mock('../../../api/boardsApi')
vi.mock('../../../api/cardsApi')
vi.mock('../../../api/labelsApi')
vi.mock('../../../api/cardCommentsApi')

const time = '2026-09-22T12:00:00Z'
const board: BoardDetail = {
  id: 'board', name: 'Account A board', description: null, isArchived: false,
  createdAt: time, updatedAt: time,
  columns: [{ id: 'column', boardId: 'board', name: 'Todo', position: 0,
    wipLimit: null, cardCount: 1, createdAt: time, updatedAt: time }],
}
const card: Card = {
  id: 'card', boardId: 'board', columnId: 'column', title: 'Account A card',
  description: '', dueDate: null, isBlocked: false, blockReason: null,
  position: 0, labels: [], createdAt: time, updatedAt: time,
}
const label: Label = {
  id: 'label', boardId: 'board', name: 'Account A label', colorHex: '#123456',
  createdAt: time, updatedAt: time,
}
const comment: CardComment = {
  id: 'comment', boardId: 'board', cardId: 'card', parentCommentId: null,
  authorUserId: 'account-a', authorUsername: 'account-a', content: 'Account A comment',
  isDeleted: false, editedAt: null, mentions: [], createdAt: time, updatedAt: time,
}

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (error: unknown) => void
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}

type Store = ReturnType<typeof useBoardStore>
const operations = [
  { name: 'create board', api: boardsApi.createBoard, result: board,
    start: (s: Store) => s.createBoard({ name: 'Account A board' }) },
  { name: 'update board', api: boardsApi.updateBoard, result: board,
    start: (s: Store) => s.updateBoard('board', { name: 'Account A board' }) },
  { name: 'delete board', api: boardsApi.deleteBoard, result: undefined,
    start: (s: Store) => s.deleteBoard('board') },
  { name: 'create card', api: cardsApi.createCard, result: card,
    start: (s: Store) => s.createCard('board', { columnId: 'column', title: card.title }) },
  { name: 'update card', api: cardsApi.updateCard, result: card,
    start: (s: Store) => s.updateCard('board', 'card', { title: card.title }) },
  { name: 'delete card', api: cardsApi.deleteCard, result: undefined,
    start: (s: Store) => s.deleteCard('board', 'card') },
  { name: 'move card', api: cardsApi.moveCard, result: card,
    start: (s: Store) => s.moveCard('board', 'card', 'column', 0) },
  { name: 'archive card', api: cardsApi.setArchived, result: card,
    start: (s: Store) => s.setCardArchived('board', 'card', true, time) },
  { name: 'restore card', api: cardsApi.setArchived, result: card,
    start: (s: Store) => s.setCardArchived('board', 'card', false, time) },
  { name: 'create label', api: labelsApi.createLabel, result: label,
    start: (s: Store) => s.createLabel('board', { name: label.name, colorHex: label.colorHex }) },
  { name: 'update label', api: labelsApi.updateLabel, result: label,
    start: (s: Store) => s.updateLabel('board', 'label', { name: label.name }) },
  { name: 'delete label', api: labelsApi.deleteLabel, result: undefined,
    start: (s: Store) => s.deleteLabel('board', 'label') },
  { name: 'create comment', api: cardCommentsApi.createComment, result: comment,
    start: (s: Store) => s.createCardComment('board', 'card', { content: comment.content }) },
  { name: 'update comment', api: cardCommentsApi.updateComment, result: comment,
    start: (s: Store) => s.updateCardComment('board', 'card', 'comment', { content: comment.content }) },
  { name: 'delete comment', api: cardCommentsApi.deleteComment, result: undefined,
    start: (s: Store) => s.deleteCardComment('board', 'card', 'comment') },
]

function installNextSession(s: Store) {
  s.boards = [{ ...board, name: 'Account B board' }]
  s.currentBoard = structuredClone({ ...board, name: 'Account B board' })
  s.activeBoardId = 'board'
  // Keep the same resource IDs, including a child that would trigger a recovery GET.
  s.currentBoardCards = [{ ...card, title: 'Account B card' },
    { ...card, id: 'child', parentCardId: 'card', title: 'Account B child' }]
  s.currentBoardLabels = [{ ...label, name: 'Account B label' }]
  s.cardCommentsByCardId = { card: [{ ...comment, content: 'Account B comment' }] }
  s.loading = true
  s.error = 'Account B error'
}

function snapshot(s: Store) {
  return JSON.parse(JSON.stringify({ boards: s.boards, currentBoard: s.currentBoard,
    activeBoardId: s.activeBoardId, cards: s.currentBoardCards, labels: s.currentBoardLabels,
    comments: s.cardCommentsByCardId, loading: s.loading, error: s.error }))
}

describe('board mutation session ownership', () => {
  beforeEach(() => { vi.resetAllMocks(); setActivePinia(createPinia()) })

  it('retires caller ownership synchronously through the same logout boundary', () => {
    const s = useBoardStore()
    const ownsOriginalSession = s.captureSession()
    expect(ownsOriginalSession()).toBe(true)
    expect(s.currentBoard).toBeNull()
    s.resetForLogout()
    expect(ownsOriginalSession()).toBe(false)
    expect(s.captureSession()()).toBe(true)
  })

  for (const op of operations) {
    for (const replacement of ['logged out', 'next account'] as const) {
      it(`${op.name}: late success cannot change ${replacement} state`, async () => {
        const s = useBoardStore()
        const toast = useToastStore()
        const success = vi.spyOn(toast, 'success')
        const warning = vi.spyOn(toast, 'warning')
        const pending = deferred<unknown>()
        vi.mocked(op.api).mockReturnValueOnce(pending.promise as never)
        // No committed detail: logout must invalidate even null → null transitions.
        const call = op.start(s)
        s.resetForLogout()
        if (replacement === 'next account') installNextSession(s)
        const before = snapshot(s)
        const refs = [s.boards, s.currentBoard, s.currentBoardCards, s.currentBoardLabels, s.cardCommentsByCardId]
        pending.resolve(op.result)
        await expect(call).resolves.toEqual(op.result)
        expect(snapshot(s)).toEqual(before)
        expect([s.boards, s.currentBoard, s.currentBoardCards, s.currentBoardLabels, s.cardCommentsByCardId])
          .toEqual(refs)
        refs.forEach((ref, index) => expect([s.boards, s.currentBoard, s.currentBoardCards,
          s.currentBoardLabels, s.cardCommentsByCardId][index]).toBe(ref))
        expect(success).not.toHaveBeenCalled()
        expect(warning).not.toHaveBeenCalled()
        expect(boardsApi.getBoard).not.toHaveBeenCalled()
        expect(labelsApi.getLabels).not.toHaveBeenCalled()
      })
    }

    it(`${op.name}: late rejection reaches caller without changing next account`, async () => {
      const s = useBoardStore()
      const toast = useToastStore()
      const errorToast = vi.spyOn(toast, 'error')
      const pending = deferred<unknown>()
      vi.mocked(op.api).mockReturnValueOnce(pending.promise as never)
      const failure = { response: { status: 409 }, message: 'Account A conflict' }
      const call = op.start(s)
      const rejected = expect(call).rejects.toBe(failure)
      s.resetForLogout()
      installNextSession(s)
      const before = snapshot(s)
      pending.reject(failure)
      await rejected
      expect(snapshot(s)).toEqual(before)
      expect(errorToast).not.toHaveBeenCalled()
    })
  }

  it('drops a queued label change across logout with no committed board before either session', async () => {
    const s = useBoardStore()
    const pending = deferred<Label>()
    vi.mocked(labelsApi.updateLabel).mockReturnValueOnce(pending.promise)
    const first = s.updateLabel('board', 'label', { name: 'First' })
    const second = s.updateLabel('board', 'label', { name: 'Second' })
    const rejected = expect(second).rejects.toThrow('board visit')
    s.resetForLogout()
    pending.resolve(label)
    await first
    await rejected
    expect(labelsApi.updateLabel).toHaveBeenCalledTimes(1)
  })

  it('preserves same-session writes before board detail loads', async () => {
    const s = useBoardStore()
    vi.mocked(boardsApi.createBoard).mockResolvedValue(board)
    vi.mocked(cardsApi.createCard).mockResolvedValue(card)
    vi.mocked(labelsApi.createLabel).mockResolvedValue(label)
    vi.mocked(cardCommentsApi.createComment).mockResolvedValue(comment)
    await s.createBoard({ name: board.name })
    await s.createCard('board', { columnId: 'column', title: card.title })
    await s.createLabel('board', { name: label.name, colorHex: label.colorHex })
    await s.createCardComment('board', 'card', { content: comment.content })
    expect(s.currentBoard).toBeNull()
    expect(s.boards).toEqual([board])
    expect(s.currentBoardCards).toEqual([card])
    expect(s.currentBoardLabels).toEqual([label])
    expect(s.cardCommentsByCardId.card).toEqual([comment])
  })

  for (const op of operations.filter(({ name }) =>
    ['update board', 'update card', 'update label'].includes(name))) {
    it(`${op.name}: old success cannot invalidate a next-session detail read`, async () => {
      const s = useBoardStore()
      const mutation = deferred<unknown>()
      vi.mocked(op.api).mockReturnValueOnce(mutation.promise as never)
      const call = op.start(s)
      s.resetForLogout()
      const detail = deferred<BoardDetail>()
      vi.mocked(boardsApi.getBoard).mockReturnValueOnce(detail.promise)
      vi.mocked(cardsApi.getCards).mockResolvedValue([{ ...card, title: 'Account B card' }])
      vi.mocked(labelsApi.getLabels).mockResolvedValue([{ ...label, name: 'Account B label' }])
      const read = s.fetchBoard('board')
      mutation.resolve(op.result)
      await call
      expect(s.loading).toBe(true)
      detail.resolve(structuredClone({ ...board, name: 'Account B board' }))
      await expect(read).resolves.toBe(true)
      expect(s.currentBoard?.name).toBe('Account B board')
      expect(s.currentBoardCards[0]?.title).toBe('Account B card')
      expect(s.currentBoardLabels[0]?.name).toBe('Account B label')
      expect(boardsApi.getBoard).toHaveBeenCalledTimes(1)
    })
  }

  const reads = [
    { name: 'cards', api: cardsApi.getCards, result: [card],
      start: (s: Store) => s.fetchCards('board') },
    { name: 'comments', api: cardCommentsApi.getComments, result: [comment],
      start: (s: Store) => s.fetchCardComments('board', 'card') },
    { name: 'labels', api: labelsApi.getLabels, result: [label],
      start: (s: Store) => s.fetchLabels('board') },
    { name: 'provenance', api: cardsApi.getCardProvenance, result: null,
      start: (s: Store) => s.fetchCardProvenance('board', 'card') },
  ]
  for (const read of reads) {
    for (const outcome of ['success', 'failure'] as const) {
      it(`direct ${read.name} read: stale ${outcome} cannot publish across logout`, async () => {
        const s = useBoardStore()
        const errorToast = vi.spyOn(useToastStore(), 'error')
        const pending = deferred<unknown>()
        vi.mocked(read.api).mockReturnValueOnce(pending.promise as never)
        const call = read.start(s)
        const failure = new Error('Account A read failed')
        const rejected = outcome === 'failure' ? expect(call).rejects.toBe(failure) : null
        s.resetForLogout()
        installNextSession(s)
        const before = snapshot(s)
        if (outcome === 'failure') { pending.reject(failure); await rejected }
        else { pending.resolve(read.result); await call }
        expect(snapshot(s)).toEqual(before)
        expect(errorToast).not.toHaveBeenCalled()
      })
    }
  }
})
