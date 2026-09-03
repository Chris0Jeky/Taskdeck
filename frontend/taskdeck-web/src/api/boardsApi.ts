import http, { type BoardReadOptions } from './http'
import type { Board, BoardDetail, CreateBoardDto, UpdateBoardDto, PaginatedBoards } from '../types/board'

export const boardsApi = {
  async getBoards(search?: string, includeArchived = false, options?: BoardReadOptions): Promise<Board[]> {
    const result = await boardsApi.getBoardsPaginated(search, includeArchived, 0, 200, options)
    return result.items
  },

  async getBoardsPaginated(
    search?: string,
    includeArchived = false,
    offset = 0,
    limit?: number,
    options?: BoardReadOptions,
  ): Promise<PaginatedBoards> {
    const params = new URLSearchParams()
    if (search) params.append('search', search)
    if (includeArchived) params.append('includeArchived', 'true')
    if (offset > 0) params.append('offset', String(offset))
    if (limit !== undefined) params.append('limit', String(limit))

    const url = `/boards?${params}`
    const { data } = options ? await http.get<PaginatedBoards>(url, options) : await http.get<PaginatedBoards>(url)
    return data
  },

  async getBoard(id: string, options?: BoardReadOptions): Promise<BoardDetail> {
    const url = `/boards/${id}`
    const { data } = options ? await http.get<BoardDetail>(url, options) : await http.get<BoardDetail>(url)
    return data
  },

  async createBoard(board: CreateBoardDto): Promise<Board> {
    const { data } = await http.post<Board>('/boards', board)
    return data
  },

  async updateBoard(id: string, board: UpdateBoardDto): Promise<Board> {
    const { data } = await http.put<Board>(`/boards/${id}`, board)
    return data
  },

  async deleteBoard(id: string): Promise<void> {
    await http.delete(`/boards/${id}`)
  },
}
