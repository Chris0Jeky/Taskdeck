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
    toast: { success: vi.fn(), error: vi.fn(), info: vi.fn() },
  }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((settle) => {
    resolve = settle
  })
  return { promise, resolve }
}

describe('cardCommentStore cache ordering', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockCardCommentsApi.getComments.mockReset()
    mockCardCommentsApi.createComment.mockReset()
    mockCardCommentsApi.updateComment.mockReset()
    mockCardCommentsApi.deleteComment.mockReset()
  })

  it('does not let a fetch that started first erase a created comment', async () => {
    const state = createState()
    const helpers = createHelpers()
    const staleRead = deferred<TestComment[]>()
    mockCardCommentsApi.getComments.mockReturnValueOnce(staleRead.promise)
    mockCardCommentsApi.createComment.mockResolvedValueOnce({ ...secondComment })
    const actions = createCardCommentActions(state as never, helpers as never)

    const pendingRead = actions.fetchCardComments('board-1', 'card-1')
    await actions.createCardComment('board-1', 'card-1', { content: 'Second' })
    staleRead.resolve([{ ...originalComment }])
    await pendingRead

    expect(state.cardCommentsByCardId.value['card-1'].map(comment => comment.id)).toEqual([
      'cmt-1',
      'cmt-2',
    ])
  })

  it('does not let a fetch that started first erase an updated comment', async () => {
    const state = createState()
    const helpers = createHelpers()
    const staleRead = deferred<TestComment[]>()
    mockCardCommentsApi.getComments.mockReturnValueOnce(staleRead.promise)
    mockCardCommentsApi.updateComment.mockResolvedValueOnce({
      ...originalComment,
      content: 'Edited',
      updatedAt: '2026-09-20T10:02:00Z',
    })
    const actions = createCardCommentActions(state as never, helpers as never)

    const pendingRead = actions.fetchCardComments('board-1', 'card-1')
    await actions.updateCardComment('board-1', 'card-1', 'cmt-1', { content: 'Edited' })
    staleRead.resolve([{ ...originalComment }])
    await pendingRead

    expect(state.cardCommentsByCardId.value['card-1'][0].content).toBe('Edited')
  })

  it('does not let a fetch that started first restore a deleted comment', async () => {
    const state = createState()
    state.cardCommentsByCardId.value['card-1'].push({ ...secondComment })
    const helpers = createHelpers()
    const staleRead = deferred<TestComment[]>()
    mockCardCommentsApi.getComments.mockReturnValueOnce(staleRead.promise)
    mockCardCommentsApi.deleteComment.mockResolvedValueOnce(undefined)
    const actions = createCardCommentActions(state as never, helpers as never)

    const pendingRead = actions.fetchCardComments('board-1', 'card-1')
    await actions.deleteCardComment('board-1', 'card-1', 'cmt-1')
    staleRead.resolve([{ ...originalComment }, { ...secondComment }])
    await pendingRead

    expect(state.cardCommentsByCardId.value['card-1'].map(comment => comment.id)).toEqual([
      'cmt-2',
    ])
  })

  it('lets only the latest overlapping fetch commit for a card', async () => {
    const state = createState()
    const helpers = createHelpers()
    const firstRead = deferred<TestComment[]>()
    const secondRead = deferred<TestComment[]>()
    mockCardCommentsApi.getComments
      .mockReturnValueOnce(firstRead.promise)
      .mockReturnValueOnce(secondRead.promise)
    const actions = createCardCommentActions(state as never, helpers as never)

    const pendingFirstRead = actions.fetchCardComments('board-1', 'card-1')
    const pendingSecondRead = actions.fetchCardComments('board-1', 'card-1')
    secondRead.resolve([{ ...secondComment }])
    await pendingSecondRead
    firstRead.resolve([{ ...originalComment }])
    const firstResult = await pendingFirstRead

    expect(firstResult).toEqual([{ ...originalComment }])
    expect(state.cardCommentsByCardId.value['card-1']).toEqual([{ ...secondComment }])
  })

  it('does not repopulate the prior board cache after navigation', async () => {
    const state = createState()
    const helpers = createHelpers()
    const staleRead = deferred<TestComment[]>()
    mockCardCommentsApi.getComments.mockReturnValueOnce(staleRead.promise)
    const actions = createCardCommentActions(state as never, helpers as never)

    const pendingRead = actions.fetchCardComments('board-1', 'card-1')
    const nextBoardCache = {
      'card-next': [{ ...secondComment, id: 'cmt-next' }],
    }
    state.currentBoard.value = { id: 'board-2' }
    state.cardCommentsByCardId.value = nextBoardCache
    staleRead.resolve([{ ...originalComment }])
    await pendingRead

    expect(state.cardCommentsByCardId.value).toEqual(nextBoardCache)
    expect(state.cardCommentsByCardId.value).not.toHaveProperty('card-1')
  })

  it('preserves a fresher stable-id comment that a refresh committed before create settles', async () => {
    const state = createState()
    const helpers = createHelpers()
    const pendingCreateResponse = deferred<TestComment>()
    mockCardCommentsApi.createComment.mockReturnValueOnce(pendingCreateResponse.promise)
    const actions = createCardCommentActions(state as never, helpers as never)

    const pendingCreate = actions.createCardComment('board-1', 'card-1', {
      content: 'Second',
    })
    const refreshed = {
      ...secondComment,
      content: 'Second from authoritative refresh',
      updatedAt: '2026-09-20T10:03:00Z',
    }
    state.cardCommentsByCardId.value['card-1'].push(refreshed)
    pendingCreateResponse.resolve({ ...secondComment })
    await pendingCreate

    expect(
      state.cardCommentsByCardId.value['card-1'].filter(comment => comment.id === 'cmt-2'),
    ).toEqual([refreshed])
  })
})
