/**
 * Card operations: fetch, create, update, delete, move cards, and provenance.
 */
import { cardsApi } from '../../api/cardsApi'
import { getErrorMessage } from '../../utils/errorMessage'
import type { CreateCardDto, UpdateCardDto, CardCaptureProvenance } from '../../types/board'
import type { BoardState } from './boardState'
import type { BoardHelpers } from './boardStoreHelpers'

export function createCardActions(state: BoardState, helpers: BoardHelpers) {
  async function fetchCards(
    boardId: string,
    filters?: { search?: string; labelId?: string; columnId?: string },
  ) {
    if (helpers.isDemoMode) return
    try {
      state.currentBoardCards.value = await cardsApi.getCards(boardId, filters)

      // Keep column card counts in sync with the latest cards collection
      if (state.currentBoard.value) {
        const counts = state.currentBoardCards.value.reduce((map, card) => {
          map.set(card.columnId, (map.get(card.columnId) ?? 0) + 1)
          return map
        }, new Map<string, number>())

        state.currentBoard.value.columns.forEach((column) => {
          column.cardCount = counts.get(column.id) ?? 0
        })
      }
    } catch (e: unknown) {
      helpers.handleApiError(e, 'Failed to fetch cards')
      throw e
    }
  }

  async function createCard(boardId: string, card: CreateCardDto) {
    helpers.guardDemoMutation()
    try {
      state.loading.value = true
      state.error.value = null
      const newCard = await cardsApi.createCard(boardId, card)
      state.currentBoardCards.value.push(newCard)
      helpers.updateColumnCardCount(newCard.columnId, 1)
      helpers.toast.success(`Card "${newCard.title.trim()}" created successfully`)
      return newCard
    } catch (e: unknown) {
      helpers.handleApiError(e, 'Failed to create card')
      throw e
    } finally {
      state.loading.value = false
    }
  }

  async function updateCard(boardId: string, cardId: string, card: UpdateCardDto) {
    helpers.guardDemoMutation()
    try {
      state.loading.value = true
      state.error.value = null
      const existingCard = state.currentBoardCards.value.find((c) => c.id === cardId)
      const request = {
        ...card,
        expectedUpdatedAt: card.expectedUpdatedAt ?? existingCard?.updatedAt ?? null,
      }
      const updatedCard = await cardsApi.updateCard(boardId, cardId, request)

      // Update the card in the store
      const index = state.currentBoardCards.value.findIndex((c) => c.id === cardId)
      if (index !== -1) {
        state.currentBoardCards.value[index] = updatedCard
      }

      helpers.toast.success('Card updated successfully')
      return updatedCard
    } catch (e: unknown) {
      if (helpers.isHttpConflict(e)) {
        helpers.toast.error(getErrorMessage(e, 'Failed to update card'))
      } else {
        helpers.handleApiError(e, 'Failed to update card')
      }
      throw e
    } finally {
      state.loading.value = false
    }
  }

  async function deleteCard(boardId: string, cardId: string) {
    helpers.guardDemoMutation()
    try {
      state.loading.value = true
      state.error.value = null
      const existingCard = state.currentBoardCards.value.find((card) => card.id === cardId)
      await cardsApi.deleteCard(boardId, cardId)

      // Remove the card from the store
      state.currentBoardCards.value = state.currentBoardCards.value.filter((c) => c.id !== cardId)
      if (state.cardCommentsByCardId.value[cardId]) {
        const { [cardId]: _, ...remainingComments } = state.cardCommentsByCardId.value
        state.cardCommentsByCardId.value = remainingComments
      }

      if (existingCard) {
        helpers.updateColumnCardCount(existingCard.columnId, -1)
      }

      helpers.toast.success('Card deleted successfully')
    } catch (e: unknown) {
      helpers.handleApiError(e, 'Failed to delete card')
      throw e
    } finally {
      state.loading.value = false
    }
  }

  async function moveCard(
    boardId: string,
    cardId: string,
    targetColumnId: string,
    targetPosition: number,
  ) {
    helpers.guardDemoMutation()
    try {
      state.loading.value = true
      state.error.value = null

      const existingCardIndex = state.currentBoardCards.value.findIndex((c) => c.id === cardId)
      const existingCard =
        existingCardIndex !== -1 ? state.currentBoardCards.value[existingCardIndex] : null
      const previousColumnId = existingCard?.columnId ?? null
      const updatedCard = await cardsApi.moveCard(boardId, cardId, {
        targetColumnId,
        targetPosition,
      })

      if (existingCardIndex !== -1) {
        state.currentBoardCards.value.splice(existingCardIndex, 1)
      }

      state.currentBoardCards.value.push(updatedCard)

      if (previousColumnId && previousColumnId !== updatedCard.columnId) {
        helpers.updateColumnCardCount(previousColumnId, -1)
        helpers.updateColumnCardCount(updatedCard.columnId, 1)
      }

      helpers.toast.success('Card moved successfully')
      return updatedCard
    } catch (e: unknown) {
      helpers.handleApiError(e, 'Failed to move card')
      throw e
    } finally {
      state.loading.value = false
    }
  }

  async function fetchCardProvenance(
    boardId: string,
    cardId: string,
  ): Promise<CardCaptureProvenance | null> {
    if (helpers.isDemoMode) return null
    try {
      return await cardsApi.getCardProvenance(boardId, cardId)
    } catch (e: unknown) {
      if (helpers.isHttpNotFound(e)) {
        return null
      }

      helpers.handleApiError(e, 'Failed to fetch card provenance')
      throw e
    }
  }

  return {
    fetchCards,
    createCard,
    updateCard,
    deleteCard,
    moveCard,
    fetchCardProvenance,
  }
}
