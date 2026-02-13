import http from './http'
import { buildQueryParams } from './queryBuilder'
import type { Board, BoardDetail, CreateBoardDto, UpdateBoardDto } from '../types/board'

export const boardsApi = {
  async getBoards(search?: string, includeArchived = false): Promise<Board[]> {
    const params = buildQueryParams({
      search,
      includeArchived: includeArchived ? 'true' : undefined,
    })

    const { data } = await http.get<Board[]>(`/boards?${params}`)
    return data
  },

  async getBoard(id: string): Promise<BoardDetail> {
    const { data } = await http.get<BoardDetail>(`/boards/${id}`)
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
