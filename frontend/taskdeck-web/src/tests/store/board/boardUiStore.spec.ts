import { describe, expect, it } from 'vitest'
import { ref } from 'vue'
import { createBoardUiActions } from '../../../store/board/boardUiStore'

function createMockState() {
  return {
    boardPresenceMembers: ref<Array<{ userId: string }>>([]),
    editingCardId: ref<string | null>(null),
  }
}

describe('boardUiStore', () => {
  describe('setBoardPresenceMembers', () => {
    it('sets the members array', () => {
      const state = createMockState()
      const { setBoardPresenceMembers } = createBoardUiActions(state as any)
      const members = [{ userId: 'u1' }, { userId: 'u2' }]
      setBoardPresenceMembers(members as any)
      expect(state.boardPresenceMembers.value).toEqual(members)
    })

    it('clears members with empty array', () => {
      const state = createMockState()
      state.boardPresenceMembers.value = [{ userId: 'u1' }]
      const { setBoardPresenceMembers } = createBoardUiActions(state as any)
      setBoardPresenceMembers([])
      expect(state.boardPresenceMembers.value).toEqual([])
    })
  })

  describe('setEditingCard', () => {
    it('sets card id', () => {
      const state = createMockState()
      const { setEditingCard } = createBoardUiActions(state as any)
      setEditingCard('card-42')
      expect(state.editingCardId.value).toBe('card-42')
    })

    it('clears with null', () => {
      const state = createMockState()
      state.editingCardId.value = 'card-42'
      const { setEditingCard } = createBoardUiActions(state as any)
      setEditingCard(null)
      expect(state.editingCardId.value).toBeNull()
    })
  })
})
