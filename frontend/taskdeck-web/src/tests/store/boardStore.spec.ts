import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useBoardStore } from '../../store/boardStore'
import { boardsApi } from '../../api/boardsApi'
import { cardsApi } from '../../api/cardsApi'
import { cardCommentsApi } from '../../api/cardCommentsApi'
import { columnsApi } from '../../api/columnsApi'
import { labelsApi } from '../../api/labelsApi'
import type { Board, Card, Column, Label } from '../../types/board'
import type { CardComment } from '../../types/comments'

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

vi.mock('../../api/cardCommentsApi', () => ({
  cardCommentsApi: {
    getComments: vi.fn(),
    createComment: vi.fn(),
    updateComment: vi.fn(),
    deleteComment: vi.fn(),
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

      // currentBoard should remain null — a Board (without columns) is not a valid BoardDetail
      expect(store.currentBoard).toBeNull()
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
      expect(cardsApi.updateCard).toHaveBeenCalledWith(
        'board-1',
        'card-1',
        expect.objectContaining({
          expectedUpdatedAt: card1.updatedAt,
        }),
      )
    })
  })

  describe('presence state', () => {
    it('should update presence members and editing card state', () => {
      store.setBoardPresenceMembers([
        { userId: 'user-1', displayName: 'Tester', editingCardId: null },
      ])
      store.setEditingCard('card-1')

      expect(store.boardPresenceMembers).toEqual([
        { userId: 'user-1', displayName: 'Tester', editingCardId: null },
      ])
      expect(store.editingCardId).toBe('card-1')
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

  describe('card comment actions', () => {
    it('should return empty card comments when none are cached', () => {
      expect(store.getCardComments('missing-card')).toEqual([])
    })

    it('should fetch and cache comments per card', async () => {
      const comment: CardComment = {
        id: 'comment-1',
        boardId: 'board-1',
        cardId: 'card-1',
        parentCommentId: null,
        authorUserId: 'user-1',
        authorUsername: 'user_one',
        content: 'Test comment',
        isDeleted: false,
        editedAt: null,
        mentions: [],
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      }

      vi.mocked(cardCommentsApi.getComments).mockResolvedValue([comment])

      const result = await store.fetchCardComments('board-1', 'card-1')

      expect(result).toEqual([comment])
      expect(store.getCardComments('card-1')).toEqual([comment])
    })

    it('should propagate errors when fetching card comments', async () => {
      const fetchError = new Error('fetch failed')
      vi.mocked(cardCommentsApi.getComments).mockRejectedValue(fetchError)

      await expect(store.fetchCardComments('board-1', 'card-1')).rejects.toThrow('fetch failed')

      expect(store.error).toBe('fetch failed')
    })

    it('should create and update comment state', async () => {
      const createdComment: CardComment = {
        id: 'comment-1',
        boardId: 'board-1',
        cardId: 'card-1',
        parentCommentId: null,
        authorUserId: 'user-1',
        authorUsername: 'user_one',
        content: 'Created comment',
        isDeleted: false,
        editedAt: null,
        mentions: [],
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      }

      const updatedComment: CardComment = {
        ...createdComment,
        content: 'Updated comment',
        editedAt: new Date().toISOString(),
      }

      vi.mocked(cardCommentsApi.createComment).mockResolvedValue(createdComment)
      vi.mocked(cardCommentsApi.updateComment).mockResolvedValue(updatedComment)

      await store.createCardComment('board-1', 'card-1', { content: 'Created comment' })
      expect(store.getCardComments('card-1')).toEqual([createdComment])

      await store.updateCardComment('board-1', 'card-1', 'comment-1', { content: 'Updated comment' })
      expect(store.getCardComments('card-1')).toEqual([updatedComment])
    })

    it('should propagate errors when creating card comments', async () => {
      const creationError = new Error('create failed')
      vi.mocked(cardCommentsApi.createComment).mockRejectedValue(creationError)

      await expect(store.createCardComment('board-1', 'card-1', { content: 'Create comment' })).rejects.toThrow('create failed')

      expect(store.error).toBe('create failed')
      expect(store.loading).toBe(false)
    })

    it('should propagate errors when updating card comments', async () => {
      const updateError = new Error('update failed')
      vi.mocked(cardCommentsApi.updateComment).mockRejectedValue(updateError)

      await expect(store.updateCardComment('board-1', 'card-1', 'comment-1', { content: 'Update comment' })).rejects.toThrow('update failed')

      expect(store.error).toBe('update failed')
      expect(store.loading).toBe(false)
    })

    it('should delete a comment and remove it from local state', async () => {
      const comment1: CardComment = {
        id: 'comment-1',
        boardId: 'board-1',
        cardId: 'card-1',
        parentCommentId: null,
        authorUserId: 'user-1',
        authorUsername: 'user_one',
        content: 'First comment',
        isDeleted: false,
        editedAt: null,
        mentions: [],
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      }
      const comment2: CardComment = {
        id: 'comment-2',
        boardId: 'board-1',
        cardId: 'card-1',
        parentCommentId: null,
        authorUserId: 'user-2',
        authorUsername: 'user_two',
        content: 'Remaining comment',
        isDeleted: false,
        editedAt: null,
        mentions: [],
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      }

      // Pre-populate comments in store
      vi.mocked(cardCommentsApi.getComments).mockResolvedValue([comment1, comment2])
      await store.fetchCardComments('board-1', 'card-1')

      vi.mocked(cardCommentsApi.deleteComment).mockResolvedValue()

      await store.deleteCardComment('board-1', 'card-1', 'comment-1')

      expect(cardCommentsApi.deleteComment).toHaveBeenCalledWith('board-1', 'card-1', 'comment-1')
      // Should remove locally without re-fetching
      expect(store.getCardComments('card-1')).toEqual([comment2])
      expect(store.loading).toBe(false)
      expect(store.error).toBeNull()
    })

    it('should propagate errors when deleting card comments', async () => {
      const deletionError = new Error('delete failed')
      vi.mocked(cardCommentsApi.deleteComment).mockRejectedValue(deletionError)

      await expect(store.deleteCardComment('board-1', 'card-1', 'comment-1')).rejects.toThrow('delete failed')

      expect(store.error).toBe('delete failed')
      expect(store.loading).toBe(false)
    })
  })

  describe('activeBoardId — preserveSelection guard', () => {
    const boardA: Board = {
      id: 'board-a',
      name: 'Board A',
      description: '',
      isArchived: false,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      columns: [],
    }

    const boardB: Board = {
      id: 'board-b',
      name: 'Board B',
      description: '',
      isArchived: false,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      columns: [],
    }

    it('sets activeBoardId to first board on initial fetchBoards when no prior selection', async () => {
      vi.mocked(boardsApi.getBoards).mockResolvedValue([boardA, boardB])

      await store.fetchBoards()

      expect(store.activeBoardId).toBe('board-a')
    })

    it('preserves activeBoardId across fetchBoards when selected board still exists', async () => {
      vi.useFakeTimers()
      // First load — sets selection to boardA (first item)
      vi.mocked(boardsApi.getBoards).mockResolvedValue([boardA, boardB])
      await store.fetchBoards()
      expect(store.activeBoardId).toBe('board-a')

      // User selects boardB
      store.activeBoardId = 'board-b'

      // Advance past throttle window so the next fetchBoards is not suppressed.
      vi.advanceTimersByTime(6000)

      // Poll cycle returns boards in a different order — boardB is still present
      vi.mocked(boardsApi.getBoards).mockResolvedValue([boardB, boardA])
      await store.fetchBoards()

      // Selection must NOT flip back to boardB (now first) or boardA
      expect(store.activeBoardId).toBe('board-b')
      vi.useRealTimers()
    })

    it('falls back to first board when the selected board is removed from the list', async () => {
      vi.useFakeTimers()
      vi.mocked(boardsApi.getBoards).mockResolvedValue([boardA, boardB])
      await store.fetchBoards()
      store.activeBoardId = 'board-b'

      // Advance past throttle window so the next fetchBoards is not suppressed.
      vi.advanceTimersByTime(6000)

      // boardB has been deleted on the server
      vi.mocked(boardsApi.getBoards).mockResolvedValue([boardA])
      await store.fetchBoards()

      expect(store.activeBoardId).toBe('board-a')
      vi.useRealTimers()
    })

    it('sets activeBoardId to null when no boards remain after refresh', async () => {
      vi.useFakeTimers()
      vi.mocked(boardsApi.getBoards).mockResolvedValue([boardA])
      await store.fetchBoards()
      store.activeBoardId = 'board-a'

      // Advance past throttle window so the next fetchBoards is not suppressed.
      vi.advanceTimersByTime(6000)

      vi.mocked(boardsApi.getBoards).mockResolvedValue([])
      await store.fetchBoards()

      expect(store.activeBoardId).toBeNull()
      vi.useRealTimers()
    })

    it('clears activeBoardId when the active board is deleted', async () => {
      vi.mocked(boardsApi.getBoards).mockResolvedValue([boardA, boardB])
      await store.fetchBoards()
      store.activeBoardId = 'board-a'

      vi.mocked(boardsApi.deleteBoard).mockResolvedValue()
      await store.deleteBoard('board-a')

      // activeBoardId should fall back to the remaining board
      expect(store.activeBoardId).toBe('board-b')
    })
  })
})
