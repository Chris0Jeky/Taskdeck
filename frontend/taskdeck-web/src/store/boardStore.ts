import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { boardsApi } from '../api/boardsApi'
import { columnsApi } from '../api/columnsApi'
import { cardsApi } from '../api/cardsApi'
import { labelsApi } from '../api/labelsApi'
import { useToastStore } from './toastStore'
import { getErrorMessage } from '../utils/errorMessage'
import type { BoardPresenceMember } from '../types/realtime'
import type { Board, BoardDetail, Card, Label, CreateBoardDto, CreateColumnDto, CreateCardDto, CreateLabelDto, UpdateCardDto, UpdateBoardDto, UpdateColumnDto, UpdateLabelDto } from '../types/board'

export interface CardFilters {
  searchText: string
  labelIds: string[]
  dueDateFilter: 'all' | 'overdue' | 'due-today' | 'due-week' | 'no-date'
  showBlockedOnly: boolean
}

export const useBoardStore = defineStore('board', () => {
  // Toast notifications
  const toast = useToastStore()

  // State
  const boards = ref<Board[]>([])
  const currentBoard = ref<BoardDetail | null>(null)
  const currentBoardCards = ref<Card[]>([])
  const currentBoardLabels = ref<Label[]>([])
  const boardPresenceMembers = ref<BoardPresenceMember[]>([])
  const editingCardId = ref<string | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  const handleApiError = (err: unknown, fallback: string) => {
    const message = getErrorMessage(err, fallback)
    error.value = message
    toast.error(message)
  }

  // Filter state
  const filters = ref<CardFilters>({
    searchText: '',
    labelIds: [],
    dueDateFilter: 'all',
    showBlockedOnly: false
  })

  const updateColumnCardCount = (columnId: string, delta: number) => {
    if (!currentBoard.value) return

    const column = currentBoard.value.columns.find((c) => c.id === columnId)
    if (!column) return

    const nextCount = (column.cardCount ?? 0) + delta
    column.cardCount = Math.max(0, nextCount)
  }

  // Helper function to check if a card matches current filters
  const cardMatchesFilters = (card: Card): boolean => {
    // Search text filter
    if (filters.value.searchText) {
      const searchLower = filters.value.searchText.toLowerCase()
      const matchesTitle = card.title.toLowerCase().includes(searchLower)
      const matchesDescription = card.description?.toLowerCase().includes(searchLower)
      if (!matchesTitle && !matchesDescription) return false
    }

    // Label filter
    if (filters.value.labelIds.length > 0) {
      const cardLabelIds = card.labels.map(l => l.id)
      const hasMatchingLabel = filters.value.labelIds.some(id => cardLabelIds.includes(id))
      if (!hasMatchingLabel) return false
    }

    // Due date filter
    if (filters.value.dueDateFilter !== 'all') {
      const now = new Date()
      const today = new Date(now.getFullYear(), now.getMonth(), now.getDate())
      const weekFromNow = new Date(today)
      weekFromNow.setDate(weekFromNow.getDate() + 7)

      switch (filters.value.dueDateFilter) {
        case 'overdue':
          if (!card.dueDate || new Date(card.dueDate) >= today) return false
          break
        case 'due-today':
        {
          if (!card.dueDate) return false
          const dueDate = new Date(card.dueDate)
          const dueDateDay = new Date(dueDate.getFullYear(), dueDate.getMonth(), dueDate.getDate())
          if (dueDateDay.getTime() !== today.getTime()) return false
          break
        }
        case 'due-week':
        {
          if (!card.dueDate) return false
          const due = new Date(card.dueDate)
          if (due < today || due > weekFromNow) return false
          break
        }
        case 'no-date':
          if (card.dueDate) return false
          break
      }
    }

    // Blocked status filter
    if (filters.value.showBlockedOnly && !card.isBlocked) {
      return false
    }

    return true
  }

  // Computed
  const cardsByColumn = computed(() => {
    const map = new Map<string, Card[]>()

    // Filter cards first
    const filteredCards = currentBoardCards.value.filter(cardMatchesFilters)

    filteredCards.forEach((card) => {
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

  const filteredCardCount = computed(() => {
    return currentBoardCards.value.filter(cardMatchesFilters).length
  })

  const totalCardCount = computed(() => {
    return currentBoardCards.value.length
  })

  // Actions
  async function fetchBoards(search?: string, includeArchived = false) {
    try {
      loading.value = true
      error.value = null
      boards.value = await boardsApi.getBoards(search, includeArchived)
    } catch (e: unknown) {
      handleApiError(e, 'Failed to fetch boards')
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
    } catch (e: unknown) {
      handleApiError(e, 'Failed to fetch board')
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
      toast.success(`Board "${newBoard.name}" created successfully`)
      return newBoard
    } catch (e: unknown) {
      handleApiError(e, 'Failed to create board')
      throw e
    } finally {
      loading.value = false
    }
  }

  async function updateBoard(boardId: string, board: UpdateBoardDto) {
    try {
      loading.value = true
      error.value = null
      const updatedBoard = await boardsApi.updateBoard(boardId, board)

      // Update in boards list
      const index = boards.value.findIndex((b) => b.id === boardId)
      if (index !== -1) {
        boards.value[index] = updatedBoard
      }

      // Update current board if it's the one being edited
      if (currentBoard.value) {
        if (currentBoard.value.id === boardId) {
          currentBoard.value = { ...currentBoard.value, ...updatedBoard }
        }
      } else {
        currentBoard.value = updatedBoard as BoardDetail
      }

      toast.success('Board updated successfully')
      return updatedBoard
    } catch (e: unknown) {
      handleApiError(e, 'Failed to update board')
      throw e
    } finally {
      loading.value = false
    }
  }

  async function deleteBoard(boardId: string) {
    try {
      loading.value = true
      error.value = null
      await boardsApi.deleteBoard(boardId)

      // Remove from boards list
      boards.value = boards.value.filter((b) => b.id !== boardId)

      // Clear current board if it's the one being deleted
      if (currentBoard.value && currentBoard.value.id === boardId) {
        currentBoard.value = null
        currentBoardCards.value = []
        currentBoardLabels.value = []
        boardPresenceMembers.value = []
        editingCardId.value = null
      }

      toast.success('Board archived successfully')
    } catch (e: unknown) {
      handleApiError(e, 'Failed to archive board')
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

      toast.success(`Column "${newColumn.name}" created successfully`)
      return newColumn
    } catch (e: unknown) {
      handleApiError(e, 'Failed to create column')
      throw e
    } finally {
      loading.value = false
    }
  }

  async function updateColumn(boardId: string, columnId: string, column: UpdateColumnDto) {
    try {
      loading.value = true
      error.value = null
      const updatedColumn = await columnsApi.updateColumn(boardId, columnId, column)

      // Update column in current board
      if (currentBoard.value && currentBoard.value.id === boardId) {
        const index = currentBoard.value.columns.findIndex((c) => c.id === columnId)
        if (index !== -1) {
          currentBoard.value.columns[index] = updatedColumn
        }
      }

      toast.success('Column updated successfully')
      return updatedColumn
    } catch (e: unknown) {
      handleApiError(e, 'Failed to update column')
      throw e
    } finally {
      loading.value = false
    }
  }

  async function deleteColumn(boardId: string, columnId: string) {
    try {
      loading.value = true
      error.value = null
      await columnsApi.deleteColumn(boardId, columnId)

      // Remove column from current board
      if (currentBoard.value && currentBoard.value.id === boardId) {
        currentBoard.value.columns = currentBoard.value.columns.filter((c) => c.id !== columnId)
      }

      // Remove cards from deleted column
      currentBoardCards.value = currentBoardCards.value.filter((card) => card.columnId !== columnId)

      toast.success('Column deleted successfully')
    } catch (e: unknown) {
      handleApiError(e, 'Failed to delete column')
      throw e
    } finally {
      loading.value = false
    }
  }

  async function reorderColumns(boardId: string, columnIds: string[]) {
    try {
      loading.value = true
      error.value = null
      const reorderedColumns = await columnsApi.reorderColumns(boardId, columnIds)

      // Update columns in current board with reordered list
      if (currentBoard.value && currentBoard.value.id === boardId) {
        currentBoard.value.columns = reorderedColumns
      }

      toast.success('Columns reordered successfully')
      return reorderedColumns
    } catch (e: unknown) {
      handleApiError(e, 'Failed to reorder columns')
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
      updateColumnCardCount(newCard.columnId, 1)
      toast.success(`Card "${newCard.title}" created successfully`)
      return newCard
    } catch (e: unknown) {
      handleApiError(e, 'Failed to create card')
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
      toast.success(`Label "${newLabel.name}" created successfully`)
      return newLabel
    } catch (e: unknown) {
      handleApiError(e, 'Failed to create label')
      throw e
    } finally {
      loading.value = false
    }
  }

  async function updateLabel(boardId: string, labelId: string, label: UpdateLabelDto) {
    try {
      loading.value = true
      error.value = null
      const updatedLabel = await labelsApi.updateLabel(boardId, labelId, label)

      // Update label in store
      const index = currentBoardLabels.value.findIndex((l) => l.id === labelId)
      if (index !== -1) {
        currentBoardLabels.value[index] = updatedLabel
      }

      toast.success('Label updated successfully')
      return updatedLabel
    } catch (e: unknown) {
      handleApiError(e, 'Failed to update label')
      throw e
    } finally {
      loading.value = false
    }
  }

  async function deleteLabel(boardId: string, labelId: string) {
    try {
      loading.value = true
      error.value = null
      await labelsApi.deleteLabel(boardId, labelId)

      // Remove label from store
      currentBoardLabels.value = currentBoardLabels.value.filter((l) => l.id !== labelId)

      toast.success('Label deleted successfully')
    } catch (e: unknown) {
      handleApiError(e, 'Failed to delete label')
      throw e
    } finally {
      loading.value = false
    }
  }

  async function fetchCards(boardId: string, filters?: { search?: string; labelId?: string; columnId?: string }) {
    try {
      currentBoardCards.value = await cardsApi.getCards(boardId, filters)

      // Keep column card counts in sync with the latest cards collection
      if (currentBoard.value) {
        const counts = currentBoardCards.value.reduce((map, card) => {
          map.set(card.columnId, (map.get(card.columnId) ?? 0) + 1)
          return map
        }, new Map<string, number>())

        currentBoard.value.columns.forEach((column) => {
          column.cardCount = counts.get(column.id) ?? 0
        })
      }
    } catch (e: unknown) {
      handleApiError(e, 'Failed to fetch cards')
      throw e
    }
  }

  async function fetchLabels(boardId: string) {
    try {
      currentBoardLabels.value = await labelsApi.getLabels(boardId)
    } catch (e: unknown) {
      handleApiError(e, 'Failed to fetch labels')
      throw e
    }
  }

  async function updateCard(boardId: string, cardId: string, card: UpdateCardDto) {
    try {
      loading.value = true
      error.value = null
      const existingCard = currentBoardCards.value.find((c) => c.id === cardId)
      const request = {
        ...card,
        expectedUpdatedAt: card.expectedUpdatedAt ?? existingCard?.updatedAt ?? null,
      }
      const updatedCard = await cardsApi.updateCard(boardId, cardId, request)

      // Update the card in the store
      const index = currentBoardCards.value.findIndex((c) => c.id === cardId)
      if (index !== -1) {
        currentBoardCards.value[index] = updatedCard
      }

      toast.success('Card updated successfully')
      return updatedCard
    } catch (e: unknown) {
      handleApiError(e, 'Failed to update card')
      throw e
    } finally {
      loading.value = false
    }
  }

  async function deleteCard(boardId: string, cardId: string) {
    try {
      loading.value = true
      error.value = null
      const existingCard = currentBoardCards.value.find((card) => card.id === cardId)
      await cardsApi.deleteCard(boardId, cardId)

      // Remove the card from the store
      currentBoardCards.value = currentBoardCards.value.filter((c) => c.id !== cardId)

      if (existingCard) {
        updateColumnCardCount(existingCard.columnId, -1)
      }

      toast.success('Card deleted successfully')
    } catch (e: unknown) {
      handleApiError(e, 'Failed to delete card')
      throw e
    } finally {
      loading.value = false
    }
  }

  async function moveCard(boardId: string, cardId: string, targetColumnId: string, targetPosition: number) {
    try {
      loading.value = true
      error.value = null

      const existingCardIndex = currentBoardCards.value.findIndex((c) => c.id === cardId)
      const existingCard = existingCardIndex !== -1 ? currentBoardCards.value[existingCardIndex] : null
      const previousColumnId = existingCard?.columnId ?? null
      const updatedCard = await cardsApi.moveCard(boardId, cardId, { targetColumnId, targetPosition })

      if (existingCardIndex !== -1) {
        currentBoardCards.value.splice(existingCardIndex, 1)
      }

      currentBoardCards.value.push(updatedCard)

      if (previousColumnId && previousColumnId !== updatedCard.columnId) {
        updateColumnCardCount(previousColumnId, -1)
        updateColumnCardCount(updatedCard.columnId, 1)
      }

      toast.success('Card moved successfully')
      return updatedCard
    } catch (e: unknown) {
      handleApiError(e, 'Failed to move card')
      throw e
    } finally {
      loading.value = false
    }
  }

  // Filter actions
  const updateFilters = (newFilters: CardFilters) => {
    filters.value = { ...newFilters }
  }

  const clearFilters = () => {
    filters.value = {
      searchText: '',
      labelIds: [],
      dueDateFilter: 'all',
      showBlockedOnly: false
    }
  }

  function setBoardPresenceMembers(members: BoardPresenceMember[]) {
    boardPresenceMembers.value = members
  }

  function setEditingCard(cardId: string | null) {
    editingCardId.value = cardId
  }

  return {
    // State
    boards,
    currentBoard,
    currentBoardCards,
    currentBoardLabels,
    boardPresenceMembers,
    editingCardId,
    loading,
    error,
    filters,

    // Computed
    cardsByColumn,
    filteredCardCount,
    totalCardCount,

    // Actions
    fetchBoards,
    fetchBoard,
    createBoard,
    updateBoard,
    deleteBoard,
    createColumn,
    updateColumn,
    deleteColumn,
    reorderColumns,
    createCard,
    updateCard,
    deleteCard,
    createLabel,
    updateLabel,
    deleteLabel,
    updateFilters,
    clearFilters,
    setBoardPresenceMembers,
    setEditingCard,
    fetchCards,
    fetchLabels,
    moveCard,
  }
})
