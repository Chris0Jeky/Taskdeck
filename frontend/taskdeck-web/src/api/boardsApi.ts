import http from './http'
import type { Board, BoardDetail, CreateBoardDto, UpdateBoardDto } from '../types/board'

export const boardsApi = {
  async getBoards(search?: string, includeArchived = false): Promise<Board[]> {
    const params = new URLSearchParams()
    if (search) params.append('search', search)
    if (includeArchived) params.append('includeArchived', 'true')

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
