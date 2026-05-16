import { describe, expect, it, vi, beforeEach } from 'vitest'
import { ref } from 'vue'

const { mockCardsApi } = vi.hoisted(() => ({
  mockCardsApi: {
    getCards: vi.fn(),
    createCard: vi.fn(),
    updateCard: vi.fn(),
    deleteCard: vi.fn(),
    moveCard: vi.fn(),
    getCardProvenance: vi.fn(),
  },
}))

vi.mock('../../../api/cardsApi', () => ({
  cardsApi: mockCardsApi,
}))

vi.mock('../../../utils/errorMessage', () => ({
  getErrorMessage: (err: unknown, fallback: string) => {
    const typed = err as { message?: string } | null
    return typed?.message ?? fallback
  },
}))

import { createCardActions } from '../../../store/board/cardStore'

function createMockState() {
  return {
    currentBoard: ref<{
      id: string
      columns: Array<{ id: string; name: string; cardCount: number }>
    } | null>({
      id: 'board-1',
      columns: [{ id: 'col-1', name: 'Todo', cardCount: 2 }],
    }),
    currentBoardCards: ref([
      {
        id: 'card-1',
        boardId: 'board-1',
        columnId: 'col-1',
        title: 'First',
        description: '',
        dueDate: null,
        isBlocked: false,
        blockReason: null,
        position: 0,
        labels: [],
        createdAt: '2024-01-01T00:00:00Z',
        updatedAt: '2024-01-02T00:00:00Z',
      },
      {
        id: 'card-2',
        boardId: 'board-1',
        columnId: 'col-1',
        title: 'Second',
        description: '',
        dueDate: null,
        isBlocked: false,
        blockReason: null,
        position: 1,
        labels: [],
        createdAt: '2024-01-01T00:00:00Z',
        updatedAt: '2024-01-03T00:00:00Z',
      },
    ]),
    cardCommentsByCardId: ref<Record<string, unknown>>({}),
    loading: ref(false),
    error: ref<string | null>(null),
  }
}

function createMockHelpers() {
  return {
    guardDemoMutation: vi.fn(),
    handleApiError: vi.fn(),
    isHttpConflict: vi.fn().mockReturnValue(false),
    isDemoMode: false,
    toast: { success: vi.fn(), error: vi.fn() },
    updateColumnCardCount: vi.fn(),
  }
}

describe('cardStore', () => {
  let state: ReturnType<typeof createMockState>
  let helpers: ReturnType<typeof createMockHelpers>

  beforeEach(() => {
    vi.clearAllMocks()
    state = createMockState()
    helpers = createMockHelpers()
  })

  describe('fetchCards', () => {
    it('fetches cards and updates state and column cardCount', async () => {
      const apiCards = [
        { id: 'card-a', columnId: 'col-1', title: 'A' },
        { id: 'card-b', columnId: 'col-1', title: 'B' },
        { id: 'card-c', columnId: 'col-1', title: 'C' },
      ]
      mockCardsApi.getCards.mockResolvedValueOnce(apiCards)
      const { fetchCards } = createCardActions(state as any, helpers as any)

      await fetchCards('board-1')

      expect(mockCardsApi.getCards).toHaveBeenCalledWith('board-1', undefined)
      expect(state.currentBoardCards.value).toEqual(apiCards)
      expect(state.currentBoard.value!.columns[0].cardCount).toBe(3)
    })

    it('passes filters to the API', async () => {
      mockCardsApi.getCards.mockResolvedValueOnce([])
      const { fetchCards } = createCardActions(state as any, helpers as any)

      await fetchCards('board-1', { search: 'test', labelId: 'lbl-1' })

      expect(mockCardsApi.getCards).toHaveBeenCalledWith('board-1', {
        search: 'test',
        labelId: 'lbl-1',
      })
    })

    it('skips fetch in demo mode', async () => {
      helpers.isDemoMode = true
      const { fetchCards } = createCardActions(state as any, helpers as any)

      await fetchCards('board-1')

      expect(mockCardsApi.getCards).not.toHaveBeenCalled()
    })

    it('handles error by calling handleApiError and rethrowing', async () => {
      mockCardsApi.getCards.mockRejectedValueOnce(new Error('network'))
      const { fetchCards } = createCardActions(state as any, helpers as any)

      await expect(fetchCards('board-1')).rejects.toThrow('network')
      expect(helpers.handleApiError).toHaveBeenCalledWith(
        expect.any(Error),
        'Failed to fetch cards',
      )
    })
  })

  describe('createCard', () => {
    it('creates card, appends to state, updates column count, and shows toast', async () => {
      const newCard = {
        id: 'card-new',
        boardId: 'board-1',
        columnId: 'col-1',
        title: 'New Card',
        description: '',
        position: 2,
        labels: [],
        createdAt: '2024-01-04T00:00:00Z',
        updatedAt: '2024-01-04T00:00:00Z',
      }
      mockCardsApi.createCard.mockResolvedValueOnce(newCard)
      const { createCard } = createCardActions(state as any, helpers as any)

      const result = await createCard('board-1', {
        title: 'New Card',
        columnId: 'col-1',
      } as any)

      expect(result).toEqual(newCard)
      expect(state.currentBoardCards.value).toHaveLength(3)
      expect(state.currentBoardCards.value[2]).toEqual(newCard)
      expect(helpers.updateColumnCardCount).toHaveBeenCalledWith('col-1', 1)
      expect(helpers.toast.success).toHaveBeenCalledWith(
        'Card "New Card" created successfully',
      )
      expect(state.loading.value).toBe(false)
    })

    it('guards demo mutation', async () => {
      helpers.guardDemoMutation.mockImplementation(() => {
        throw new Error('demo')
      })
      const { createCard } = createCardActions(state as any, helpers as any)

      await expect(
        createCard('board-1', { title: 'X', columnId: 'col-1' } as any),
      ).rejects.toThrow('demo')
      expect(mockCardsApi.createCard).not.toHaveBeenCalled()
    })

    it('handles error by calling handleApiError and rethrowing', async () => {
      mockCardsApi.createCard.mockRejectedValueOnce(new Error('create-fail'))
      const { createCard } = createCardActions(state as any, helpers as any)

      await expect(
        createCard('board-1', { title: 'X', columnId: 'col-1' } as any),
      ).rejects.toThrow('create-fail')
      expect(helpers.handleApiError).toHaveBeenCalledWith(
        expect.any(Error),
        'Failed to create card',
      )
      expect(state.loading.value).toBe(false)
    })
  })

  describe('updateCard', () => {
    it('finds and replaces card in array', async () => {
      const updatedCard = {
        id: 'card-1',
        boardId: 'board-1',
        columnId: 'col-1',
        title: 'Updated',
        description: 'new desc',
        position: 0,
        labels: [],
        createdAt: '2024-01-01T00:00:00Z',
        updatedAt: '2024-01-05T00:00:00Z',
      }
      mockCardsApi.updateCard.mockResolvedValueOnce(updatedCard)
      const { updateCard } = createCardActions(state as any, helpers as any)

      const result = await updateCard('board-1', 'card-1', {
        title: 'Updated',
        description: 'new desc',
      } as any)

      expect(result).toEqual(updatedCard)
      expect(state.currentBoardCards.value[0]).toEqual(updatedCard)
      expect(helpers.toast.success).toHaveBeenCalledWith('Card updated successfully')
      expect(state.loading.value).toBe(false)
    })

    it('uses expectedUpdatedAt from existing card if not provided', async () => {
      const updatedCard = { id: 'card-1', title: 'Updated' }
      mockCardsApi.updateCard.mockResolvedValueOnce(updatedCard)
      const { updateCard } = createCardActions(state as any, helpers as any)

      await updateCard('board-1', 'card-1', { title: 'Updated' } as any)

      expect(mockCardsApi.updateCard).toHaveBeenCalledWith('board-1', 'card-1', {
        title: 'Updated',
        expectedUpdatedAt: '2024-01-02T00:00:00Z',
      })
    })

    it('preserves explicit expectedUpdatedAt when provided', async () => {
      const updatedCard = { id: 'card-1', title: 'Updated' }
      mockCardsApi.updateCard.mockResolvedValueOnce(updatedCard)
      const { updateCard } = createCardActions(state as any, helpers as any)

      await updateCard('board-1', 'card-1', {
        title: 'Updated',
        expectedUpdatedAt: '2024-01-10T00:00:00Z',
      } as any)

      expect(mockCardsApi.updateCard).toHaveBeenCalledWith('board-1', 'card-1', {
        title: 'Updated',
        expectedUpdatedAt: '2024-01-10T00:00:00Z',
      })
    })

    it('handles 409 conflict by calling toast.error directly', async () => {
      const conflictError = new Error('Card was modified by another user')
      helpers.isHttpConflict.mockReturnValue(true)
      mockCardsApi.updateCard.mockRejectedValueOnce(conflictError)
      const { updateCard } = createCardActions(state as any, helpers as any)

      await expect(
        updateCard('board-1', 'card-1', { title: 'X' } as any),
      ).rejects.toThrow('Card was modified by another user')

      expect(helpers.isHttpConflict).toHaveBeenCalledWith(conflictError)
      expect(helpers.toast.error).toHaveBeenCalledWith(
        'Card was modified by another user',
      )
      expect(helpers.handleApiError).not.toHaveBeenCalled()
    })

    it('handles other errors via handleApiError', async () => {
      mockCardsApi.updateCard.mockRejectedValueOnce(new Error('server'))
      const { updateCard } = createCardActions(state as any, helpers as any)

      await expect(
        updateCard('board-1', 'card-1', { title: 'X' } as any),
      ).rejects.toThrow('server')
      expect(helpers.handleApiError).toHaveBeenCalledWith(
        expect.any(Error),
        'Failed to update card',
      )
      expect(state.loading.value).toBe(false)
    })
  })

  describe('deleteCard', () => {
    it('removes card from array, cleans up comments, and updates column count', async () => {
      state.cardCommentsByCardId.value = { 'card-1': [{ id: 'cmt-1' }] }
      mockCardsApi.deleteCard.mockResolvedValueOnce(undefined)
      const { deleteCard } = createCardActions(state as any, helpers as any)

      await deleteCard('board-1', 'card-1')

      expect(state.currentBoardCards.value).toHaveLength(1)
      expect(state.currentBoardCards.value[0].id).toBe('card-2')
      expect(state.cardCommentsByCardId.value).not.toHaveProperty('card-1')
      expect(helpers.updateColumnCardCount).toHaveBeenCalledWith('col-1', -1)
      expect(helpers.toast.success).toHaveBeenCalledWith('Card deleted successfully')
      expect(state.loading.value).toBe(false)
    })

    it('does not crash when card has no comments entry', async () => {
      mockCardsApi.deleteCard.mockResolvedValueOnce(undefined)
      const { deleteCard } = createCardActions(state as any, helpers as any)

      await deleteCard('board-1', 'card-2')

      expect(state.currentBoardCards.value).toHaveLength(1)
      expect(state.currentBoardCards.value[0].id).toBe('card-1')
    })

    it('guards demo mutation', async () => {
      helpers.guardDemoMutation.mockImplementation(() => {
        throw new Error('demo')
      })
      const { deleteCard } = createCardActions(state as any, helpers as any)

      await expect(deleteCard('board-1', 'card-1')).rejects.toThrow('demo')
      expect(mockCardsApi.deleteCard).not.toHaveBeenCalled()
    })

    it('handles error by calling handleApiError and rethrowing', async () => {
      mockCardsApi.deleteCard.mockRejectedValueOnce(new Error('delete-fail'))
      const { deleteCard } = createCardActions(state as any, helpers as any)

      await expect(deleteCard('board-1', 'card-1')).rejects.toThrow('delete-fail')
      expect(helpers.handleApiError).toHaveBeenCalledWith(
        expect.any(Error),
        'Failed to delete card',
      )
      expect(state.loading.value).toBe(false)
    })
  })

  describe('moveCard', () => {
    it('removes from old position and pushes updated card', async () => {
      const movedCard = {
        id: 'card-1',
        boardId: 'board-1',
        columnId: 'col-2',
        title: 'First',
        description: '',
        position: 0,
        labels: [],
        createdAt: '2024-01-01T00:00:00Z',
        updatedAt: '2024-01-06T00:00:00Z',
      }
      mockCardsApi.moveCard.mockResolvedValueOnce(movedCard)

      // Add col-2 to the board
      state.currentBoard.value!.columns.push({ id: 'col-2', name: 'Done', cardCount: 0 })

      const { moveCard } = createCardActions(state as any, helpers as any)

      const result = await moveCard('board-1', 'card-1', 'col-2', 0)

      expect(result).toEqual(movedCard)
      // card-1 should be at end of array (pushed after splice)
      expect(state.currentBoardCards.value[state.currentBoardCards.value.length - 1]).toEqual(
        movedCard,
      )
      expect(helpers.toast.success).toHaveBeenCalledWith('Card moved successfully')
      expect(state.loading.value).toBe(false)
    })

    it('updates column counts when column changed', async () => {
      const movedCard = {
        id: 'card-1',
        boardId: 'board-1',
        columnId: 'col-2',
        title: 'First',
        position: 0,
      }
      mockCardsApi.moveCard.mockResolvedValueOnce(movedCard)
      const { moveCard } = createCardActions(state as any, helpers as any)

      await moveCard('board-1', 'card-1', 'col-2', 0)

      expect(helpers.updateColumnCardCount).toHaveBeenCalledWith('col-1', -1)
      expect(helpers.updateColumnCardCount).toHaveBeenCalledWith('col-2', 1)
    })

    it('does not update column counts when same column', async () => {
      const movedCard = {
        id: 'card-1',
        boardId: 'board-1',
        columnId: 'col-1',
        title: 'First',
        position: 1,
      }
      mockCardsApi.moveCard.mockResolvedValueOnce(movedCard)
      const { moveCard } = createCardActions(state as any, helpers as any)

      await moveCard('board-1', 'card-1', 'col-1', 1)

      expect(helpers.updateColumnCardCount).not.toHaveBeenCalled()
    })

    it('guards demo mutation', async () => {
      helpers.guardDemoMutation.mockImplementation(() => {
        throw new Error('demo')
      })
      const { moveCard } = createCardActions(state as any, helpers as any)

      await expect(moveCard('board-1', 'card-1', 'col-2', 0)).rejects.toThrow('demo')
      expect(mockCardsApi.moveCard).not.toHaveBeenCalled()
    })

    it('handles error by calling handleApiError and rethrowing', async () => {
      mockCardsApi.moveCard.mockRejectedValueOnce(new Error('move-fail'))
      const { moveCard } = createCardActions(state as any, helpers as any)

      await expect(moveCard('board-1', 'card-1', 'col-2', 0)).rejects.toThrow(
        'move-fail',
      )
      expect(helpers.handleApiError).toHaveBeenCalledWith(
        expect.any(Error),
        'Failed to move card',
      )
      expect(state.loading.value).toBe(false)
    })
  })

  describe('fetchCardProvenance', () => {
    it('returns provenance data from the API', async () => {
      const provenance = {
        cardId: 'card-1',
        captureItemId: 'cap-1',
        proposalId: 'prop-1',
        proposalStatus: 'Approved' as const,
        triageRunId: null,
      }
      mockCardsApi.getCardProvenance.mockResolvedValueOnce(provenance)
      const { fetchCardProvenance } = createCardActions(state as any, helpers as any)

      const result = await fetchCardProvenance('board-1', 'card-1')

      expect(result).toEqual(provenance)
      expect(mockCardsApi.getCardProvenance).toHaveBeenCalledWith('board-1', 'card-1')
    })

    it('returns null in demo mode without calling API', async () => {
      helpers.isDemoMode = true
      const { fetchCardProvenance } = createCardActions(state as any, helpers as any)

      const result = await fetchCardProvenance('board-1', 'card-1')

      expect(result).toBeNull()
      expect(mockCardsApi.getCardProvenance).not.toHaveBeenCalled()
    })

    it('handles error by calling handleApiError and rethrowing', async () => {
      mockCardsApi.getCardProvenance.mockRejectedValueOnce(new Error('prov-fail'))
      const { fetchCardProvenance } = createCardActions(state as any, helpers as any)

      await expect(fetchCardProvenance('board-1', 'card-1')).rejects.toThrow(
        'prov-fail',
      )
      expect(helpers.handleApiError).toHaveBeenCalledWith(
        expect.any(Error),
        'Failed to fetch card provenance',
      )
    })
  })
})
