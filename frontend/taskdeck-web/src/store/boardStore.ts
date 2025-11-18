import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { boardsApi } from '../api/boardsApi'
import { columnsApi } from '../api/columnsApi'
import { cardsApi } from '../api/cardsApi'
import { labelsApi } from '../api/labelsApi'
import type { Board, BoardDetail, Card, Label, CreateBoardDto, CreateColumnDto, CreateCardDto, CreateLabelDto } from '../types/board'

export const useBoardStore = defineStore('board', () => {
  // State
  const boards = ref<Board[]>([])
  const currentBoard = ref<BoardDetail | null>(null)
  const currentBoardCards = ref<Card[]>([])
  const currentBoardLabels = ref<Label[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)

  // Computed
  const cardsByColumn = computed(() => {
    const map = new Map<string, Card[]>()
    currentBoardCards.value.forEach((card) => {
      if (!map.has(card.columnId)) {
        map.set(card.columnId, [])
      }
      map.get(card.columnId)!.push(card)
    })

    // Sort cards by position within each column
    map.forEach((cards) => {
      cards.sort((a, b) => a.position - b.position)
    })

    return map
  })

  // Actions
  async function fetchBoards(search?: string, includeArchived = false) {
    try {
      loading.value = true
      error.value = null
      boards.value = await boardsApi.getBoards(search, includeArchived)
    } catch (e: any) {
      error.value = e.response?.data?.message || e.message || 'Failed to fetch boards'
      throw e
    } finally {
      loading.value = false
    }
  }

  async function fetchBoard(id: string) {
    try {
      loading.value = true
      error.value = null
      currentBoard.value = await boardsApi.getBoard(id)

      // Fetch cards and labels for the board
      await Promise.all([fetchCards(id), fetchLabels(id)])
    } catch (e: any) {
      error.value = e.response?.data?.message || e.message || 'Failed to fetch board'
      throw e
    } finally {
      loading.value = false
    }
  }

  async function createBoard(board: CreateBoardDto) {
    try {
      loading.value = true
      error.value = null
      const newBoard = await boardsApi.createBoard(board)
      boards.value.push(newBoard)
      return newBoard
    } catch (e: any) {
      error.value = e.response?.data?.message || e.message || 'Failed to create board'
      throw e
    } finally {
      loading.value = false
    }
  }

  async function createColumn(boardId: string, column: CreateColumnDto) {
    try {
      loading.value = true
      error.value = null
      const newColumn = await columnsApi.createColumn(boardId, column)

      if (currentBoard.value && currentBoard.value.id === boardId) {
        currentBoard.value.columns.push(newColumn)
      }

      return newColumn
    } catch (e: any) {
      error.value = e.response?.data?.message || e.message || 'Failed to create column'
      throw e
    } finally {
      loading.value = false
    }
  }

  async function createCard(boardId: string, card: CreateCardDto) {
    try {
      loading.value = true
      error.value = null
      const newCard = await cardsApi.createCard(boardId, card)
      currentBoardCards.value.push(newCard)
      return newCard
    } catch (e: any) {
      error.value = e.response?.data?.message || e.message || 'Failed to create card'
      throw e
    } finally {
      loading.value = false
    }
  }

  async function createLabel(boardId: string, label: CreateLabelDto) {
    try {
      loading.value = true
      error.value = null
      const newLabel = await labelsApi.createLabel(boardId, label)
      currentBoardLabels.value.push(newLabel)
      return newLabel
    } catch (e: any) {
      error.value = e.response?.data?.message || e.message || 'Failed to create label'
      throw e
    } finally {
      loading.value = false
    }
  }

  async function fetchCards(boardId: string, filters?: { search?: string; labelId?: string; columnId?: string }) {
    try {
      currentBoardCards.value = await cardsApi.getCards(boardId, filters)
    } catch (e: any) {
      error.value = e.response?.data?.message || e.message || 'Failed to fetch cards'
      throw e
    }
  }

  async function fetchLabels(boardId: string) {
    try {
      currentBoardLabels.value = await labelsApi.getLabels(boardId)
    } catch (e: any) {
      error.value = e.response?.data?.message || e.message || 'Failed to fetch labels'
      throw e
    }
  }

  async function moveCard(boardId: string, cardId: string, targetColumnId: string, targetPosition: number) {
    try {
      loading.value = true
      error.value = null
      const updatedCard = await cardsApi.moveCard(boardId, cardId, { targetColumnId, targetPosition })

      // Update the card in the store
      const index = currentBoardCards.value.findIndex((c) => c.id === cardId)
      if (index !== -1) {
        currentBoardCards.value[index] = updatedCard
      }

      return updatedCard
    } catch (e: any) {
      error.value = e.response?.data?.message || e.message || 'Failed to move card'
      throw e
    } finally {
      loading.value = false
    }
  }

  return {
    // State
    boards,
    currentBoard,
    currentBoardCards,
    currentBoardLabels,
    loading,
    error,

    // Computed
    cardsByColumn,

    // Actions
    fetchBoards,
    fetchBoard,
    createBoard,
    createColumn,
    createCard,
    createLabel,
    fetchCards,
    fetchLabels,
    moveCard,
  }
})
