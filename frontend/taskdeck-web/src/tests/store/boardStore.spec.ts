import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useBoardStore } from '../../store/boardStore'
import { boardsApi } from '../../api/boardsApi'
import { cardsApi } from '../../api/cardsApi'
import { columnsApi } from '../../api/columnsApi'
import { labelsApi } from '../../api/labelsApi'
import type { Board, Card, Column, Label } from '../../types/board'

// Mock all API modules
vi.mock('../../api/boardsApi', () => ({
  boardsApi: {
    getBoards: vi.fn(),
    getBoard: vi.fn(),
    createBoard: vi.fn(),
    updateBoard: vi.fn(),
    deleteBoard: vi.fn(),
  },
}))

vi.mock('../../api/cardsApi', () => ({
  cardsApi: {
    getCards: vi.fn(),
    createCard: vi.fn(),
    updateCard: vi.fn(),
    moveCard: vi.fn(),
    deleteCard: vi.fn(),
  },
}))

vi.mock('../../api/columnsApi', () => ({
  columnsApi: {
    createColumn: vi.fn(),
    updateColumn: vi.fn(),
    deleteColumn: vi.fn(),
  },
}))

vi.mock('../../api/labelsApi', () => ({
  labelsApi: {
    getLabels: vi.fn(),
    createLabel: vi.fn(),
    updateLabel: vi.fn(),
    deleteLabel: vi.fn(),
  },
}))

describe('boardStore', () => {
  let store: ReturnType<typeof useBoardStore>

  beforeEach(() => {
    setActivePinia(createPinia())
    store = useBoardStore()
    vi.clearAllMocks()
  })

  describe('fetchBoards', () => {
    it('should fetch and store boards', async () => {
      const mockBoards: Board[] = [
        {
          id: '1',
          name: 'Board 1',
          description: 'Test board 1',
          isArchived: false,
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
          columns: [],
        },
        {
          id: '2',
          name: 'Board 2',
          description: 'Test board 2',
          isArchived: false,
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
          columns: [],
        },
      ]

      vi.mocked(boardsApi.getBoards).mockResolvedValue(mockBoards)

      await store.fetchBoards()

      expect(store.boards).toEqual(mockBoards)
      expect(store.loading).toBe(false)
      expect(store.error).toBeNull()
    })

    it('should handle errors when fetching boards', async () => {
      const errorMessage = 'Failed to fetch boards'
      vi.mocked(boardsApi.getBoards).mockRejectedValue(new Error(errorMessage))

      // The store rethrows the error after setting error state
      await expect(store.fetchBoards()).rejects.toThrow(errorMessage)

      expect(store.boards).toEqual([])
      expect(store.error).toBe(errorMessage)
      expect(store.loading).toBe(false)
    })
  })

  describe('createBoard', () => {
    it('should create a new board and add it to the store', async () => {
      const newBoard: Board = {
        id: '3',
        name: 'New Board',
        description: 'New test board',
        isArchived: false,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        columns: [],
      }

      vi.mocked(boardsApi.createBoard).mockResolvedValue(newBoard)

      const result = await store.createBoard({
        name: 'New Board',
        description: 'New test board',
      })

      expect(result).toEqual(newBoard)
      expect(store.boards).toContainEqual(newBoard)
    })
  })

  describe('updateBoard', () => {
    it('should update an existing board in the store', async () => {
      const existingBoard: Board = {
        id: '1',
        name: 'Original Board',
        description: 'Original description',
        isArchived: false,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        columns: [],
      }

      store.boards = [existingBoard]
      store.currentBoard = existingBoard

      const updatedBoard: Board = {
        ...existingBoard,
        name: 'Updated Board',
        description: 'Updated description',
      }

      vi.mocked(boardsApi.updateBoard).mockResolvedValue(updatedBoard)

      await store.updateBoard('1', {
        name: 'Updated Board',
        description: 'Updated description',
        isArchived: null,
      })

      expect(store.boards[0]).toEqual(updatedBoard)
      expect(store.currentBoard).toEqual(updatedBoard)
    })

    it('should handle board not found in store', async () => {
      const updatedBoard: Board = {
        id: '999',
        name: 'Updated Board',
        description: 'Updated description',
        isArchived: false,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        columns: [],
      }

      vi.mocked(boardsApi.updateBoard).mockResolvedValue(updatedBoard)

      await store.updateBoard('999', {
        name: 'Updated Board',
        description: 'Updated description',
        isArchived: null,
      })

      // Should still update currentBoard even if not in boards array
      expect(store.currentBoard).toEqual(updatedBoard)
    })
  })

  describe('deleteBoard', () => {
    it('should delete a board from the store', async () => {
      const board1: Board = {
        id: '1',
        name: 'Board 1',
        description: 'Test board 1',
        isArchived: false,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        columns: [],
      }

      const board2: Board = {
        id: '2',
        name: 'Board 2',
        description: 'Test board 2',
        isArchived: false,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        columns: [],
      }

      store.boards = [board1, board2]
      store.currentBoard = board1

      vi.mocked(boardsApi.deleteBoard).mockResolvedValue()

      await store.deleteBoard('1')

      expect(store.boards).toEqual([board2])
      expect(store.currentBoard).toBeNull()
    })
  })

  describe('updateCard', () => {
    it('should update a card in the store', async () => {
      const card1: Card = {
        id: 'card-1',
        boardId: 'board-1',
        columnId: 'column-1',
        title: 'Original Title',
        description: 'Original description',
        position: 0,
        dueDate: null,
        isBlocked: false,
        blockReason: null,
        labels: [],
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      }

      store.currentBoardCards = [card1]

      const updatedCard: Card = {
        ...card1,
        title: 'Updated Title',
        description: 'Updated description',
      }

      vi.mocked(cardsApi.updateCard).mockResolvedValue(updatedCard)

      const result = await store.updateCard('board-1', 'card-1', {
        title: 'Updated Title',
        description: 'Updated description',
        dueDate: null,
        isBlocked: null,
        blockReason: null,
        labelIds: null,
      })

      expect(result).toEqual(updatedCard)
      expect(store.currentBoardCards[0]).toEqual(updatedCard)
    })
  })

  describe('deleteCard', () => {
    it('should delete a card from the store', async () => {
      const card1: Card = {
        id: 'card-1',
        boardId: 'board-1',
        columnId: 'column-1',
        title: 'Card 1',
        description: '',
        position: 0,
        dueDate: null,
        isBlocked: false,
        blockReason: null,
        labels: [],
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      }

      const card2: Card = {
        id: 'card-2',
        boardId: 'board-1',
        columnId: 'column-1',
        title: 'Card 2',
        description: '',
        position: 1,
        dueDate: null,
        isBlocked: false,
        blockReason: null,
        labels: [],
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      }

      store.currentBoardCards = [card1, card2]

      vi.mocked(cardsApi.deleteCard).mockResolvedValue()

      await store.deleteCard('board-1', 'card-1')

      expect(store.currentBoardCards).toEqual([card2])
    })
  })

  describe('updateColumn', () => {
    it('should update a column in the current board', async () => {
      const column1: Column = {
        id: 'column-1',
        boardId: 'board-1',
        name: 'Original Column',
        position: 0,
        wipLimit: null,
        cardCount: 0,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      }

      const board: Board = {
        id: 'board-1',
        name: 'Board 1',
        description: '',
        isArchived: false,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        columns: [column1],
      }

      store.currentBoard = board

      const updatedColumn: Column = {
        ...column1,
        name: 'Updated Column',
        wipLimit: 5,
      }

      vi.mocked(columnsApi.updateColumn).mockResolvedValue(updatedColumn)

      await store.updateColumn('board-1', 'column-1', {
        name: 'Updated Column',
        wipLimit: 5,
        position: null,
      })

      expect(store.currentBoard?.columns[0]).toEqual(updatedColumn)
    })
  })

  describe('deleteColumn', () => {
    it('should delete a column from the current board', async () => {
      const column1: Column = {
        id: 'column-1',
        boardId: 'board-1',
        name: 'Column 1',
        position: 0,
        wipLimit: null,
        cardCount: 0,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      }

      const column2: Column = {
        id: 'column-2',
        boardId: 'board-1',
        name: 'Column 2',
        position: 1,
        wipLimit: null,
        cardCount: 0,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      }

      const board: Board = {
        id: 'board-1',
        name: 'Board 1',
        description: '',
        isArchived: false,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        columns: [column1, column2],
      }

      store.currentBoard = board

      vi.mocked(columnsApi.deleteColumn).mockResolvedValue()

      await store.deleteColumn('board-1', 'column-1')

      expect(store.currentBoard?.columns).toEqual([column2])
    })
  })

  describe('createLabel', () => {
    it('should create a new label and add it to the store', async () => {
      const newLabel: Label = {
        id: 'label-1',
        boardId: 'board-1',
        name: 'Bug',
        colorHex: '#EF4444',
        createdAt: new Date().toISOString(),
      }

      store.currentBoardLabels = []

      vi.mocked(labelsApi.createLabel).mockResolvedValue(newLabel)

      const result = await store.createLabel('board-1', {
        name: 'Bug',
        colorHex: '#EF4444',
      })

      expect(result).toEqual(newLabel)
      expect(store.currentBoardLabels).toContainEqual(newLabel)
    })
  })

  describe('updateLabel', () => {
    it('should update a label in the store', async () => {
      const label1: Label = {
        id: 'label-1',
        boardId: 'board-1',
        name: 'Original Label',
        colorHex: '#EF4444',
        createdAt: new Date().toISOString(),
      }

      store.currentBoardLabels = [label1]

      const updatedLabel: Label = {
        ...label1,
        name: 'Updated Label',
        colorHex: '#10B981',
      }

      vi.mocked(labelsApi.updateLabel).mockResolvedValue(updatedLabel)

      await store.updateLabel('board-1', 'label-1', {
        name: 'Updated Label',
        colorHex: '#10B981',
      })

      expect(store.currentBoardLabels[0]).toEqual(updatedLabel)
    })
  })

  describe('deleteLabel', () => {
    it('should delete a label from the store', async () => {
      const label1: Label = {
        id: 'label-1',
        boardId: 'board-1',
        name: 'Label 1',
        colorHex: '#EF4444',
        createdAt: new Date().toISOString(),
      }

      const label2: Label = {
        id: 'label-2',
        boardId: 'board-1',
        name: 'Label 2',
        colorHex: '#10B981',
        createdAt: new Date().toISOString(),
      }

      store.currentBoardLabels = [label1, label2]

      vi.mocked(labelsApi.deleteLabel).mockResolvedValue()

      await store.deleteLabel('board-1', 'label-1')

      expect(store.currentBoardLabels).toEqual([label2])
    })
  })

  describe('cardsByColumn computed property', () => {
    it('should group cards by column and sort by position', () => {
      const card1: Card = {
        id: 'card-1',
        boardId: 'board-1',
        columnId: 'column-1',
        title: 'Card 1',
        description: '',
        position: 1,
        dueDate: null,
        isBlocked: false,
        blockReason: null,
        labels: [],
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      }

      const card2: Card = {
        id: 'card-2',
        boardId: 'board-1',
        columnId: 'column-1',
        title: 'Card 2',
        description: '',
        position: 0,
        dueDate: null,
        isBlocked: false,
        blockReason: null,
        labels: [],
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      }

      const card3: Card = {
        id: 'card-3',
        boardId: 'board-1',
        columnId: 'column-2',
        title: 'Card 3',
        description: '',
        position: 0,
        dueDate: null,
        isBlocked: false,
        blockReason: null,
        labels: [],
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      }

      store.currentBoardCards = [card1, card2, card3]

      const grouped = store.cardsByColumn

      expect(grouped.get('column-1')).toEqual([card2, card1])
      expect(grouped.get('column-2')).toEqual([card3])
    })
  })
})
