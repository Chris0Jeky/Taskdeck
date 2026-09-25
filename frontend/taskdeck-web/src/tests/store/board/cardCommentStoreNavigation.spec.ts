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

function navigateToNextBoard(state: ReturnType<typeof createState>) {
  const nextBoardCache = {
    'card-next': [{
      ...originalComment,
      id: 'cmt-next',
      content: 'Next board',
    }],
  }
  state.currentBoard.value = { id: 'board-2' }
  state.cardCommentsByCardId.value = nextBoardCache
  return nextBoardCache
}

describe('cardCommentStore late write ownership', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockCardCommentsApi.createComment.mockReset()
    mockCardCommentsApi.updateComment.mockReset()
    mockCardCommentsApi.deleteComment.mockReset()
  })

  it('does not append a create response after another board is selected', async () => {
    const state = createState()
    const helpers = createHelpers()
    const response = deferred<TestComment>()
    mockCardCommentsApi.createComment.mockReturnValueOnce(response.promise)
    const { createCardComment } = createCardCommentActions(state as never, helpers as never)

    const pendingCreate = createCardComment('board-1', 'card-1', { content: 'Created' })
    const nextBoardCache = navigateToNextBoard(state)
    const created = {
      ...originalComment,
      id: 'cmt-created',
      content: 'Created',
    }
    response.resolve(created)
    const result = await pendingCreate

    expect(result).toEqual(created)
    expect(state.cardCommentsByCardId.value).toEqual(nextBoardCache)
  })

  it('does not apply an update response after another board is selected', async () => {
    const state = createState()
    const helpers = createHelpers()
    const response = deferred<TestComment>()
    mockCardCommentsApi.updateComment.mockReturnValueOnce(response.promise)
    const { updateCardComment } = createCardCommentActions(state as never, helpers as never)

    const pendingUpdate = updateCardComment('board-1', 'card-1', 'cmt-1', {
      content: 'Edited',
    })
    const nextBoardCache = navigateToNextBoard(state)
    response.resolve({
      ...originalComment,
      content: 'Edited',
      updatedAt: '2026-09-20T10:02:00Z',
    })
    await pendingUpdate

    expect(state.cardCommentsByCardId.value).toEqual(nextBoardCache)
  })

  it('does not apply a delete response after another board is selected', async () => {
    const state = createState()
    const helpers = createHelpers()
    const response = deferred<void>()
    mockCardCommentsApi.deleteComment.mockReturnValueOnce(response.promise)
    const { deleteCardComment } = createCardCommentActions(state as never, helpers as never)

    const pendingDelete = deleteCardComment('board-1', 'card-1', 'cmt-1')
    const nextBoardCache = navigateToNextBoard(state)
    response.resolve(undefined)
    await pendingDelete

    expect(state.cardCommentsByCardId.value).toEqual(nextBoardCache)
  })
})
