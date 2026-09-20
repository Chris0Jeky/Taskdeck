import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'

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

function createState() {
  return {
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

describe('cardCommentStore visit and mutation ownership', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockCardCommentsApi.getComments.mockReset()
    mockCardCommentsApi.updateComment.mockReset()
  })

  it('does not let an earlier board visit invalidate the authoritative read after A to B to A', async () => {
    const state = createState()
    const helpers = createHelpers()
    const oldVisitUpdate = deferred<TestComment>()
    const reopenedRead = deferred<TestComment[]>()
    mockCardCommentsApi.updateComment.mockReturnValueOnce(oldVisitUpdate.promise)
    mockCardCommentsApi.getComments.mockReturnValueOnce(reopenedRead.promise)
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
    state.cardCommentsByCardId.value = {}
    const pendingRead = actions.fetchCardComments('board-1', 'card-1')

    oldVisitUpdate.resolve({
      ...originalComment,
      content: 'Old visit edit',
      updatedAt: '2026-09-20T10:01:00Z',
    })
    await pendingUpdate
    reopenedRead.resolve([{
      ...originalComment,
      content: 'Authoritative reopened value',
      updatedAt: '2026-09-20T10:02:00Z',
    }])
    await pendingRead

    expect(state.cardCommentsByCardId.value['card-1']).toEqual([{
      ...originalComment,
      content: 'Authoritative reopened value',
      updatedAt: '2026-09-20T10:02:00Z',
    }])
    expect(helpers.toast.success).not.toHaveBeenCalledWith('Comment updated')
  })

  it('does not let an older edit settle over a newer edit or invalidate its refresh', async () => {
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

    const secondResult = {
      ...originalComment,
      content: 'Second edit',
      updatedAt: '2026-09-20T10:02:00Z',
    }
    secondEdit.resolve(secondResult)
    await pendingSecond
    const pendingRead = actions.fetchCardComments('board-1', 'card-1')

    firstEdit.resolve({
      ...originalComment,
      content: 'First edit',
      updatedAt: '2026-09-20T10:01:00Z',
    })
    await pendingFirst
    authoritativeRead.resolve([secondResult])
    await pendingRead

    expect(state.cardCommentsByCardId.value['card-1']).toEqual([secondResult])
    expect(helpers.toast.success).toHaveBeenCalledTimes(1)
    expect(helpers.toast.success).toHaveBeenCalledWith('Comment updated')
  })
})
