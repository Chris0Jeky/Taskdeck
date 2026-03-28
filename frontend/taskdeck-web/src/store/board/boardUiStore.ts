/**
 * Board UI state: presence members, editing card state.
 */
import type { BoardPresenceMember } from '../../types/realtime'
import type { BoardState } from './boardState'

export function createBoardUiActions(state: BoardState) {
  function setBoardPresenceMembers(members: BoardPresenceMember[]) {
    state.boardPresenceMembers.value = members
  }

  function setEditingCard(cardId: string | null) {
    state.editingCardId.value = cardId
  }

  return {
    setBoardPresenceMembers,
    setEditingCard,
  }
}
