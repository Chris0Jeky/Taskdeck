import { describe, it, expect, beforeEach, vi } from 'vitest'
import { boardsApi } from '../../api/boardsApi'
import http from '../../api/http'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}))

describe('boardsApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  describe('getBoards', () => {
    it('should fetch boards with default params', async () => {
      const mockBoards = [{ id: '1', name: 'Board 1' }]
      vi.mocked(http.get).mockResolvedValue({ data: mockBoards })

      const result = await boardsApi.getBoards()

      expect(http.get).toHaveBeenCalledWith('/boards?')
      expect(result).toEqual(mockBoards)
    })

    it('should fetch boards with search param', async () => {
      const mockBoards = [{ id: '1', name: 'Test Board' }]
      vi.mocked(http.get).mockResolvedValue({ data: mockBoards })

      const result = await boardsApi.getBoards('test')

      expect(http.get).toHaveBeenCalledWith('/boards?search=test')
      expect(result).toEqual(mockBoards)
    })

    it('should fetch boards with includeArchived param', async () => {
      vi.mocked(http.get).mockResolvedValue({ data: [] })

      await boardsApi.getBoards(undefined, true)

      expect(http.get).toHaveBeenCalledWith('/boards?includeArchived=true')
    })

    it('should fetch boards with search and includeArchived params', async () => {
      vi.mocked(http.get).mockResolvedValue({ data: [] })

      await boardsApi.getBoards('query', true)

      expect(http.get).toHaveBeenCalledWith('/boards?search=query&includeArchived=true')
    })
  })

  describe('getBoard', () => {
    it('should fetch a single board by ID', async () => {
      const mockBoard = { id: 'board-1', name: 'Board 1', columns: [] }
      vi.mocked(http.get).mockResolvedValue({ data: mockBoard })

      const result = await boardsApi.getBoard('board-1')

      expect(http.get).toHaveBeenCalledWith('/boards/board-1')
      expect(result).toEqual(mockBoard)
    })
  })

  describe('createBoard', () => {
    it('should create a board with the provided data', async () => {
      const newBoard = { id: '1', name: 'New Board', description: 'Desc' }
      vi.mocked(http.post).mockResolvedValue({ data: newBoard })

      const result = await boardsApi.createBoard({ name: 'New Board', description: 'Desc' })

      expect(http.post).toHaveBeenCalledWith('/boards', { name: 'New Board', description: 'Desc' })
      expect(result).toEqual(newBoard)
    })
  })

  describe('updateBoard', () => {
    it('should update a board with the provided data', async () => {
      const updatedBoard = { id: '1', name: 'Updated Board' }
      vi.mocked(http.put).mockResolvedValue({ data: updatedBoard })

      const updateData = { name: 'Updated Board', description: null, isArchived: null }
      const result = await boardsApi.updateBoard('1', updateData)

      expect(http.put).toHaveBeenCalledWith('/boards/1', updateData)
      expect(result).toEqual(updatedBoard)
    })
  })

  describe('deleteBoard', () => {
    it('should delete a board by ID', async () => {
      vi.mocked(http.delete).mockResolvedValue({})

      await boardsApi.deleteBoard('board-1')

      expect(http.delete).toHaveBeenCalledWith('/boards/board-1')
    })
  })
})
