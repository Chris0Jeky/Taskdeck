/**
 * Card operations: fetch, create, update, delete, move cards, and provenance.
 */
import { cardsApi } from '../../api/cardsApi'
import { getErrorMessage } from '../../utils/errorMessage'
import type { CardDetachPreview, CreateCardDto, UpdateCardDto, CardCaptureProvenance } from '../../types/board'
import type { BoardState } from './boardState'
import type { BoardHelpers } from './boardStoreHelpers'
import type { BoardFetchOptions } from './boardCrudStore'

export function createCardActions(
  state: BoardState,
  helpers: BoardHelpers,
  refreshBoard: (boardId: string, options?: BoardFetchOptions) => Promise<boolean>,
) {
  async function refreshDetachedChildren(boardId: string) {
    // The mutation already committed. The shared detail reader owns cancellation,
    // session/navigation generations and any current-context refresh warning.
    await refreshBoard(boardId, {
      intent: 'background',
      backgroundFailureMessage: 'Card change saved, but child links could not be refreshed. Refresh the board before editing.',
    })
  }
  async function setCardArchived(boardId: string, cardId: string, archive: boolean, expectedUpdatedAt: string, expectedChildrenFingerprint?: string) {
    helpers.guardDemoMutation()
    const updated = expectedChildrenFingerprint === undefined
      ? await cardsApi.setArchived(boardId, cardId, archive, expectedUpdatedAt)
      : await cardsApi.setArchived(boardId, cardId, archive, expectedUpdatedAt, expectedChildrenFingerprint)
    helpers.markBoardDetailMutation(boardId)
    if (state.currentBoard.value?.id === boardId) {
      const existed = state.currentBoardCards.value.some(card => card.id === cardId)
      state.currentBoardCards.value = state.currentBoardCards.value.filter(card => card.id !== cardId)
      if (!archive) state.currentBoardCards.value.push(updated)
      if (archive && existed) helpers.updateColumnCardCount(updated.columnId, -1)
      if (!archive && !existed) helpers.updateColumnCardCount(updated.columnId, 1)
    }
    if (archive && state.currentBoard.value?.id === boardId && state.currentBoardCards.value.some(card => card.parentCardId === cardId)) await refreshDetachedChildren(boardId)
    return updated
  }
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
      helpers.markBoardDetailMutation(boardId)
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
      helpers.markBoardDetailMutation(boardId)

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

  async function deleteCard(boardId: string, cardId: string, confirmation?: CardDetachPreview) {
    helpers.guardDemoMutation()
    let refreshChildren: boolean
    try {
      state.loading.value = true
      state.error.value = null
      const existingCard = state.currentBoardCards.value.find((card) => card.id === cardId)
      await cardsApi.deleteCard(boardId, cardId, confirmation)
      helpers.markBoardDetailMutation(boardId)

      // Remove the card from the store
      state.currentBoardCards.value = state.currentBoardCards.value.filter((c) => c.id !== cardId)
      if (state.cardCommentsByCardId.value[cardId]) {
        const { [cardId]: _, ...remainingComments } = state.cardCommentsByCardId.value
        state.cardCommentsByCardId.value = remainingComments
      }

      if (existingCard) {
        helpers.updateColumnCardCount(existingCard.columnId, -1)
      }

      refreshChildren = state.currentBoard.value?.id === boardId && state.currentBoardCards.value.some(card => card.parentCardId === cardId)
      helpers.toast.success('Card deleted successfully')
    } catch (e: unknown) {
      helpers.handleApiError(e, 'Failed to delete card')
      throw e
    } finally {
      state.loading.value = false
    }
    // Finish mutation-owned loading/error writes before a refresh can outlive navigation.
    if (refreshChildren) await refreshDetachedChildren(boardId)
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
      helpers.markBoardDetailMutation(boardId)

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
      // cardsApi.getCardProvenance already returns null for 404 (manual cards have no
      // capture provenance — absence is expected, not exceptional).
      return await cardsApi.getCardProvenance(boardId, cardId)
    } catch (e: unknown) {
      helpers.handleApiError(e, 'Failed to fetch card provenance')
      throw e
    }
  }

  return {
    setCardArchived,
    fetchCards,
    createCard,
    updateCard,
    deleteCard,
    moveCard,
    fetchCardProvenance,
  }
}
