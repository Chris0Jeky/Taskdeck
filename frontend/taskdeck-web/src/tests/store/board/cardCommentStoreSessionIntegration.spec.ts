import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { cardCommentsApi } from '../../../api/cardCommentsApi'
import { useBoardStore } from '../../../store/boardStore'
import type { BoardDetail } from '../../../types/board'
import type { CardComment } from '../../../types/comments'

vi.mock('../../../api/cardCommentsApi')

const time = '2026-09-22T12:00:00Z'
const column = {
  id: 'column-a', boardId: 'board-a', name: 'Todo', position: 0,
  wipLimit: null, cardCount: 1, createdAt: time, updatedAt: time,
}
const boardA: BoardDetail = {
  id: 'board-a', name: 'Account A board', description: null, isArchived: false,
  createdAt: time, updatedAt: time, columns: [column],
}
const boardB: BoardDetail = { ...boardA, id: 'board-b', name: 'Other board' }
const commentA: CardComment = {
  id: 'comment-a', boardId: 'board-a', cardId: 'card-a', parentCommentId: null,
  authorUserId: 'account-a', authorUsername: 'account-a', content: 'Account A comment',
  isDeleted: false, editedAt: null, mentions: [], createdAt: time, updatedAt: time,
}

function deferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}

async function flushPromises() {
  await Promise.resolve()
  await Promise.resolve()
}

function installCommentCache(store: ReturnType<typeof useBoardStore>, comment: CardComment) {
  store.cardCommentsByCardId = { [comment.cardId]: [structuredClone(comment)] }
}

describe('card comment session integration', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    setActivePinia(createPinia())
  })

  it('rejects a queued same-comment write before transport across null logout and preserves the new error', async () => {
    const store = useBoardStore()
    const first = deferred<CardComment>()
    const secondTransportFailure = new Error('queued transport must not start')
    vi.mocked(cardCommentsApi.updateComment)
      .mockReturnValueOnce(first.promise)
      .mockRejectedValueOnce(secondTransportFailure)

    const firstCall = store.updateCardComment('board-a', 'card-a', 'comment-a', { content: 'First edit' })
    const secondCall = store.updateCardComment('board-a', 'card-a', 'comment-a', { content: 'Second edit' })
    const secondRejected = expect(secondCall).rejects.toMatchObject({ name: 'StaleBoardVisitError' })

    store.resetForLogout()
    expect(store.currentBoard).toBeNull()
    store.error = 'new-session-error'
    first.resolve(structuredClone(commentA))

    await expect(firstCall).resolves.toEqual(commentA)
    await secondRejected
    expect(cardCommentsApi.updateComment).toHaveBeenCalledTimes(1)
    expect(store.error).toBe('new-session-error')
  })

  it('keeps shared loading through queued same-comment writes until the final request settles', async () => {
    const store = useBoardStore()
    const first = deferred<CardComment>()
    const second = deferred<CardComment>()
    vi.mocked(cardCommentsApi.updateComment)
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(second.promise)

    const firstCall = store.updateCardComment('board-a', 'card-a', 'comment-a', { content: 'First edit' })
    const secondCall = store.updateCardComment('board-a', 'card-a', 'comment-a', { content: 'Second edit' })
    expect(store.loading).toBe(true)
    expect(cardCommentsApi.updateComment).toHaveBeenCalledTimes(1)

    first.resolve(structuredClone(commentA))
    await expect(firstCall).resolves.toEqual(commentA)
    await flushPromises()
    expect(cardCommentsApi.updateComment).toHaveBeenCalledTimes(2)
    expect(store.loading).toBe(true)

    const secondResult = { ...commentA, content: 'Second edit', updatedAt: '2026-09-22T12:01:00Z' }
    second.resolve(secondResult)
    await expect(secondCall).resolves.toEqual(secondResult)
    expect(store.loading).toBe(false)
  })

  it('does not publish an old A-to-B-to-A reconciliation after logout and a new same-id account', async () => {
    const store = useBoardStore()
    store.currentBoard = structuredClone(boardA)
    installCommentCache(store, commentA)
    const oldWrite = deferred<CardComment>()
    const oldReconciliation = deferred<CardComment[]>()
    vi.mocked(cardCommentsApi.updateComment).mockReturnValueOnce(oldWrite.promise)
    vi.mocked(cardCommentsApi.getComments).mockReturnValueOnce(oldReconciliation.promise)

    const write = store.updateCardComment('board-a', 'card-a', 'comment-a', { content: 'Old visit edit' })
    store.currentBoard = structuredClone(boardB)
    store.cardCommentsByCardId = {}
    store.currentBoard = structuredClone(boardA)
    const reopened = { ...commentA, content: 'Reopened account A value' }
    installCommentCache(store, reopened)

    const updated = { ...commentA, content: 'Old visit edit', updatedAt: '2026-09-22T12:01:00Z' }
    oldWrite.resolve(updated)
    await flushPromises()
    expect(cardCommentsApi.getComments).toHaveBeenCalledWith('board-a', 'card-a')

    store.resetForLogout()
    store.currentBoard = structuredClone(boardA)
    const newAccountComment = { ...commentA, content: 'New account value' }
    installCommentCache(store, newAccountComment)

    oldReconciliation.resolve([updated])
    await expect(write).resolves.toEqual(updated)
    expect(store.cardCommentsByCardId).toEqual({ 'card-a': [newAccountComment] })
  })
})
