import { describe, expect, it, vi, beforeEach } from 'vitest'
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

function createMockState() {
  return {
    cardCommentsByCardId: ref<Record<string, Array<{ id: string; createdAt: string }>>>({
      'card-1': [
        { id: 'cmt-1', createdAt: '2026-01-01T00:00:00Z' },
        { id: 'cmt-2', createdAt: '2026-01-02T00:00:00Z' },
      ],
    }),
    loading: ref(false),
    error: ref<string | null>(null),
  }
}

function createMockHelpers() {
  return {
    guardDemoMutation: vi.fn(),
    handleApiError: vi.fn(),
    isDemoMode: false,
    toast: { success: vi.fn(), error: vi.fn(), info: vi.fn() },
  }
}

describe('cardCommentStore', () => {
  let state: ReturnType<typeof createMockState>
  let helpers: ReturnType<typeof createMockHelpers>

  beforeEach(() => {
    vi.clearAllMocks()
    state = createMockState()
    helpers = createMockHelpers()
  })

  describe('getCardComments', () => {
    it('returns comments for existing card', () => {
      const { getCardComments } = createCardCommentActions(state as any, helpers as any)
      expect(getCardComments('card-1')).toHaveLength(2)
    })

    it('returns empty array for unknown card', () => {
      const { getCardComments } = createCardCommentActions(state as any, helpers as any)
      expect(getCardComments('card-unknown')).toEqual([])
    })
  })

  describe('fetchCardComments', () => {
    it('fetches and stores comments for a card', async () => {
      const comments = [
        { id: 'cmt-3', createdAt: '2026-02-01T00:00:00Z' },
      ]
      mockCardCommentsApi.getComments.mockResolvedValueOnce(comments)
      const { fetchCardComments } = createCardCommentActions(state as any, helpers as any)
      const result = await fetchCardComments('board-1', 'card-2')
      expect(result).toEqual(comments)
      expect(state.cardCommentsByCardId.value['card-2']).toEqual(comments)
      expect(state.cardCommentsByCardId.value['card-1']).toHaveLength(2)
    })

    it('returns empty in demo mode', async () => {
      helpers.isDemoMode = true
      const { fetchCardComments } = createCardCommentActions(state as any, helpers as any)
      const result = await fetchCardComments('board-1', 'card-1')
      expect(result).toEqual([])
      expect(mockCardCommentsApi.getComments).not.toHaveBeenCalled()
    })

    it('handles error and rethrows', async () => {
      mockCardCommentsApi.getComments.mockRejectedValueOnce(new Error('net'))
      const { fetchCardComments } = createCardCommentActions(state as any, helpers as any)
      await expect(fetchCardComments('board-1', 'card-1')).rejects.toThrow('net')
      expect(helpers.handleApiError).toHaveBeenCalledWith(expect.any(Error), 'Failed to fetch card comments')
    })
  })

  describe('createCardComment', () => {
    it('appends comment sorted by createdAt', async () => {
      const newComment = { id: 'cmt-new', createdAt: '2026-01-01T12:00:00Z' }
      mockCardCommentsApi.createComment.mockResolvedValueOnce(newComment)
      const { createCardComment } = createCardCommentActions(state as any, helpers as any)
      const result = await createCardComment('board-1', 'card-1', { content: 'hi' } as any)
      expect(result).toEqual(newComment)
      const comments = state.cardCommentsByCardId.value['card-1']
      expect(comments).toHaveLength(3)
      expect(comments[0].id).toBe('cmt-1')
      expect(comments[1].id).toBe('cmt-new')
      expect(comments[2].id).toBe('cmt-2')
      expect(helpers.toast.success).toHaveBeenCalledWith('Comment added')
      expect(state.loading.value).toBe(false)
    })

    it('sorts a new earliest comment before existing comments', async () => {
      const newComment = { id: 'cmt-earliest', createdAt: '2025-12-31T23:59:59Z' }
      mockCardCommentsApi.createComment.mockResolvedValueOnce(newComment)
      const { createCardComment } = createCardCommentActions(state as any, helpers as any)

      await createCardComment('board-1', 'card-1', { content: 'first' } as any)

      expect(state.cardCommentsByCardId.value['card-1'].map((comment) => comment.id)).toEqual([
        'cmt-earliest',
        'cmt-1',
        'cmt-2',
      ])
    })

    it('sorts a new latest comment after existing comments', async () => {
      const newComment = { id: 'cmt-latest', createdAt: '2026-01-03T00:00:00Z' }
      mockCardCommentsApi.createComment.mockResolvedValueOnce(newComment)
      const { createCardComment } = createCardCommentActions(state as any, helpers as any)

      await createCardComment('board-1', 'card-1', { content: 'last' } as any)

      expect(state.cardCommentsByCardId.value['card-1'].map((comment) => comment.id)).toEqual([
        'cmt-1',
        'cmt-2',
        'cmt-latest',
      ])
    })

    it('creates entry for card with no prior comments', async () => {
      const newComment = { id: 'cmt-first', createdAt: '2026-03-01T00:00:00Z' }
      mockCardCommentsApi.createComment.mockResolvedValueOnce(newComment)
      const { createCardComment } = createCardCommentActions(state as any, helpers as any)
      await createCardComment('board-1', 'card-new', { content: 'first' } as any)
      expect(state.cardCommentsByCardId.value['card-new']).toEqual([newComment])
    })

    it('guards demo mutation', async () => {
      helpers.guardDemoMutation.mockImplementation(() => { throw new Error('demo') })
      const { createCardComment } = createCardCommentActions(state as any, helpers as any)
      await expect(createCardComment('board-1', 'card-1', {} as any)).rejects.toThrow('demo')
    })

    it('handles error and rethrows', async () => {
      mockCardCommentsApi.createComment.mockRejectedValueOnce(new Error('fail'))
      const { createCardComment } = createCardCommentActions(state as any, helpers as any)
      await expect(createCardComment('board-1', 'card-1', {} as any)).rejects.toThrow('fail')
      expect(helpers.handleApiError).toHaveBeenCalled()
      expect(state.loading.value).toBe(false)
    })
  })

  describe('updateCardComment', () => {
    it('replaces comment in place', async () => {
      const updated = { id: 'cmt-1', createdAt: '2026-01-01T00:00:00Z', content: 'edited' }
      mockCardCommentsApi.updateComment.mockResolvedValueOnce(updated)
      const { updateCardComment } = createCardCommentActions(state as any, helpers as any)
      const result = await updateCardComment('board-1', 'card-1', 'cmt-1', { content: 'edited' } as any)
      expect(result).toEqual(updated)
      expect(state.cardCommentsByCardId.value['card-1'][0]).toEqual(updated)
      expect(state.cardCommentsByCardId.value['card-1']).toHaveLength(2)
      expect(helpers.toast.success).toHaveBeenCalledWith('Comment updated')
    })

    it('guards demo mutation', async () => {
      helpers.guardDemoMutation.mockImplementation(() => { throw new Error('demo') })
      const { updateCardComment } = createCardCommentActions(state as any, helpers as any)
      await expect(updateCardComment('board-1', 'card-1', 'cmt-1', {} as any)).rejects.toThrow('demo')
    })

    it('handles error and rethrows', async () => {
      mockCardCommentsApi.updateComment.mockRejectedValueOnce(new Error('upd'))
      const { updateCardComment } = createCardCommentActions(state as any, helpers as any)
      await expect(updateCardComment('board-1', 'card-1', 'cmt-1', {} as any)).rejects.toThrow('upd')
      expect(helpers.handleApiError).toHaveBeenCalled()
      expect(state.loading.value).toBe(false)
    })
  })

  describe('deleteCardComment', () => {
    it('removes comment from card', async () => {
      mockCardCommentsApi.deleteComment.mockResolvedValueOnce(undefined)
      const { deleteCardComment } = createCardCommentActions(state as any, helpers as any)
      await deleteCardComment('board-1', 'card-1', 'cmt-1')
      expect(state.cardCommentsByCardId.value['card-1']).toHaveLength(1)
      expect(state.cardCommentsByCardId.value['card-1'][0].id).toBe('cmt-2')
      expect(helpers.toast.success).toHaveBeenCalledWith('Comment deleted')
    })

    it('guards demo mutation', async () => {
      helpers.guardDemoMutation.mockImplementation(() => { throw new Error('demo') })
      const { deleteCardComment } = createCardCommentActions(state as any, helpers as any)
      await expect(deleteCardComment('board-1', 'card-1', 'cmt-1')).rejects.toThrow('demo')
    })

    it('handles error and rethrows', async () => {
      mockCardCommentsApi.deleteComment.mockRejectedValueOnce(new Error('del'))
      const { deleteCardComment } = createCardCommentActions(state as any, helpers as any)
      await expect(deleteCardComment('board-1', 'card-1', 'cmt-1')).rejects.toThrow('del')
      expect(helpers.handleApiError).toHaveBeenCalled()
      expect(state.loading.value).toBe(false)
    })
  })
})
