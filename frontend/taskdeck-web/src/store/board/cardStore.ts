/**
 * Card operations: fetch, create, update, delete, move cards, and provenance.
 */
import { watch } from 'vue'
import { cardsApi } from '../../api/cardsApi'
import { getErrorMessage } from '../../utils/errorMessage'
import type { CardDetachPreview, CreateCardDto, UpdateCardDto, CardCaptureProvenance } from '../../types/board'
import type { BoardState } from './boardState'
import type { BoardHelpers } from './boardStoreHelpers'
import type { BoardFetchOptions } from './boardCrudStore'

interface CardMutationVisit {
  boardId: string
  generation: number
}

class StaleBoardVisitError extends Error {
  constructor() {
    super('The board visit that queued this card change has ended.')
    this.name = 'StaleBoardVisitError'
  }
}

export function createCardActions(
  state: BoardState,
  helpers: BoardHelpers,
  refreshBoard: (boardId: string, options?: BoardFetchOptions) => Promise<boolean>,
) {
  // Move and delete target the same durable card and neither API exposes a
  // shared client mutation token. Serialize only that per-card lane so server
  // commit order follows user intent, while unrelated cards remain concurrent.
  // The visit generation prevents a queued pre-logout intent from starting
  // under a later session's credentials.
  const mutationTailByCardId = new Map<string, Promise<void>>()
  let boardVisitGeneration = 0

  watch(
    () => state.currentBoard.value?.id ?? null,
    (nextBoardId, previousBoardId) => {
      if (nextBoardId !== previousBoardId) boardVisitGeneration++
    },
    { flush: 'sync' },
  )

  function captureCardMutationVisit(boardId: string): CardMutationVisit {
    return { boardId, generation: boardVisitGeneration }
  }

  function isCurrentCardMutationVisit(visit: CardMutationVisit) {
    const currentBoard = state.currentBoard.value
    return (
      (currentBoard === null || currentBoard.id === visit.boardId) &&
      boardVisitGeneration === visit.generation
    )
  }

  async function runCardMutation<T>(
    cardId: string,
    visit: CardMutationVisit,
    mutation: () => Promise<T>,
  ): Promise<T> {
    const previous = mutationTailByCardId.get(cardId)
    let operation: Promise<T>

    if (previous) {
      operation = previous.catch(() => undefined).then(() => {
        if (!isCurrentCardMutationVisit(visit)) throw new StaleBoardVisitError()
        return mutation()
      })
    } else {
      // The first intent is already submitted by the caller; do not defer its
      // transport to a microtask where immediate navigation could cancel it.
      if (!isCurrentCardMutationVisit(visit)) throw new StaleBoardVisitError()
      operation = mutation()
    }

    const tail = operation.then(
      () => undefined,
      () => undefined,
    )
    mutationTailByCardId.set(cardId, tail)

    try {
      return await operation
    } finally {
      if (mutationTailByCardId.get(cardId) === tail) {
        mutationTailByCardId.delete(cardId)
      }
    }
  }

  function isOlderCardSnapshot(candidateUpdatedAt: string, currentUpdatedAt: string) {
    const candidateTime = Date.parse(candidateUpdatedAt)
    const currentTime = Date.parse(currentUpdatedAt)
    return Number.isFinite(candidateTime) &&
      Number.isFinite(currentTime) &&
      candidateTime < currentTime
  }

  async function refreshDetachedChildren(boardId: string) {
    // The mutation already committed. It only changes hierarchy ownership, not
    // surviving comment threads, so keep the open editor's same-board cache
    // reachable while the shared reader installs the authoritative child list.
    // The detail reader still owns cancellation, session/navigation generations,
    // cross-board cache clearing and any current-context refresh warning.
    await refreshBoard(boardId, {
      intent: 'background',
      preserveCardComments: true,
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
      // A board-detail refresh can commit the created card while this POST is
      // still resolving. Keep that newer snapshot intact instead of appending a
      // second copy or advancing its column count again. A response that
      // reaches a different selected board likewise belongs to the board that
      // initiated it. A fresh store has no detail yet and still seeds its cards.
      if (
        (state.currentBoard.value === null || state.currentBoard.value.id === boardId) &&
        !state.currentBoardCards.value.some((existing) => existing.id === newCard.id)
      ) {
        state.currentBoardCards.value.push(newCard)
        helpers.updateColumnCardCount(newCard.columnId, 1)
      }
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
    const visit = captureCardMutationVisit(boardId)
    return runCardMutation(cardId, visit, async () => {
      let refreshChildren = false
      try {
        state.loading.value = true
        state.error.value = null
        await cardsApi.deleteCard(boardId, cardId, confirmation)
        helpers.markBoardDetailMutation(boardId)

        // A move, realtime refresh, or navigation can replace this state while
        // the DELETE is in flight. Commit only into the exact initiating board
        // visit and derive the count delta from the card that exists NOW. If an
        // authoritative refresh already removed it, its count is already settled.
        if (isCurrentCardMutationVisit(visit)) {
          const committedCard = state.currentBoardCards.value.find((card) => card.id === cardId)
          state.currentBoardCards.value = state.currentBoardCards.value.filter((card) => card.id !== cardId)
          if (state.cardCommentsByCardId.value[cardId]) {
            const { [cardId]: _, ...remainingComments } = state.cardCommentsByCardId.value
            state.cardCommentsByCardId.value = remainingComments
          }

          if (committedCard) {
            helpers.updateColumnCardCount(committedCard.columnId, -1)
          }

          refreshChildren = state.currentBoard.value?.id === boardId &&
            state.currentBoardCards.value.some(card => card.parentCardId === cardId)
          helpers.toast.success('Card deleted successfully')
        }
      } catch (e: unknown) {
        helpers.handleApiError(e, 'Failed to delete card')
        throw e
      } finally {
        state.loading.value = false
      }
      // Finish mutation-owned loading/error writes before a refresh can outlive navigation.
      if (refreshChildren) await refreshDetachedChildren(boardId)
    })
  }

  async function moveCard(
    boardId: string,
    cardId: string,
    targetColumnId: string,
    targetPosition: number,
  ) {
    helpers.guardDemoMutation()
    const visit = captureCardMutationVisit(boardId)
    return runCardMutation(cardId, visit, async () => {
      try {
        state.loading.value = true
        state.error.value = null

        const updatedCard = await cardsApi.moveCard(boardId, cardId, {
          targetColumnId,
          targetPosition,
        })
        helpers.markBoardDetailMutation(boardId)

        if (!isCurrentCardMutationVisit(visit)) {
          return updatedCard
        }

        // Resolve the committed card after the await. Its current column owns
        // any count delta; a pre-request snapshot has no settlement authority.
        const commitIndex = state.currentBoardCards.value.findIndex((card) => card.id === cardId)
        if (commitIndex === -1) {
          // A later delete or authoritative refresh removed the card. Never
          // resurrect it from an older move response. Preserve the historical
          // null-board preload behavior only when no board session exists yet.
          if (state.currentBoard.value === null && state.currentBoardCards.value.length === 0) {
            state.currentBoardCards.value.push(updatedCard)
            helpers.toast.success('Card moved successfully')
          }
          return updatedCard
        }

        const committedCard = state.currentBoardCards.value[commitIndex]
        if (isOlderCardSnapshot(updatedCard.updatedAt, committedCard.updatedAt)) {
          return updatedCard
        }

        state.currentBoardCards.value[commitIndex] = updatedCard
        if (committedCard.columnId !== updatedCard.columnId) {
          helpers.updateColumnCardCount(committedCard.columnId, -1)
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
    })
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
