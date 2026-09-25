/**
 * Board UI state: presence members, editing card state.
 */
import type { BoardPresenceMember } from '../../types/realtime'
import type { BoardState, BoardViewVisit } from './boardState'

export function createBoardUiActions(state: BoardState) {
  function beginBoardViewVisit(boardId: string): BoardViewVisit {
    const visit = { boardId }
    state.boardViewVisit.value = visit
    return visit
  }

  function endBoardViewVisit(visit: BoardViewVisit) {
    if (state.boardViewVisit.value === visit) {
      state.boardViewVisit.value = { boardId: null }
    }
  }

  function setBoardPresenceMembers(members: BoardPresenceMember[]) {
    state.boardPresenceMembers.value = members
  }

  function setEditingCard(cardId: string | null) {
    state.editingCardId.value = cardId
  }

  return {
    beginBoardViewVisit,
    endBoardViewVisit,
    setBoardPresenceMembers,
    setEditingCard,
  }
}
