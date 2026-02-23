import { describe, it, expect, beforeEach, vi } from 'vitest'
import { cardsApi } from '../../api/cardsApi'
import http from '../../api/http'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    patch: vi.fn(),
    delete: vi.fn(),
  },
}))

describe('cardsApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  describe('getCards', () => {
    it('should fetch cards for a board with no params', async () => {
      const mockCards = [{ id: 'card-1', title: 'Card 1' }]
      vi.mocked(http.get).mockResolvedValue({ data: mockCards })

      const result = await cardsApi.getCards('board-1')

      expect(http.get).toHaveBeenCalledWith('/boards/board-1/cards?')
      expect(result).toEqual(mockCards)
    })

    it('should fetch cards with search param', async () => {
      vi.mocked(http.get).mockResolvedValue({ data: [] })

      await cardsApi.getCards('board-1', { search: 'bug' })

      expect(http.get).toHaveBeenCalledWith('/boards/board-1/cards?search=bug')
    })

    it('should fetch cards with labelId param', async () => {
      vi.mocked(http.get).mockResolvedValue({ data: [] })

      await cardsApi.getCards('board-1', { labelId: 'label-1' })

      expect(http.get).toHaveBeenCalledWith('/boards/board-1/cards?labelId=label-1')
    })

    it('should fetch cards with columnId param', async () => {
      vi.mocked(http.get).mockResolvedValue({ data: [] })

      await cardsApi.getCards('board-1', { columnId: 'col-1' })

      expect(http.get).toHaveBeenCalledWith('/boards/board-1/cards?columnId=col-1')
    })

    it('should fetch cards with multiple params', async () => {
      vi.mocked(http.get).mockResolvedValue({ data: [] })

      await cardsApi.getCards('board-1', { search: 'test', labelId: 'label-1', columnId: 'col-1' })

      expect(http.get).toHaveBeenCalledWith(
        '/boards/board-1/cards?search=test&labelId=label-1&columnId=col-1'
      )
    })
  })

  describe('createCard', () => {
    it('should create a card with the provided data', async () => {
      const newCard = { id: 'card-1', title: 'New Card' }
      vi.mocked(http.post).mockResolvedValue({ data: newCard })

      const createData = { columnId: 'col-1', title: 'New Card', description: '' }
      const result = await cardsApi.createCard('board-1', createData)

      expect(http.post).toHaveBeenCalledWith('/boards/board-1/cards', createData)
      expect(result).toEqual(newCard)
    })
  })

  describe('updateCard', () => {
    it('should update a card with partial data', async () => {
      const updatedCard = { id: 'card-1', title: 'Updated Card' }
      vi.mocked(http.patch).mockResolvedValue({ data: updatedCard })

      const updateData = {
        title: 'Updated Card',
        description: null,
        dueDate: null,
        isBlocked: null,
        blockReason: null,
        labelIds: null,
        expectedUpdatedAt: '2026-01-01T00:00:00.000Z',
      }
      const result = await cardsApi.updateCard('board-1', 'card-1', updateData)

      expect(http.patch).toHaveBeenCalledWith('/boards/board-1/cards/card-1', updateData)
      expect(result).toEqual(updatedCard)
    })
  })

  describe('moveCard', () => {
    it('should move a card to a target column and position', async () => {
      const movedCard = { id: 'card-1', columnId: 'col-2', position: 0 }
      vi.mocked(http.post).mockResolvedValue({ data: movedCard })

      const moveData = { targetColumnId: 'col-2', targetPosition: 0 }
      const result = await cardsApi.moveCard('board-1', 'card-1', moveData)

      expect(http.post).toHaveBeenCalledWith('/boards/board-1/cards/card-1/move', moveData)
      expect(result).toEqual(movedCard)
    })
  })

  describe('deleteCard', () => {
    it('should delete a card by ID', async () => {
      vi.mocked(http.delete).mockResolvedValue({})

      await cardsApi.deleteCard('board-1', 'card-1')

      expect(http.delete).toHaveBeenCalledWith('/boards/board-1/cards/card-1')
    })
  })

  describe('getCardProvenance', () => {
    it('should fetch card provenance by board and card IDs', async () => {
      const provenance = {
        cardId: 'card-1',
        captureItemId: 'capture-1',
        proposalId: 'proposal-1',
        proposalStatus: 'Applied',
        triageRunId: 'triage-1',
      }
      vi.mocked(http.get).mockResolvedValue({ data: provenance })

      const result = await cardsApi.getCardProvenance('board-1', 'card-1')

      expect(http.get).toHaveBeenCalledWith('/boards/board-1/cards/card-1/provenance')
      expect(result).toEqual(provenance)
    })
  })
})
