import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'
import { createBoardState } from '../../../store/board/boardState'

const { mockCardCommentsApi } = vi.hoisted(() => ({
  mockCardCommentsApi: {
    getComments: vi.fn(),
    createComment: vi.fn(),
    updateComment: vi.fn(),
    deleteComment: vi.fn(),
  },
}))

vi.mock('../../../api/cardCommentsApi', () => ({
  cardCommentsApi: mockCardCommentsApi,
}))

import { createCardCommentActions } from '../../../store/board/cardCommentStore'

interface TestComment {
  id: string
  content: string
  createdAt: string
  updatedAt: string
}

const originalComment: TestComment = {
  id: 'cmt-1',
  content: 'Original',
  createdAt: '2026-09-20T10:00:00Z',
  updatedAt: '2026-09-20T10:00:00Z',
}

const secondComment: TestComment = {
  id: 'cmt-2',
  content: 'Second',
  createdAt: '2026-09-20T10:01:00Z',
  updatedAt: '2026-09-20T10:01:00Z',
}

function createState() {
  return {
    ...createBoardState(),
    currentBoard: ref<{ id: string } | null>({ id: 'board-1' }),
    cardCommentsByCardId: ref<Record<string, TestComment[]>>({
      'card-1': [{ ...originalComment }],
    }),
    loading: ref(false),
    error: ref<string | null>(null),
  }
}

function createHelpers() {
  return {
    guardDemoMutation: vi.fn(),
    handleApiError: vi.fn(),
    isDemoMode: false,
    toast: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
  }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((settle) => {
    resolve = settle
  })
  return { promise, resolve }
}

async function flushPromises() {
  await Promise.resolve()
  await Promise.resolve()
}

describe('cardCommentStore visit and mutation ownership', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockCardCommentsApi.getComments.mockReset()
    mockCardCommentsApi.createComment.mockReset()
    mockCardCommentsApi.updateComment.mockReset()
    mockCardCommentsApi.deleteComment.mockReset()
  })

  it('patches the current same-board cache when a detail refresh replaces it before update settlement', async () => {
    const state = createState()
    const helpers = createHelpers()
    const update = deferred<TestComment>()
    mockCardCommentsApi.updateComment.mockReturnValueOnce(update.promise)
    const actions = createCardCommentActions(state as never, helpers as never)

    const pendingUpdate = actions.updateCardComment(
      'board-1',
      'card-1',
      'cmt-1',
      { content: 'Edited' },
    )

    const refreshedCache = {
      'card-1': [{ ...originalComment, content: 'Pre-write refresh' }],
    }
    state.cardCommentsByCardId.value = refreshedCache
    const installedCache = state.cardCommentsByCardId.value
    const updated = {
      ...originalComment,
      content: 'Edited',
      updatedAt: '2026-09-20T10:02:00Z',
    }
    update.resolve(updated)
    await pendingUpdate

    expect(state.cardCommentsByCardId.value).toBe(installedCache)
    expect(state.cardCommentsByCardId.value['card-1']).toEqual([updated])
    expect(mockCardCommentsApi.getComments).not.toHaveBeenCalled()
  })

  it('patches the current same-board cache when a detail refresh replaces it before create settlement', async () => {
    const state = createState()
    const helpers = createHelpers()
    const create = deferred<TestComment>()
    mockCardCommentsApi.createComment.mockReturnValueOnce(create.promise)
    const actions = createCardCommentActions(state as never, helpers as never)

    const pendingCreate = actions.createCardComment('board-1', 'card-1', {
      content: 'Second',
    })

    const refreshedCache = {
      'card-1': [{ ...originalComment, content: 'Pre-write refresh' }],
    }
    state.cardCommentsByCardId.value = refreshedCache
    const installedCache = state.cardCommentsByCardId.value
    create.resolve({ ...secondComment })
    await pendingCreate

    expect(state.cardCommentsByCardId.value).toBe(installedCache)
    expect(state.cardCommentsByCardId.value['card-1'].map(comment => comment.id)).toEqual([
      'cmt-1',
      'cmt-2',
    ])
  })

  it('patches the current same-board cache when a detail refresh replaces it before delete settlement', async () => {
    const state = createState()
    state.cardCommentsByCardId.value['card-1'].push({ ...secondComment })
    const helpers = createHelpers()
    const deletion = deferred<void>()
    mockCardCommentsApi.deleteComment.mockReturnValueOnce(deletion.promise)
    const actions = createCardCommentActions(state as never, helpers as never)

    const pendingDelete = actions.deleteCardComment('board-1', 'card-1', 'cmt-1')

    const refreshedCache = {
      'card-1': [{ ...originalComment }, { ...secondComment }],
    }
    state.cardCommentsByCardId.value = refreshedCache
    const installedCache = state.cardCommentsByCardId.value
    deletion.resolve(undefined)
    await pendingDelete

    expect(state.cardCommentsByCardId.value).toBe(installedCache)
    expect(state.cardCommentsByCardId.value['card-1'].map(comment => comment.id)).toEqual([
      'cmt-2',
    ])
  })

  it('reconciles a successful old-visit write into the currently reopened same board', async () => {
    const state = createState()
    const helpers = createHelpers()
    const oldVisitUpdate = deferred<TestComment>()
    const reconciliation = deferred<TestComment[]>()
    mockCardCommentsApi.updateComment.mockReturnValueOnce(oldVisitUpdate.promise)
    mockCardCommentsApi.getComments.mockReturnValueOnce(reconciliation.promise)
    const actions = createCardCommentActions(state as never, helpers as never)

    const pendingUpdate = actions.updateCardComment(
      'board-1',
      'card-1',
      'cmt-1',
      { content: 'Old visit edit' },
    )

    state.currentBoard.value = { id: 'board-2' }
    state.cardCommentsByCardId.value = {}
    state.currentBoard.value = { id: 'board-1' }
    state.cardCommentsByCardId.value = {
      'card-1': [{ ...originalComment, content: 'Reopened pre-write value' }],
    }
    const reopenedCache = state.cardCommentsByCardId.value

    const updated = {
      ...originalComment,
      content: 'Old visit edit',
      updatedAt: '2026-09-20T10:01:00Z',
    }
    oldVisitUpdate.resolve(updated)
    await flushPromises()
    expect(mockCardCommentsApi.getComments).toHaveBeenCalledWith('board-1', 'card-1')

    reconciliation.resolve([updated])
    await pendingUpdate

    expect(state.cardCommentsByCardId.value).toBe(reopenedCache)
    expect(state.cardCommentsByCardId.value['card-1']).toEqual([updated])
    expect(helpers.toast.success).not.toHaveBeenCalledWith('Comment updated')
  })

  it('does not start a queued comment write after the board session has ended', async () => {
    const state = createState()
    const helpers = createHelpers()
    const firstEdit = deferred<TestComment>()
    mockCardCommentsApi.updateComment
      .mockReturnValueOnce(firstEdit.promise)
      .mockResolvedValueOnce({
        ...originalComment,
        content: 'Second edit',
        updatedAt: '2026-09-20T10:02:00Z',
      })
    const actions = createCardCommentActions(state as never, helpers as never)

    const pendingFirst = actions.updateCardComment(
      'board-1',
      'card-1',
      'cmt-1',
      { content: 'First edit' },
    )
    const pendingSecond = actions
      .updateCardComment(
        'board-1',
        'card-1',
        'cmt-1',
        { content: 'Second edit' },
      )
      .catch(error => error as Error)

    await flushPromises()
    expect(mockCardCommentsApi.updateComment).toHaveBeenCalledTimes(1)

    state.currentBoard.value = null
    state.cardCommentsByCardId.value = {}
    firstEdit.resolve({
      ...originalComment,
      content: 'First edit',
      updatedAt: '2026-09-20T10:01:00Z',
    })
    await pendingFirst
    const cancellation = await pendingSecond

    expect(cancellation).toBeInstanceOf(Error)
    expect((cancellation as Error).name).toBe('StaleBoardVisitError')
    expect(mockCardCommentsApi.updateComment).toHaveBeenCalledTimes(1)
    expect(helpers.handleApiError).not.toHaveBeenCalled()
    expect(state.cardCommentsByCardId.value).toEqual({})
  })

  it('serializes overlapping edits so the later intent commits last and owns the cache', async () => {
    const state = createState()
    const helpers = createHelpers()
    const firstEdit = deferred<TestComment>()
    const secondEdit = deferred<TestComment>()
    const authoritativeRead = deferred<TestComment[]>()
    mockCardCommentsApi.updateComment
      .mockReturnValueOnce(firstEdit.promise)
      .mockReturnValueOnce(secondEdit.promise)
    mockCardCommentsApi.getComments.mockReturnValueOnce(authoritativeRead.promise)
    const actions = createCardCommentActions(state as never, helpers as never)

    const pendingFirst = actions.updateCardComment(
      'board-1',
      'card-1',
      'cmt-1',
      { content: 'First edit' },
    )
    const pendingSecond = actions.updateCardComment(
      'board-1',
      'card-1',
      'cmt-1',
      { content: 'Second edit' },
    )

    await flushPromises()
    expect(mockCardCommentsApi.updateComment).toHaveBeenCalledTimes(1)

    const firstResult = {
      ...originalComment,
      content: 'First edit',
      updatedAt: '2026-09-20T10:01:00Z',
    }
    firstEdit.resolve(firstResult)
    await pendingFirst
    await flushPromises()
    expect(mockCardCommentsApi.updateComment).toHaveBeenCalledTimes(2)
    expect(state.cardCommentsByCardId.value['card-1']).toEqual([firstResult])

    const pendingRead = actions.fetchCardComments('board-1', 'card-1')
    const secondResult = {
      ...originalComment,
      content: 'Second edit',
      updatedAt: '2026-09-20T10:02:00Z',
    }
    secondEdit.resolve(secondResult)
    await pendingSecond
    authoritativeRead.resolve([secondResult])
    await pendingRead

    expect(state.cardCommentsByCardId.value['card-1']).toEqual([secondResult])
    expect(helpers.toast.success).toHaveBeenCalledTimes(2)
    expect(helpers.toast.success).toHaveBeenNthCalledWith(1, 'Comment updated')
    expect(helpers.toast.success).toHaveBeenNthCalledWith(2, 'Comment updated')
  })
})
